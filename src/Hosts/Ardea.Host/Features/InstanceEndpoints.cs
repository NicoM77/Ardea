using System.ComponentModel;
using Ardea.Iam.Contracts;
using Ardea.Platform.Ai;
using Ardea.Platform.Audit;
using Ardea.Platform.Email;
using Ardea.Platform.Http;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Settings;

namespace Ardea.Host.Features;

/// <summary>
/// The first administration (task 0.13, master plan 11.4): registration, SMTP with a test mail,
/// abandoned jobs with retry, the installed version. Instance administrator only.
/// </summary>
internal sealed record InstanceStatusView(IReadOnlyList<Platform.Status.StatusWarning> Warnings, IReadOnlyList<Platform.Status.StatusMetric> Metrics, DateTimeOffset At);

internal static class InstanceEndpoints
{
    /// <summary>The system's warnings about itself and the operating figures (master plan 12.7; task 6.6).</summary>
    private static async Task<InstanceStatusView> StatusAsync(
        IEnumerable<Platform.Status.IInstanceStatusContributor> contributors, TimeProvider time, CancellationToken cancellationToken)
    {
        var status = await CollectAsync(contributors, cancellationToken);
        return new InstanceStatusView(status.Warnings, status.Metrics, time.GetUtcNow());
    }

    /// <summary>Only the warnings, without figures: whether the data shown is current.</summary>
    private static async Task<IReadOnlyList<Platform.Status.StatusWarning>> WarningsAsync(
        IEnumerable<Platform.Status.IInstanceStatusContributor> contributors, CancellationToken cancellationToken) =>
        (await CollectAsync(contributors, cancellationToken)).Warnings;

    private static async Task<Platform.Status.InstanceStatusBuilder> CollectAsync(
        IEnumerable<Platform.Status.IInstanceStatusContributor> contributors, CancellationToken cancellationToken)
    {
        var status = new Platform.Status.InstanceStatusBuilder();
        foreach (var contributor in contributors)
        {
            await contributor.ContributeAsync(status, cancellationToken);
        }

        return status;
    }

    public static void Map(IEndpointRouteBuilder api)
    {
        var instance = api.MapGroup("/instance").WithTags("Administration").RequireAuthorization(IamPolicies.InstanceAdmin);
        instance.MapGet("", GetAsync).WithName("getInstance");
        instance.MapGet("/settings", GetSettingsAsync).WithName("getInstanceSettings");
        instance.MapPatch("/settings", UpdateSettingsAsync).WithName("updateInstanceSettings").Produces<InstanceSettingsResponse>().Problems(400, 409);
        instance.MapPost("/smtp/test", TestSmtpAsync).WithName("sendTestEmail").NoContent().Problems(400, 409, 502);
        instance.MapGet("/ai/providers", (IAiProviders providers) => providers.All).WithName("listAiProviders");
        instance.MapPost("/ai/providers/{name}/test", TestAiProviderAsync).WithName("testAiProvider").Produces<AiConnectionResult>().Problems(404);
        instance.MapGet("/jobs", ListJobsAsync).WithName("listAbandonedJobs");
        instance.MapGet("/status", StatusAsync).WithName("getInstanceStatus").Produces<InstanceStatusView>();

        // Every signed-in user sees whether the data is current (master plan 12.7: «Stand von 02:10, Quelle nicht erreichbar»).
        api.MapGet("/status", WarningsAsync).WithTags("Administration").WithName("getStatusWarnings").RequireAuthorization().Produces<IReadOnlyList<Platform.Status.StatusWarning>>();
        instance.MapPost("/jobs/{jobId:guid}/retry", RetryJobAsync).WithName("retryJob").NoContent().Problems(404);
    }

    private static async Task<InstanceResponse> GetAsync(IJobAdministration jobs, CancellationToken cancellationToken)
    {
        var counts = await jobs.CountAsync(cancellationToken);
        return new InstanceResponse(AppVersion.Current, new JobCountsResponse(counts.Pending, counts.Running, counts.Dead, counts.Failed));
    }

    private static async Task<InstanceSettingsResponse> GetSettingsAsync(IInstanceSettings settings, CancellationToken cancellationToken) =>
        await ReadAsync(settings, cancellationToken);

    private static async Task<IResult> UpdateSettingsAsync(
        UpdateInstanceSettingsRequest request, HttpContext http, IUnitOfWork unitOfWork, IInstanceSettings settings, IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.Registration is { } mode && !InstanceSettingDefinitions.RegistrationModes.Contains(mode))
        {
            errors["registration"] = ["Choose invite, open or closed."];
        }

