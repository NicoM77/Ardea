using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ardea.Intel.Sources.CveProgram;

internal sealed record CveCursor(DateTimeOffset Through, string? Validators)
{
    public override string ToString() => JsonSerializer.Serialize(new Stored(Through.ToString("O", CultureInfo.InvariantCulture), Validators));

    public static CveCursor? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var stored = JsonSerializer.Deserialize<Stored>(value) ?? throw new FormatException("Empty CVE cursor.");
        return new CveCursor(DateTimeOffset.Parse(stored.Through, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), stored.Validators);
    }

    private sealed record Stored([property: JsonPropertyName("through")] string Through, [property: JsonPropertyName("validators")] string? Validators);
}

internal sealed record DeltaChange(
    [property: JsonPropertyName("cveId")] string CveId,
    [property: JsonPropertyName("githubLink")] string? GithubLink,
    [property: JsonPropertyName("dateUpdated")] DateTimeOffset? DateUpdated);

internal sealed record DeltaEntry(
    [property: JsonPropertyName("fetchTime")] DateTimeOffset FetchTime,
    [property: JsonPropertyName("new")] List<DeltaChange>? New,
    [property: JsonPropertyName("updated")] List<DeltaChange>? Updated);

internal sealed record DeltaStep(DateTimeOffset FetchTime, IReadOnlyList<DeltaChange> Changes, bool IsNewest);

internal sealed record DeltaPlan(IReadOnlyList<DeltaStep> Steps, bool GapTooLong)
{
    public static DeltaPlan Create(IReadOnlyList<DeltaEntry> log, DateTimeOffset through)
    {
        if (log.Count == 0)
        {
            return new DeltaPlan([], GapTooLong: false);
        }

        var ordered = log.OrderBy(e => e.FetchTime).ToList();
        if (through < ordered[0].FetchTime)
        {
            return new DeltaPlan([], GapTooLong: true);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = ordered.Where(e => e.FetchTime > through).ToList();
        var steps = new List<DeltaStep>(pending.Count);
        for (var i = 0; i < pending.Count; i++)
        {
            var entry = pending[i];
            var changes = (entry.New ?? []).Concat(entry.Updated ?? [])
                .Where(c => CveId.IsValid(c.CveId) && seen.Add(c.CveId))
                .ToList();
            steps.Add(new DeltaStep(entry.FetchTime, changes, IsNewest: i == pending.Count - 1));
        }

        return new DeltaPlan(steps, GapTooLong: false);
    }
}
