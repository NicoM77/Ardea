using Ardea.Platform.Persistence;
using Ardea.Platform.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Platform.Status;

public sealed record WarningNotice(string Code, string? Subject, string Severity, string Message, DateTimeOffset FirstSeenAt);

public sealed record WarningMail(IReadOnlyList<WarningNotice> Raised, IReadOnlyList<WarningNotice> Reminded, IReadOnlyList<WarningNotice> Resolved)
{
    public static readonly WarningMail Empty = new([], [], []);

    public bool IsEmpty => Raised.Count == 0 && Reminded.Count == 0 && Resolved.Count == 0;

    internal IReadOnlyDictionary<(string Code, string Subject), DateTimeOffset?> PreviousReminders { get; init; } =
        new Dictionary<(string Code, string Subject), DateTimeOffset?>();
}

/// <summary>When the administrators were last mailed about an active warning; shown in the administration.</summary>
public sealed record WarningNotified(string Code, string? Subject, DateTimeOffset At);

internal static class InstanceWarningRules
{
    public static readonly TimeSpan RemindEvery = TimeSpan.FromHours(24);

    public static readonly TimeSpan StayClearFor = TimeSpan.FromMinutes(4);

    private const int MaxMessageLength = 1000;

    public sealed record Outcome(IReadOnlyList<InstanceWarningRow> Added, IReadOnlyList<InstanceWarningRow> Removed, WarningMail Mail);

    public static Outcome Apply(IReadOnlyList<InstanceWarningRow> rows, IReadOnlyList<StatusWarning> current, DateTimeOffset now, bool claim)
    {
        var byKey = rows.ToDictionary(r => (r.Code, r.Subject));
        var seen = new HashSet<(string, string)>();
        List<InstanceWarningRow> added = [];
        List<InstanceWarningRow> removed = [];
        List<WarningNotice> raised = [];
        List<WarningNotice> reminded = [];
        List<WarningNotice> resolved = [];
        var previous = new Dictionary<(string Code, string Subject), DateTimeOffset?>();

        foreach (var warning in current)
        {
            var key = (warning.Code, warning.Subject ?? string.Empty);
            if (!seen.Add(key))
            {
                continue;
            }

            if (!byKey.TryGetValue(key, out var row))
            {
                row = new InstanceWarningRow
                {
                    Code = key.Code,
                    Subject = key.Item2,
                    Severity = warning.Severity,
                    Message = warning.Message,
                    FirstSeenAt = now,
                };
                added.Add(row);
            }
            else if (row.ResolvedAt is not null)
            {
                row.FirstSeenAt = now;
                row.NotifiedAt = null;
                row.RemindedAt = null;
                row.ResolvedAt = null;
            }

            row.Severity = warning.Severity;
            row.Message = Truncate(warning.Message);
            row.LastSeenAt = now;
            row.ClearedAt = null;
            if (!claim)
            {
                continue;
            }

            if (row.NotifiedAt is null)
            {
                row.NotifiedAt = now;
                raised.Add(Notice(row));
            }
            else if (now - (row.RemindedAt ?? row.NotifiedAt.Value) >= RemindEvery)
            {
                previous[key] = row.RemindedAt;
                row.RemindedAt = now;
                reminded.Add(Notice(row));
            }
        }

        foreach (var row in rows.Where(r => !seen.Contains((r.Code, r.Subject))))
        {
            if (row.ResolvedAt is not null)
            {
                removed.Add(row);
            }
            else if (row.ClearedAt is null)
            {
                row.ClearedAt = now;
            }
            else if (now - row.ClearedAt.Value < StayClearFor)
            {
                continue;
            }
            else if (row.NotifiedAt is null)
            {
                removed.Add(row);
            }
            else if (claim)
            {
                row.ResolvedAt = now;
                resolved.Add(Notice(row));
            }
        }

        var mail = raised.Count + reminded.Count + resolved.Count == 0
            ? WarningMail.Empty
            : new WarningMail(raised, reminded, resolved) { PreviousReminders = previous };
        return new Outcome(added, removed, mail);
    }

    internal static string Truncate(string message)
    {
        if (message.Length <= MaxMessageLength)
        {
            return message;
        }

        var length = char.IsHighSurrogate(message[MaxMessageLength - 1]) ? MaxMessageLength - 1 : MaxMessageLength;
        return message[..length];
    }

    private static WarningNotice Notice(InstanceWarningRow row) =>
        new(row.Code, row.Subject.Length == 0 ? null : row.Subject, row.Severity, row.Message, row.FirstSeenAt);
}

internal sealed class InstanceWarningLedger(PlatformDbContext db)
{
    public async Task<WarningMail> ReconcileAsync(IReadOnlyList<StatusWarning> current, DateTimeOffset now, bool claim, CancellationToken cancellationToken)
    {
        var rows = await db.InstanceWarnings.ToListAsync(cancellationToken);
        var outcome = InstanceWarningRules.Apply(rows, current, now, claim);
        db.InstanceWarnings.AddRange(outcome.Added);
        db.InstanceWarnings.RemoveRange(outcome.Removed);
        await db.SaveChangesAsync(cancellationToken);
        return outcome.Mail;
    }

    public async Task ReleaseAsync(WarningMail mail, CancellationToken cancellationToken)
    {
        var rows = (await db.InstanceWarnings.ToListAsync(cancellationToken)).ToDictionary(r => (r.Code, r.Subject));
        InstanceWarningRow? Row(WarningNotice notice) => rows.GetValueOrDefault((notice.Code, notice.Subject ?? string.Empty));
        foreach (var notice in mail.Raised)
        {
            Row(notice)?.NotifiedAt = null;
        }

        foreach (var notice in mail.Reminded)
        {
            Row(notice)?.RemindedAt = mail.PreviousReminders.GetValueOrDefault((notice.Code, notice.Subject ?? string.Empty));
        }

        foreach (var notice in mail.Resolved)
        {
            Row(notice)?.ResolvedAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WarningNotified>> NotifiedAsync(CancellationToken cancellationToken)
    {
        var rows = await db.InstanceWarnings.AsNoTracking()
            .Where(w => w.NotifiedAt != null && w.ResolvedAt == null && w.ClearedAt == null)
            .OrderBy(w => w.Code).ThenBy(w => w.Subject)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new WarningNotified(r.Code, r.Subject.Length == 0 ? null : r.Subject, r.RemindedAt ?? r.NotifiedAt!.Value))];
    }
}