        if (request.SmtpPort is { Length: > 0 } port && (!int.TryParse(port, out var number) || number is < 1 or > 65535))
        {
            errors["smtpPort"] = ["Enter a port between 1 and 65535."];
        }

        if (request.SmtpSecurity is { Length: > 0 } security && security is not ("starttls" or "tls" or "none"))
        {
            errors["smtpSecurity"] = ["Choose starttls, tls or none."];
        }

        if (request.WebhookInternalHosts is { Length: > 0 } released
            && InstanceSettingDefinitions.Hosts(released).Any(h => Uri.CheckHostName(h) == UriHostNameType.Unknown))
        {
            errors["webhookInternalHosts"] = ["Enter host names or addresses, separated by commas."];
        }

        if (request.SmtpFrom is { Length: > 0 } from && !System.Net.Mail.MailAddress.TryCreate(from, out _))
        {
            errors["smtpFrom"] = ["Enter a valid sender address."];
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation("validation_failed", "Some values are not valid.", errors);
        }

        var changes = new List<(SettingDefinition Definition, string? Value)>();
        void Change(SettingDefinition definition, string? value)
        {
            // Absent leaves a setting as it is; an empty string removes the stored value.
            if (value is not null)
            {
                changes.Add((definition, value.Length == 0 ? null : value.Trim()));
            }
        }

        Change(InstanceSettingDefinitions.Registration, request.Registration);
        Change(SmtpSettings.Host, request.SmtpHost);
        Change(SmtpSettings.Port, request.SmtpPort);
        Change(SmtpSettings.User, request.SmtpUser);
        Change(SmtpSettings.Password, request.SmtpPassword);
        Change(SmtpSettings.From, request.SmtpFrom);
        Change(SmtpSettings.Security, request.SmtpSecurity);
        Change(InstanceSettingDefinitions.WebhookInternalHosts, request.WebhookInternalHosts is { } hosts ? string.Join(",", InstanceSettingDefinitions.Hosts(hosts)) : null);

        var userId = Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        foreach (var (definition, value) in changes)
        {
            if ((await settings.GetAsync(definition, cancellationToken)).Locked)
            {
                return ApiProblems.Problem("setting_locked", StatusCodes.Status409Conflict, $"{definition.Key} is set by an environment variable and cannot be changed here.");
            }

            await settings.SetAsync(definition, value, userId, cancellationToken);
        }

        if (changes.Count > 0)
        {
            // Names only: the audit protocol never holds a secret.
            await audit.RecordAsync(
                new AuditEntry("instance.settings_changed", "setting", null, new { keys = changes.Select(c => c.Definition.Key).ToArray() }) { InstanceEvent = true },
                cancellationToken);
        }

        var result = await ReadAsync(settings, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(result);
    }

    /// <summary>
    /// Asks the provider for a one-word answer (master plan 14, task 4.1). The request opens no transaction while
    /// it waits for the provider; the audit entry is written in its own short one. The result names the kind of
    /// failure, never the key or the request.
    /// </summary>
    private static async Task<IResult> TestAiProviderAsync(string name, IAiProviders providers, IUnitOfWorkFactory units, CancellationToken cancellationToken)
    {
        if (await providers.TestAsync(name, cancellationToken) is not { } result)
        {
            return ApiProblems.Problem("ai_provider_not_found", StatusCodes.Status404NotFound, "No such AI provider is configured.");
        }

        await using var scope = units.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditLog>().RecordAsync(
            new AuditEntry("instance.ai_provider_tested", "ai_provider", name, new { result.Ok, result.DurationMs }) { InstanceEvent = true }, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        return Results.Ok(result);
    }

    /// <summary>Sends a test mail now. The request opens no transaction: the sender reads its settings in its own short one.</summary>
    private static async Task<IResult> TestSmtpAsync(TestEmailRequest request, IEmailSender email, IUnitOfWorkFactory units, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.To) || !System.Net.Mail.MailAddress.TryCreate(request.To, out _))
        {
            return ApiProblems.Validation("validation_failed", "Enter a valid recipient.", new Dictionary<string, string[]> { ["to"] = ["Enter a valid e-mail address."] });
        }

