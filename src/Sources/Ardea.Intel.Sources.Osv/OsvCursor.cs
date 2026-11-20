using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ardea.Intel.Sources.Osv;

internal sealed record EcosystemCursor(DateTimeOffset Through, string? Validators);

internal sealed record OsvCursor(DateTimeOffset? From, IReadOnlyDictionary<string, EcosystemCursor> Ecosystems)
{
    public OsvCursor With(string ecosystem, EcosystemCursor position)
    {
        var ecosystems = new Dictionary<string, EcosystemCursor>(Ecosystems, StringComparer.Ordinal) { [ecosystem] = position };
        return this with { Ecosystems = ecosystems };
    }

    public override string ToString() => JsonSerializer.Serialize(new Stored(
        From?.ToString("O", CultureInfo.InvariantCulture),
        Ecosystems.OrderBy(e => e.Key, StringComparer.Ordinal).ToDictionary(
            e => e.Key,
            e => new StoredEcosystem(e.Value.Through.ToString("O", CultureInfo.InvariantCulture), e.Value.Validators),
            StringComparer.Ordinal)));

    public static OsvCursor? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var stored = JsonSerializer.Deserialize<Stored>(value) ?? throw new FormatException("Empty OSV cursor.");
        return new OsvCursor(
            stored.From is null ? null : Date(stored.From),
            (stored.Ecosystems ?? []).ToDictionary(e => e.Key, e => new EcosystemCursor(Date(e.Value.Through), e.Value.Validators), StringComparer.Ordinal));
    }

    private static DateTimeOffset Date(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record Stored(
        [property: JsonPropertyName("from")] string? From,
        [property: JsonPropertyName("ecosystems")] Dictionary<string, StoredEcosystem>? Ecosystems);

    private sealed record StoredEcosystem(
        [property: JsonPropertyName("through")] string Through,
        [property: JsonPropertyName("validators")] string? Validators);
}
