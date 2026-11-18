using System.Globalization;
using System.Text.Json;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Sources.Formats.Osv;

/// <summary>The fields of an OSV record the fetch needs before it reads the whole record.</summary>
public sealed record OsvMetadata(string Id, DateTimeOffset? Published, DateTimeOffset Modified, bool Withdrawn);

/// <summary>
/// Reads a record of the OSV schema 1.x (master plan 5.3) into an observation with one claim block:
/// the record's own id and its <c>aliases</c> as aliases, <c>related</c> and <c>upstream</c> as related
/// identifiers, CVSS vectors, affected packages with package URL and version ranges, references and
/// CWEs. Only what the record says; nothing is guessed (master plan 6.1).
/// </summary>
public static class OsvRecordReader
{
    public const int ParserVersion = 1;

    /// <summary>OSV reference types as tags in the vocabulary of the CVE reader where one fits.</summary>
    private static readonly Dictionary<string, string> ReferenceTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ADVISORY"] = "advisory",
        ["ARTICLE"] = "article",
        ["DETECTION"] = "detection",
        ["DISCUSSION"] = "discussion",
        ["REPORT"] = "issue-tracking",
        ["FIX"] = "patch",
        ["INTRODUCED"] = "introduced",
        ["GIT"] = "git",
        ["PACKAGE"] = "package",
        ["EVIDENCE"] = "exploit",
    };

    public static NormalizationResult Read(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return NormalizationResult.Quarantine($"Not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "id") is not { Length: > 0 } id)
            {
                return NormalizationResult.Quarantine("No id: not an OSV record.");
            }

            var schema = Text(root, "schema_version");
            if (schema is not null && !schema.StartsWith("1.", StringComparison.Ordinal))
            {
                return NormalizationResult.Quarantine($"{id}: unsupported schema_version {schema}; this reader knows 1.x.");
            }

            if (Date(root, "modified") is null)
            {
                return NormalizationResult.Quarantine($"{id}: no valid modified date, which the OSV schema requires.");
            }

            try
            {
                return NormalizationResult.Success(ReadRecord(root, id));
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or UriFormatException)
            {
                return NormalizationResult.Quarantine($"{id}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Id and dates of a record, or <c>null</c> when it has no id or no valid <c>modified</c>.</summary>
    public static OsvMetadata? Metadata(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && Text(root, "id") is { Length: > 0 } id && Date(root, "modified") is { } modified
                ? new OsvMetadata(id, Date(root, "published"), modified, Date(root, "withdrawn") is not null)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The identifier of an OSV id or alias: CVE and GHSA under their own schemes; every other id of
    /// the OSV namespace (<c>PYSEC-</c>, <c>GO-</c>, <c>DRUPAL-</c>, <c>OSV-</c>, <c>BIT-</c>, …) as <c>OSV</c>,
    /// which ranks after GHSA when the core picks the primary key (master plan 6.2).
    /// </summary>
    public static Identifier Identify(string id) =>
        id.StartsWith("CVE-", StringComparison.OrdinalIgnoreCase) ? new Identifier("CVE", id.ToUpperInvariant())
        : id.StartsWith("GHSA-", StringComparison.OrdinalIgnoreCase) ? new Identifier("GHSA", "GHSA-" + id[5..].ToLowerInvariant())
        : new Identifier("OSV", id);

    private static Observation ReadRecord(JsonElement root, string id)
    {
        var aliases = new[] { id }.Concat(Strings(root, "aliases"))
            .Select(Identify)
            .Distinct()
            .ToList();
        var related = Strings(root, "related").Concat(Strings(root, "upstream"))
            .Select(Identify)
            .Where(r => !aliases.Contains(r))
            .Distinct()
            .ToList();
        var origin = Origin(id);
        var affected = Affected(root).ToList();
        var metrics = Metrics(root, origin)
            .Concat(Objects(root, "affected").SelectMany(a => Metrics(a, origin)))
            .Distinct()
            .ToList();
        var references = References(root)
            .GroupBy(r => r.Url.AbsoluteUri, StringComparer.Ordinal)
            .Select(g => g.First() with { Tags = [.. g.SelectMany(r => r.Tags).Distinct(StringComparer.Ordinal)] })
            .ToList();
        var databaseSpecific = root.TryGetProperty("database_specific", out var specific) ? specific : default;
        var weaknesses = Strings(databaseSpecific, "cwe_ids")
            .Where(c => c.StartsWith("CWE-", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var fixAvailable = affected.Any(a => a.Versions.Any(v => v.Fixed is not null))
            || references.Any(r => r.Tags.Contains("patch", StringComparer.Ordinal));

        var claim = new ClaimBlock
        {
            Aliases = aliases,
            Related = related,
            State = Date(root, "withdrawn") is not null ? VulnerabilityState.Rejected : VulnerabilityState.Published,
            Title = Clean(Text(root, "summary")),
            Description = Clean(Text(root, "details")),
            Metrics = metrics,
            Affected = affected,
            Remediation = new Remediation { FixAvailable = fixAvailable },
            References = references,
            Weaknesses = weaknesses,
            PublishedAt = Date(root, "published"),
            ModifiedAt = Date(root, "modified"),
        };

        return new Observation
        {
            Kind = ObservationKind.Vulnerability,
            Title = claim.Title,
            Description = claim.Description,
            Language = "en",
            PublishedAt = claim.PublishedAt,
            ModifiedAt = claim.ModifiedAt,
            Claims = [claim],
        };
    }

    /// <summary>
    /// One product per <c>affected</c> entry with a package. Ranges of type <c>ECOSYSTEM</c> and
    /// <c>SEMVER</c> are compared with the ecosystem's scheme (Go writes SEMVER without the <c>v</c> its
    /// scheme also reads); <c>GIT</c> ranges keep their commits under the scheme <c>git</c>, which is
    /// stored but never evaluated. The explicit <c>versions</c> list repeats what the ranges say and can
    /// run to hundreds of entries, so it is kept only when the entry has no comparable range.
    /// </summary>
    private static IEnumerable<AffectedProduct> Affected(JsonElement root)
    {
        foreach (var entry in Objects(root, "affected"))
        {
            if (!entry.TryGetProperty("package", out var package) || package.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var ecosystem = Text(package, "ecosystem");
            var name = Clean(Text(package, "name"));
            var scheme = OsvEcosystems.Scheme(ecosystem);
            var ranges = Objects(entry, "ranges").ToList();
            var versions = ranges.SelectMany(r => Ranges(r, ecosystem, scheme)).ToList();
            var comparable = ranges.Any(r => Text(r, "type") is "ECOSYSTEM" or "SEMVER");
            if (!comparable)
            {
                versions.AddRange(Strings(entry, "versions").Select(v => new VersionRange { Exact = v, Scheme = scheme }));
            }

            yield return new AffectedProduct
            {
                Product = name,
                Purl = Clean(Text(package, "purl")) ?? OsvEcosystems.PackageUrl(ecosystem, name),
                VersionScheme = scheme,
                Versions = versions,
            };
        }
    }

    /// <summary>
    /// The events of one range in record order: each <c>introduced</c> opens a range, the next
    /// <c>fixed</c> (exclusive) or <c>last_affected</c> (inclusive) closes it; <c>introduced: "0"</c> has
    /// no lower bound. <c>limit</c> only bounds a search in a git history and says nothing about
    /// affectedness. A range left open is affected upwards without end.
    /// </summary>
    private static IEnumerable<VersionRange> Ranges(JsonElement range, string? ecosystem, string? ecosystemScheme)
    {
        var type = Text(range, "type");
        var scheme = type switch
        {
            "GIT" => "git",
            "ECOSYSTEM" => ecosystemScheme,
            "SEMVER" => OsvEcosystems.Mvp.Contains(ecosystem ?? string.Empty, StringComparer.OrdinalIgnoreCase) ? ecosystemScheme : "semver",
            _ => type?.ToLowerInvariant(),
        };
        var raw = range.GetRawText();

        VersionRange? open = null;
        foreach (var @event in Objects(range, "events"))
        {
            foreach (var property in @event.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
            {
                var value = property.Value.GetString()!.Trim();
                switch (property.Name)
                {
                    case "introduced":
                        if (open is not null)
                        {
                            yield return open;
                        }

                        open = new VersionRange { Introduced = value is "0" or "" ? null : value, Scheme = scheme, Raw = raw };
                        break;
                    case "fixed":
                        yield return (open ?? new VersionRange { Scheme = scheme, Raw = raw }) with { Fixed = value };
                        open = null;
                        break;
                    case "last_affected":
                        yield return (open ?? new VersionRange { Scheme = scheme, Raw = raw }) with { LastAffected = value };
                        open = null;
                        break;
                }
            }
        }

        if (open is not null)
        {
            yield return open;
        }
    }

    /// <summary>CVSS vectors; the qualitative Ubuntu severity has no place in a CVSS metric and is left out.</summary>
    private static IEnumerable<SeverityMetric> Metrics(JsonElement element, string? assigner)
    {
        foreach (var severity in Objects(element, "severity"))
        {
            var vector = Text(severity, "score")?.Trim();
            if (string.IsNullOrEmpty(vector))
            {
                continue;
            }

            var version = Text(severity, "type") switch
            {
                "CVSS_V2" => "2.0",
                "CVSS_V3" => vector.StartsWith("CVSS:3.0/", StringComparison.Ordinal) ? "3.0" : "3.1",
                "CVSS_V4" => "4.0",
                _ => null,
            };
            if (version is not null)
            {
                // OSV carries the vector only; the score is left to whoever computes it from the vector.
                yield return new SeverityMetric("CVSS", version, vector, null) { Assigner = assigner };
            }
        }
    }

    private static IEnumerable<Reference> References(JsonElement root)
    {
        foreach (var reference in Objects(root, "references"))
        {
            if (Text(reference, "url") is { } url && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                var tag = Text(reference, "type") is { } type && ReferenceTags.TryGetValue(type, out var known) ? known : null;
                yield return new Reference(uri) { Tags = tag is null ? [] : [tag] };
            }
        }
    }

    /// <summary>The database a record comes from, by its id; it assigned the record's CVSS vectors.</summary>
    private static string? Origin(string id) => id[..Math.Max(0, id.IndexOf('-', StringComparison.Ordinal))].ToUpperInvariant() switch
    {
        "GHSA" => "GitHub",
        "PYSEC" => "PyPA",
        "GO" => "Go",
        "DRUPAL" => "Drupal",
        "OSV" => "OSS-Fuzz",
        _ => null,
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IEnumerable<JsonElement> Objects(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object)
            : [];

    private static List<string> Strings(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim()).Where(s => s.Length > 0)]
            : [];

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? Date(JsonElement element, string property) =>
        Text(element, property) is { } text && OsvTimestamp.TryParse(text, out var date) ? date : null;
}

/// <summary>
/// RFC 3339 timestamps as OSV writes them, with up to nine fractional digits
/// (<c>2026-11-18T19:45:04.566522493Z</c>); <see cref="DateTimeOffset"/> keeps seven, the rest is cut.
/// </summary>
public static class OsvTimestamp
{
    public static bool TryParse(string? text, out DateTimeOffset value)
    {
        value = default;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        var dot = trimmed.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
        {
            var end = dot + 1;
            while (end < trimmed.Length && char.IsAsciiDigit(trimmed[end]))
            {
                end++;
            }

            if (end - dot - 1 > 7)
            {
                trimmed = string.Concat(trimmed.AsSpan(0, dot + 8), trimmed.AsSpan(end));
            }
        }

        if (!DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value))
        {
            return false;
        }

        value = value.ToUniversalTime();
        return true;
    }
}