        if (!await email.IsConfiguredAsync(cancellationToken))
        {
            return ApiProblems.Problem("smtp_not_configured", StatusCodes.Status409Conflict, "Set host and sender address first.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await email.SendAsync(new EmailMessage(
                request.To.Trim(),
                "Ardea test e-mail",
                "This is a test e-mail from Ardea. The SMTP settings work.",
                "<p>This is a test e-mail from Ardea. The SMTP settings work.</p>"), timeout.Token);
        }
        catch (Exception ex) when (ex is MailKit.ServiceNotConnectedException or MailKit.Security.AuthenticationException or MailKit.ProtocolException
                                   or MailKit.Net.Smtp.SmtpCommandException or System.Net.Sockets.SocketException or IOException or OperationCanceledException)
        {
            return ApiProblems.Problem("smtp_failed", StatusCodes.Status502BadGateway, "The mail server did not accept the test e-mail: " + ex.Message);
        }

        await using var scope = units.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditLog>().RecordAsync(new AuditEntry("instance.smtp_tested") { InstanceEvent = true }, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IReadOnlyList<AbandonedJobResponse>> ListJobsAsync(IJobAdministration jobs, CancellationToken cancellationToken) =>
        [.. (await jobs.ListAbandonedAsync(100, cancellationToken)).Select(j =>
            new AbandonedJobResponse(j.Id, j.Kind, j.State, j.Attempts, j.MaxAttempts, j.LastError, j.CreatedAt, j.FinishedAt))];

    private static async Task<IResult> RetryJobAsync(Guid jobId, IUnitOfWork unitOfWork, IJobAdministration jobs, IAuditLog audit, CancellationToken cancellationToken)
    {
        if (!await jobs.RetryAsync(jobId, cancellationToken))
        {
            return ApiProblems.Problem("not_found", StatusCodes.Status404NotFound, "No dead or failed job with this id, or a newer run of it is already waiting.");
        }

        await audit.RecordAsync(new AuditEntry("instance.job_retried", "job", jobId.ToString()) { InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<InstanceSettingsResponse> ReadAsync(IInstanceSettings settings, CancellationToken cancellationToken)
    {
        async Task<SettingView> View(SettingDefinition definition)
        {
            var value = await settings.GetAsync(definition, cancellationToken);
            return new SettingView(definition.IsSecret ? null : value.Value, value.Value is not null, value.Locked);
        }

        return new InstanceSettingsResponse(
            await View(InstanceSettingDefinitions.Registration),
            new SmtpSettingsView(
                await View(SmtpSettings.Host),
                await View(SmtpSettings.Port),
                await View(SmtpSettings.User),
                await View(SmtpSettings.Password),
                await View(SmtpSettings.From),
                await View(SmtpSettings.Security)),
            await View(InstanceSettingDefinitions.WebhookInternalHosts));
    }
}

internal sealed record JobCountsResponse(long Pending, long Running, long Dead, long Failed);

internal sealed record InstanceResponse(string Version, JobCountsResponse Jobs);

/// <param name="Value">The effective value; always null for secrets.</param>
/// <param name="IsSet">Whether a value is in effect, from the environment, the database or a default.</param>
/// <param name="Locked">Set by an environment variable: shown read-only.</param>
internal sealed record SettingView(string? Value, bool IsSet, bool Locked);

internal sealed record SmtpSettingsView(SettingView Host, SettingView Port, SettingView User, SettingView Password, SettingView From, SettingView Security);

/// <param name="Registration">invite, open or closed.</param>
/// <param name="Smtp">The mail server.</param>
/// <param name="WebhookInternalHosts">Hosts webhooks may reach in the private network, comma-separated (master plan 9.5).</param>
internal sealed record InstanceSettingsResponse(SettingView Registration, SmtpSettingsView Smtp, SettingView WebhookInternalHosts);

/// <summary>Absent fields stay unchanged; an empty string removes a stored value.</summary>
internal sealed record UpdateInstanceSettingsRequest(
    [property: Description("invite, open or closed")] string? Registration,
    string? SmtpHost,
    string? SmtpPort,
    string? SmtpUser,
    string? SmtpPassword,
    string? SmtpFrom,
    [property: Description("starttls, tls or none")] string? SmtpSecurity,
    [property: Description("Hosts webhooks may reach in the private network, comma-separated")] string? WebhookInternalHosts = null);

internal sealed record TestEmailRequest(string? To);

internal sealed record AbandonedJobResponse(Guid Id, string Kind, string State, int Attempts, int MaxAttempts, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);
