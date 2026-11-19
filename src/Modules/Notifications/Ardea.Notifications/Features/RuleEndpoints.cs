using Ardea.Assessment.Contracts;
using Ardea.Iam.Contracts;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Audit;
using Ardea.Platform.Email;
using Ardea.Platform.Http;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Notifications.Features;

/// <summary>
/// Rules and their targets (master plan 8.3, 10.2; tasks 5.1, 5.7): owners and admins manage them. Ids in the
/// body (projects, members, addresses, endpoints) are resolved inside the organisation; a foreign one is
/// «not found» as a field error.
/// </summary>
internal static class RuleEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var org = api.MapOrganization().WithTags("Notifications");
        org.MapGet("/notification-channels", ChannelsAsync).WithName("listNotificationChannels").Produces<IReadOnlyList<NotificationChannelView>>().Problems(403, 404);

        var rules = org.MapGroup("/rules").RequireOrganizationRole(OrganizationRole.Admin);
        rules.MapGet("/", ListAsync).WithName("listNotificationRules").Produces<IReadOnlyList<NotificationRuleView>>().Problems(403, 404);
        rules.MapPost("/", CreateAsync).WithName("createNotificationRule").Produces<NotificationRuleView>(StatusCodes.Status201Created).Problems(400, 403, 404);
        rules.MapGet("/{ruleId:guid}", GetAsync).WithName("getNotificationRule").Produces<NotificationRuleView>().Problems(403, 404);
        rules.MapPut("/{ruleId:guid}", ReplaceAsync).WithName("replaceNotificationRule").Produces<NotificationRuleView>().Problems(400, 403, 404);
        rules.MapDelete("/{ruleId:guid}", DeleteAsync).WithName("deleteNotificationRule").NoContent().Problems(403, 404);
    }

    private static async Task<IReadOnlyList<NotificationChannelView>> ChannelsAsync(IEmailSender email, CancellationToken cancellationToken)
    {
        var smtp = await email.IsConfiguredAsync(cancellationToken);
        return
        [
            new(Channels.InApp, true, null),
            new(Channels.Email, smtp, smtp ? null : "smtp_not_configured"),
            new(Channels.Webhook, true, null),
            new(Channels.Push, false, "not_implemented"),
        ];
    }

    private static async Task<IReadOnlyList<NotificationRuleView>> ListAsync(
        NotifyDbContext db, IOrganizationMembers members, IEmailSender email, CancellationToken cancellationToken)
    {
        var rules = await db.Rules.AsNoTracking().Include(r => r.Targets)
            .OrderBy(r => r.CreatedAt).ThenByDescending(r => r.MinLevel).ThenBy(r => r.Name).ToListAsync(cancellationToken);
        var labels = await LabelsAsync(db, members, cancellationToken);
        var smtp = await email.IsConfiguredAsync(cancellationToken);
        return [.. rules.Select(r => View(r, labels, smtp))];
    }

    private static async Task<IResult> GetAsync(Guid ruleId, NotifyDbContext db, IOrganizationMembers members, IEmailSender email, CancellationToken cancellationToken)
    {
        var rule = await db.Rules.AsNoTracking().Include(r => r.Targets).SingleOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        return rule is null
            ? NotFound()
            : Results.Ok(View(rule, await LabelsAsync(db, members, cancellationToken), await email.IsConfiguredAsync(cancellationToken)));
    }

    private static async Task<IResult> CreateAsync(
        NotificationRuleRequest request, NotifyDbContext db, IActionFacts facts, IOrganizationMembers members, IOrganizationProfiles organizations,
        IEmailSender email, ITenantContext tenant, IAuditLog audit, IUnitOfWork unitOfWork, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var rule = new NotificationRule { Id = Guid.CreateVersion7(), Name = string.Empty, CreatedAt = now };
        var errors = await ApplyAsync(rule, request, db, facts, members, organizations, tenant, now, cancellationToken);
        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        db.Rules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("notify.rule_created", "rule", rule.Id.ToString(), new { rule.Name }), cancellationToken);
        var view = View(rule, await LabelsAsync(db, members, cancellationToken), await email.IsConfiguredAsync(cancellationToken));
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/orgs/{tenant.RequiredOrganizationId}/rules/{rule.Id}", view);
    }

    private static async Task<IResult> ReplaceAsync(
        Guid ruleId, NotificationRuleRequest request, NotifyDbContext db, IActionFacts facts, IOrganizationMembers members, IOrganizationProfiles organizations,
        IEmailSender email, ITenantContext tenant, IAuditLog audit, IUnitOfWork unitOfWork, TimeProvider time, CancellationToken cancellationToken)
    {
        var rule = await db.Rules.Include(r => r.Targets).SingleOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return NotFound();
        }

        var now = time.GetUtcNow();
        var errors = await ApplyAsync(rule, request, db, facts, members, organizations, tenant, now, cancellationToken);
        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("notify.rule_changed", "rule", rule.Id.ToString(), new { rule.Name }), cancellationToken);
        var view = View(rule, await LabelsAsync(db, members, cancellationToken), await email.IsConfiguredAsync(cancellationToken));
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(view);
    }

    private static async Task<IResult> DeleteAsync(Guid ruleId, NotifyDbContext db, IAuditLog audit, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        var rule = await db.Rules.SingleOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return NotFound();
        }

        db.Rules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("notify.rule_deleted", "rule", rule.Id.ToString(), new { rule.Name }), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>Validates the request and writes it into the rule; targets keep their id, and their schedule, where they stay the same.</summary>
    private static async Task<Dictionary<string, string[]>> ApplyAsync(
        NotificationRule rule, NotificationRuleRequest request, NotifyDbContext db, IActionFacts facts, IOrganizationMembers members,
        IOrganizationProfiles organizations, ITenantContext tenant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200)
        {
            errors["name"] = ["Enter a name of at most 200 characters."];
        }

        if (request.MinLevel is not (>= 2 and <= 5))
        {
            errors["minLevel"] = ["Choose a level from 2 to 5."];
        }

        var events = (request.Events ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (events.Count == 0 || events.Any(e => !NotifyEvents.All.Contains(e)))
        {
            errors["events"] = [$"Choose one or more of {string.Join(", ", NotifyEvents.All)}."];
        }

        var projects = (request.ProjectIds ?? []).Distinct().ToList();
        if (projects.Count > 0 && (await facts.ProjectNamesAsync(projects, cancellationToken)).Count != projects.Count)
        {
            errors["projectIds"] = ["No such project."];
        }

        var targets = request.Targets ?? [];
        if (targets.Count is 0 or > 20)
        {
            errors["targets"] = ["Add one to twenty targets."];
        }

        var directory = (await members.ListAsync(cancellationToken)).Select(m => m.UserId).ToHashSet();
        var addresses = await db.Addresses.AsNoTracking().Select(a => a.Id).ToListAsync(cancellationToken);
        var endpoints = await db.Endpoints.AsNoTracking().Select(e => e.Id).ToListAsync(cancellationToken);
        for (var i = 0; i < targets.Count; i++)
        {
            if (TargetError(targets[i], directory, addresses, endpoints) is { } error)
            {
                errors[$"targets[{i}]"] = [error];
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var timeZone = (await organizations.FindAsync(tenant.RequiredOrganizationId, cancellationToken))?.TimeZone ?? "UTC";
        rule.Name = name!;
        rule.Enabled = request.Enabled ?? true;
        rule.MinLevel = request.MinLevel!.Value;
        rule.Events = [.. events];
        rule.ProjectIds = [.. projects];
        rule.ChangedAt = now;

        var kept = new List<RuleTarget>();
        var order = 0;
        foreach (var t in targets)
        {
            order++;
            var mode = t.Mode!;
            var cron = mode == "digest" ? t.DigestCron!.Trim() : null;
            var same = rule.Targets.FirstOrDefault(x => !kept.Contains(x) && x.Channel == t.Channel && x.RecipientKind == t.RecipientKind
                && x.RecipientRef == t.RecipientRef!.Trim() && x.Mode == mode && x.DigestCron == cron);
            if (same is not null)
            {
                same.SendEmpty = mode == "digest" && (t.SendEmpty ?? false);
                kept.Add(same);
                continue;
            }

            var target = new RuleTarget
            {
                Id = Guid.CreateVersion7(),
                RuleId = rule.Id,
                Channel = t.Channel!,
                RecipientKind = t.RecipientKind!,
                RecipientRef = t.RecipientRef!.Trim(),
                Mode = mode,
                DigestCron = cron,
                SendEmpty = mode == "digest" && (t.SendEmpty ?? false),
                NextDigestAt = cron is null ? null : DigestSchedule.Next(cron, timeZone, now),
                LastDigestAt = cron is null ? null : now,
                // One microsecond apart, the database's precision: the rule shows its targets in the order given.
                CreatedAt = now.AddTicks(order * 10),
            };
            // Added explicitly: a client-side id would otherwise make EF take the new target for an existing one.
            rule.Targets.Add(target);
            db.Targets.Add(target);
            kept.Add(target);
        }

        foreach (var gone in rule.Targets.Except(kept).ToList())
        {
            rule.Targets.Remove(gone);
            db.Targets.Remove(gone);
        }

        return errors;
    }

    private static string? TargetError(NotificationTargetRequest target, HashSet<Guid> members, List<Guid> addresses, List<Guid> endpoints)
    {
        if (target.Channel is not { } channel || !Channels.Usable.Contains(channel))
        {
            return target.Channel == Channels.Push ? "Web Push is not available yet." : $"Choose a channel: {string.Join(", ", Channels.Usable)}.";
        }

        if (target.RecipientKind is not { } kind || !RecipientKinds.All.Contains(kind) || !RecipientKinds.Reaches(kind, channel))
        {
            return $"This channel reaches {string.Join(", ", RecipientKinds.All.Where(k => RecipientKinds.Reaches(k, channel)))}.";
        }

        var reference = target.RecipientRef?.Trim();
        var found = kind switch
        {
            RecipientKinds.Role => OrganizationRoles.TryParse(reference, out _),
            RecipientKinds.User => Guid.TryParse(reference, out var user) && members.Contains(user),
            RecipientKinds.Address => Guid.TryParse(reference, out var address) && addresses.Contains(address),
            _ => Guid.TryParse(reference, out var endpoint) && endpoints.Contains(endpoint),
        };
        if (!found)
        {
            return kind == RecipientKinds.Role ? "Choose owner, admin, member or viewer." : "No such recipient.";
        }

        return target.Mode switch
        {
            "immediate" => null,
            "digest" when DigestSchedule.TryParse(target.DigestCron, out var cron) && cron!.GetNextOccurrence(DateTime.UtcNow) is not null => null,
            "digest" => "Enter a schedule as a five-field cron expression, e.g. 0 8 * * 1-5.",
            _ => "Choose immediate or digest.",
        };
    }

    private static async Task<Dictionary<(string, string), string>> LabelsAsync(NotifyDbContext db, IOrganizationMembers members, CancellationToken cancellationToken)
    {
        var labels = new Dictionary<(string, string), string>();
        foreach (var m in await members.ListAsync(cancellationToken))
        {
            labels[(RecipientKinds.User, m.UserId.ToString())] = m.DisplayName;
        }

        foreach (var a in await db.Addresses.AsNoTracking().Select(a => new { a.Id, a.Address }).ToListAsync(cancellationToken))
        {
            labels[(RecipientKinds.Address, a.Id.ToString())] = a.Address;
        }

        foreach (var e in await db.Endpoints.AsNoTracking().Select(e => new { e.Id, e.Name }).ToListAsync(cancellationToken))
        {
            labels[(RecipientKinds.Endpoint, e.Id.ToString())] = e.Name;
        }

        return labels;
    }

    private static NotificationRuleView View(NotificationRule rule, Dictionary<(string, string), string> labels, bool smtp) => new(
        rule.Id, rule.Name, rule.Enabled, rule.ProjectIds, rule.MinLevel, rule.Events, rule.Preset,
        [.. rule.Targets.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Select(t => new NotificationTargetView(
            t.Id, t.Channel, t.RecipientKind, t.RecipientRef,
            t.RecipientKind == RecipientKinds.Role ? t.RecipientRef : labels.GetValueOrDefault((t.RecipientKind, t.RecipientRef)),
            t.Mode, t.DigestCron, t.SendEmpty, t.NextDigestAt))],
        !smtp && rule.Targets.Any(t => t.Channel == Channels.Email) ? ["email_unavailable"] : [],
        rule.CreatedAt, rule.ChangedAt);

    private static IResult Invalid(Dictionary<string, string[]> errors) =>
        ApiProblems.Validation("invalid_rule", "The rule is not valid.", errors);

    private static IResult NotFound() => ApiProblems.Problem("rule_not_found", StatusCodes.Status404NotFound, "No such rule.");
}
