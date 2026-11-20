using Ardea.Intel.Persistence;
using Ardea.Intel.Sync;
using Ardea.Platform.Audit;
using Ardea.Platform.Http;
using Ardea.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Intel.Features;

internal sealed record SourceStatus(
    string Key,
    string Name,
    string Kind,
    string Operator,
    string OperatorKind,
    bool Enabled,
    string Health,
    string? HealthReason,
    int IntervalMinutes,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset NextSyncAt,
    int ConsecutiveFailures,
    long Records,
    long Quarantined,
    string Attribution,
    string TermsMode);

internal sealed record UpdateSourceRequest(bool? Enabled, int? IntervalMinutes);

internal sealed record SourceRunView(DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Mode, string Outcome, int Fetched, int Changed, int Checkpoints, string? Error);

internal sealed record QuarantinedRecord(string ExternalId, string? Url, string? Reason, DateTimeOffset LastSeenAt, int Revision);

internal static class SourceEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/sources", ListAsync).WithTags("Sources").WithName("listSources").RequireAuthorization();

        var admin = api.MapGroup("/instance/sources").WithTags("Sources").RequireAuthorization(Policies.InstanceAdmin);
        admin.MapPatch("/{key}", UpdateAsync).WithName("updateSource").Produces<SourceStatus>().Problems(400, 404);
        admin.MapPost("/{key}/sync", SyncNowAsync).WithName("syncSource").NoContent().Problems(404, 409);
        admin.MapGet("/{key}/runs", RunsAsync).WithName("listSourceRuns").Produces<IReadOnlyList<SourceRunView>>().Problems(404);
        admin.MapGet("/{key}/quarantine", QuarantineAsync).WithName("listQuarantine").Produces<IReadOnlyList<QuarantinedRecord>>().Problems(404);
    }

    private static async Task<IReadOnlyList<SourceStatus>> ListAsync(IntelDbContext db, CancellationToken cancellationToken)
    {
        var counts = await db.SourceRecords.AsNoTracking()
            .GroupBy(r => r.SourceId)
            .Select(g => new { g.Key, Records = g.LongCount(), Quarantined = g.LongCount(r => r.ParseStatus == "quarantined") })
            .ToDictionaryAsync(c => c.Key, cancellationToken);
        var sources = await db.Sources.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken);
        return [.. sources.Select(s => Status(s, counts.TryGetValue(s.Id, out var c) ? (c.Records, c.Quarantined) : (0, 0)))];
    }

    private static async Task<IResult> UpdateAsync(
        string key, UpdateSourceRequest request, IntelDbContext db, SourceRegistry registry, IUnitOfWork unitOfWork, IAuditLog audit, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var source = await db.Sources.SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (source is null)
        {
            return NotFound(key);
        }

        var minimum = registry.Descriptors.FirstOrDefault(d => d.Key == key)?.MinimumInterval ?? TimeSpan.FromMinutes(15);
        if (request.IntervalMinutes is { } minutes)
        {
            if (TimeSpan.FromMinutes(minutes) < minimum || minutes > 7 * 24 * 60)
            {
                return ApiProblems.Validation("validation_failed", "The interval is out of range.", new Dictionary<string, string[]>
                {
                    ["intervalMinutes"] = [$"Choose between {(int)minimum.TotalMinutes} minutes and 7 days."],
                });
            }

            source.Interval = TimeSpan.FromMinutes(minutes);
        }

        if (request.Enabled is { } enabled && enabled != source.Enabled)
        {
            source.Enabled = enabled;
            source.Health = enabled ? SourceHealth.Healthy : SourceHealth.Disabled;
            source.HealthReason = null;
            if (enabled)
            {
                source.NextSyncAt = time.GetUtcNow();
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("instance.source_changed", "source", key, new { request.Enabled, request.IntervalMinutes }) { InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(Status(source, (0, 0)));
    }

    private static async Task<IResult> SyncNowAsync(string key, IntelDbContext db, IUnitOfWork unitOfWork, IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        var source = await db.Sources.SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (source is null)
        {
            return NotFound(key);
        }

        if (!source.Enabled)
        {
            return ApiProblems.Problem("source_disabled", StatusCodes.Status409Conflict, "Turn the source on first.");
        }

        source.NextSyncAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("instance.source_sync_requested", "source", key) { InstanceEvent = true }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RunsAsync(string key, int? limit, IntelDbContext db, CancellationToken cancellationToken)
    {
        var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (source is null)
        {
            return NotFound(key);
        }

        var runs = await db.SourceRuns.AsNoTracking().Where(r => r.SourceId == source.Id)
            .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).Take(Math.Clamp(limit ?? 50, 1, 200)).ToListAsync(cancellationToken);
        return Results.Ok(runs.Select(r => new SourceRunView(r.StartedAt, r.FinishedAt, r.Mode, r.Outcome, r.Fetched, r.Changed, r.Checkpoints, r.Error)).ToList());
    }

    private static async Task<IResult> QuarantineAsync(string key, int? limit, IntelDbContext db, CancellationToken cancellationToken)
    {
        var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (source is null)
        {
            return NotFound(key);
        }

        var records = await db.SourceRecords.AsNoTracking().Where(r => r.SourceId == source.Id && r.ParseStatus == "quarantined")
            .OrderByDescending(r => r.LastSeenAt).Take(Math.Clamp(limit ?? 100, 1, 500)).ToListAsync(cancellationToken);
        return Results.Ok(records.Select(r => new QuarantinedRecord(r.ExternalId, r.Url, r.QuarantineReason, r.LastSeenAt, r.Revision)).ToList());
    }

    private static SourceStatus Status(SourceRow s, (long Records, long Quarantined) counts) => new(
        s.Key, s.Name, s.Kind, s.Operator, s.OperatorKind, s.Enabled, s.Health, s.HealthReason, (int)s.Interval.TotalMinutes,
        s.LastSuccessAt, s.LastAttemptAt, s.NextSyncAt, s.ConsecutiveFailures, counts.Records, counts.Quarantined, s.Attribution, s.TermsMode);

    private static IResult NotFound(string key) => ApiProblems.Problem("not_found", StatusCodes.Status404NotFound, $"No source {key}.");
}
