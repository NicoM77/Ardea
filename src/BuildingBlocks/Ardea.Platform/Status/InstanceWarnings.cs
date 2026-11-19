using Ardea.Platform.Persistence;
using Ardea.Platform.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Platform.Status;

/// <summary>One warning as a mail to the instance administrators names it: only what the status shows.</summary>
public sealed record WarningNotice(string Code, string? Subject, string Severity, string Message, DateTimeOffset FirstSeenAt);

/// <summary>
/// What one round of the self-check tells the instance administrators (ADR 043): warnings that are new, warnings
/// still active a day after the last mail about them, and warnings that cleared and stayed clear for a round.
/// </summary>
public sealed record WarningMail(IReadOnlyList<WarningNotice> Raised, IReadOnlyList<WarningNotice> Reminded, IReadOnlyList<WarningNotice> Resolved)
{
    public static readonly WarningMail Empty = new([], [], []);

    public bool IsEmpty => Raised.Count == 0 && Reminded.Count == 0 && Resolved.Count == 0;

    /// <summary>The reminder times before this round's claim, given back when the mail could not be sent.</summary>
    internal IReadOnlyDictionary<(string Code, string Subject), DateTimeOffset?> PreviousReminders { get; init; } =
        new Dictionary<(string Code, string Subject), DateTimeOffset?>();
}

/// <summary>When the administrators were last mailed about an active warning; shown in the administration.</summary>
public sealed record WarningNotified(string Code, string? Subject, DateTimeOffset At);

/// <summary>
/// The rules of the self-check's state per warning (ADR 043), without the database. A new warning is claimed for a
/// mail at once; an active one again 24 hours after the last mail; a cleared one counts as resolved only when a
/// later round, at least four minutes on (in practice the next one), still does not see it, so a warning that
/// comes and goes between two rounds sends neither a second «new» nor a «resolved» mail. Without anyone to tell
/// (<c>claim</c> false) the state is kept, nothing is claimed, and a later round with a mail server tells them.
/// </summary>
internal static class InstanceWarningRules
{
    public static readonly TimeSpan RemindEvery = TimeSpan.FromHours(24);

    /// <summary>Just under the five minutes between two rounds, so that the next round counts.</summary>
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
                // Resolved and mailed in an earlier round, now back: a new warning.
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
                // The «resolved» mail went out in an earlier round.
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
                // Nobody was told about it, so nobody needs to hear that it is over.
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

    /// <summary>At most the column's length, never cutting a surrogate pair in two.</summary>
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

/// <summary>The self-check's state in <c>platform.instance_warning</c>, inside the caller's unit of work.</summary>
internal sealed class InstanceWarningLedger(PlatformDbContext db)
{
    /// <summary>Brings the state in line with the warnings of this round and claims what to mail (<paramref name="claim"/>).</summary>
    public async Task<WarningMail> ReconcileAsync(IReadOnlyList<StatusWarning> current, DateTimeOffset now, bool claim, CancellationToken cancellationToken)
    {
        var rows = await db.InstanceWarnings.ToListAsync(cancellationToken);
        var outcome = InstanceWarningRules.Apply(rows, current, now, claim);
        db.InstanceWarnings.AddRange(outcome.Added);
        db.InstanceWarnings.RemoveRange(outcome.Removed);
        await db.SaveChangesAsync(cancellationToken);
        return outcome.Mail;
    }

    /// <summary>Gives back a claim whose mail could not be sent, so that the next round tries again.</summary>
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

    /// <summary>The active warnings the administrators were mailed about, with the time of the last mail.</summary>
    public async Task<IReadOnlyList<WarningNotified>> NotifiedAsync(CancellationToken cancellationToken)
    {
        var rows = await db.InstanceWarnings.AsNoTracking()
            .Where(w => w.NotifiedAt != null && w.ResolvedAt == null && w.ClearedAt == null)
            .OrderBy(w => w.Code).ThenBy(w => w.Subject)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new WarningNotified(r.Code, r.Subject.Length == 0 ? null : r.Subject, r.RemindedAt ?? r.NotifiedAt!.Value))];
    }
}
