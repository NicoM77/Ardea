using System.Globalization;
using System.Text.Json;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Sources.Formats.CveJson5;

/// <summary>
/// Reads a record of the CVE JSON 5.x format (master plan 5.3) into an observation: the CNA
/// container, plus the ADP containers (CISA-ADP enrichment with CVSS, SSVC and KEV, the CVE
/// Program's references). Only what the record says; nothing is guessed (master plan 6.1).
/// </summary>
public static class CveRecordReader
{
    public const int ParserVersion = 1;

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
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("cveMetadata", out var metadata)
                || Text(metadata, "cveId") is not { } cveId)
            {
                return NormalizationResult.Quarantine("No cveMetadata.cveId: not a CVE JSON 5 record.");
            }

            var dataVersion = Text(root, "dataVersion");
            if (dataVersion is not null && !dataVersion.StartsWith("5.", StringComparison.Ordinal))
            {
                return NormalizationResult.Quarantine($"Unsupported dataVersion {dataVersion}; this reader knows 5.x.");
            }

            try
            {
                return NormalizationResult.Success(ReadRecord(root, metadata, cveId));
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or KeyNotFoundException or UriFormatException)
            {
                return NormalizationResult.Quarantine($"{cveId}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static Observation ReadRecord(JsonElement root, JsonElement metadata, string cveId)
    {
        var containers = root.TryGetProperty("containers", out var c) ? c : default;
        var cna = containers.ValueKind == JsonValueKind.Object && containers.TryGetProperty("cna", out var cnaElement) ? cnaElement : default;
        var adps = containers.ValueKind == JsonValueKind.Object && containers.TryGetProperty("adp", out var adpElement) && adpElement.ValueKind == JsonValueKind.Array
            ? adpElement.EnumerateArray().ToList()
            : [];
        var assigner = Text(metadata, "assignerShortName") ?? Provider(cna);

        var rejected = string.Equals(Text(metadata, "state"), "REJECTED", StringComparison.OrdinalIgnoreCase);
        var disputed = Strings(cna, "tags").Contains("disputed", StringComparer.OrdinalIgnoreCase);
        var description = English(cna, rejected ? "rejectedReasons" : "descriptions");

        var metrics = Metrics(cna, assigner).Concat(adps.SelectMany(a => Metrics(a, Provider(a)))).ToList();
        var affected = Affected(cna).Concat(adps.SelectMany(Affected)).ToList();
        var references = References(cna).Concat(adps.SelectMany(References))
            .GroupBy(r => r.Url.AbsoluteUri, StringComparer.Ordinal)
            .Select(g => g.First() with { Tags = [.. g.SelectMany(r => r.Tags).Distinct(StringComparer.Ordinal)] })
            .ToList();
        var weaknesses = Weaknesses(cna).Concat(adps.SelectMany(Weaknesses)).Distinct(StringComparer.Ordinal).ToList();
        var exploitation = Exploitation(adps.Prepend(cna));
        var solutions = EnglishAll(cna, "solutions");
        var workarounds = EnglishAll(cna, "workarounds");
        var fixKnown = affected.Any(a => a.Versions.Any(v => v.Fixed is not null || v.Status == AffectedStatus.Unaffected))
            || solutions.Count > 0
            || references.Any(r => r.Tags.Contains("patch", StringComparer.OrdinalIgnoreCase));

        var claim = new ClaimBlock
        {
            Aliases = [new Identifier("CVE", cveId)],
            State = rejected ? VulnerabilityState.Rejected : disputed ? VulnerabilityState.Disputed : VulnerabilityState.Published,
            Title = Text(cna, "title"),
            Description = description,
            Metrics = metrics,
            Affected = affected,
            Exploitation = exploitation,
            Remediation = new Remediation { FixAvailable = fixKnown, Solutions = solutions, Workarounds = workarounds },
            References = references,
            Weaknesses = weaknesses,
            PublishedAt = Date(metadata, "datePublished"),
            ModifiedAt = Date(metadata, "dateUpdated") ?? Date(metadata, "dateRejected"),
        };

        return new Observation
        {
            Kind = ObservationKind.Vulnerability,
            Title = claim.Title,
            Description = description,
            Language = "en",
            PublishedAt = claim.PublishedAt,
            ModifiedAt = claim.ModifiedAt,
            Claims = [claim],
        };
    }

    private static IEnumerable<SeverityMetric> Metrics(JsonElement container, string? assigner)
    {
        if (!TryArray(container, "metrics", out var metrics))
        {
            yield break;
        }

        foreach (var metric in metrics)
        {
            foreach (var (property, version) in new[] { ("cvssV4_0", "4.0"), ("cvssV3_1", "3.1"), ("cvssV3_0", "3.0"), ("cvssV2_0", "2.0") })
            {
                if (metric.TryGetProperty(property, out var cvss) && cvss.ValueKind == JsonValueKind.Object)
                {
                    yield return new SeverityMetric("CVSS", version, Text(cvss, "vectorString"), Number(cvss, "baseScore")) { Assigner = assigner };
                }
            }
        }
    }

    private static IEnumerable<AffectedProduct> Affected(JsonElement container)
    {
        if (!TryArray(container, "affected", out var affected))
        {
            yield break;
        }

        foreach (var product in affected)
        {
            var versions = TryArray(product, "versions", out var list) ? list.SelectMany(Versions).ToList() : [];
            yield return new AffectedProduct
            {
                Vendor = Clean(Text(product, "vendor")),
                Product = Clean(Text(product, "product") ?? Text(product, "packageName")),
                Cpes = Strings(product, "cpes"),
                Purl = PackageUrl.From(Text(product, "collectionURL"), Text(product, "packageName")),
                Platforms = Strings(product, "platforms"),
                VersionScheme = versions.Select(v => v.Scheme).FirstOrDefault(s => s is not null),
                Versions = versions,
                DefaultStatus = Status(Text(product, "defaultStatus")),
            };
        }
    }

    /// <summary>
    /// A version entry: <c>version</c> with <c>lessThan</c> (exclusive upper bound) or
    /// <c>lessThanOrEqual</c> (inclusive) is a range from <c>version</c>; without either it is one exact
    /// version. An upper bound with a wildcard (<c>*</c>, or text like <c>log4j-core*</c>) means none.
    /// <c>changes</c> switch the status inside the range at given versions, so one entry becomes
    /// several consecutive ranges, e.g. Log4Shell's fixed 2.3.1, 2.12.2 and 2.15.0 lines.
    /// </summary>
    private static IEnumerable<VersionRange> Versions(JsonElement entry)
    {
        var version = Text(entry, "version");
        var lessThan = Bound(Text(entry, "lessThan"));
        var lessThanOrEqual = Bound(Text(entry, "lessThanOrEqual"));
        var isRange = entry.TryGetProperty("lessThan", out _) || entry.TryGetProperty("lessThanOrEqual", out _);
        var status = Status(Text(entry, "status")) ?? AffectedStatus.Unknown;
        var scheme = Text(entry, "versionType");
        var raw = JsonSerializer.Serialize(entry);

        var changes = TryArray(entry, "changes", out var list)
            ? list.Select(c => (At: Text(c, "at"), Status: Status(Text(c, "status")))).Where(c => c.At is not null && c.Status is not null).ToList()
            : [];
        if (!isRange || changes.Count == 0)
        {
            yield return new VersionRange
            {
                Status = status,
                Exact = isRange ? null : version,
                Introduced = isRange ? version : null,
                Fixed = lessThan,
                LastAffected = lessThanOrEqual,
                Scheme = scheme,
                Raw = raw,
            };
            yield break;
        }

        var start = version;
        var current = status;
        foreach (var (at, next) in changes)
        {
            yield return new VersionRange { Status = current, Introduced = start, Fixed = at, Scheme = scheme, Raw = raw };
            start = at;
            current = next!.Value;
        }

        yield return new VersionRange { Status = current, Introduced = start, Fixed = lessThan, LastAffected = lessThanOrEqual, Scheme = scheme, Raw = raw };
    }

    private static string? Bound(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains('*', StringComparison.Ordinal) ? null : value.Trim();

    /// <summary>SSVC "Exploitation" from any container, and KEV from CISA-ADP; the strongest wins.</summary>
    private static Exploitation? Exploitation(IEnumerable<JsonElement> containers)
    {
        ExploitationState? state = null;
        string? evidence = null;
        DateTimeOffset? since = null;
        var ssvc = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var container in containers)
        {
            if (!TryArray(container, "metrics", out var metrics))
            {
                continue;
            }

            foreach (var metric in metrics)
            {
                if (!metric.TryGetProperty("other", out var other) || !other.TryGetProperty("content", out var content))
                {
                    continue;
                }

                switch (Text(other, "type"))
                {
                    case "ssvc" when TryArray(content, "options", out var options):
                        foreach (var option in options.Where(o => o.ValueKind == JsonValueKind.Object))
                        {
                            foreach (var point in option.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                            {
                                ssvc[point.Name] = point.Value.GetString()!;
                            }
                        }

                        state = Max(state, ssvc.GetValueOrDefault("Exploitation")?.ToLowerInvariant() switch
                        {
                            "active" => ExploitationState.Active,
                            "poc" or "public poc" => ExploitationState.ProofOfConcept,
                            "none" => ExploitationState.None,
                            _ => null,
                        });
                        break;
                    case "kev":
                        state = ExploitationState.Active;
                        evidence = Text(content, "reference") ?? "CISA KEV";
                        since = Date(content, "dateAdded");
                        break;
                }
            }
        }

        return state is null ? null : new Exploitation(state.Value) { Evidence = evidence, Since = since, Ssvc = ssvc };
    }

    private static IEnumerable<Reference> References(JsonElement container)
    {
        if (!TryArray(container, "references", out var references))
        {
            yield break;
        }

        foreach (var reference in references)
        {
            if (Text(reference, "url") is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                yield return new Reference(uri) { Tags = Strings(reference, "tags") };
            }
        }
    }

    private static IEnumerable<string> Weaknesses(JsonElement container)
    {
        if (!TryArray(container, "problemTypes", out var types))
        {
            yield break;
        }

        foreach (var type in types)
        {
            if (!TryArray(type, "descriptions", out var descriptions))
            {
                continue;
            }

            foreach (var description in descriptions)
            {
                if (Text(description, "cweId") is { } cwe && cwe.StartsWith("CWE-", StringComparison.OrdinalIgnoreCase))
                {
                    yield return cwe.ToUpperInvariant();
                }
            }
        }
    }

    private static ExploitationState? Max(ExploitationState? a, ExploitationState? b) =>
        a is null ? b : b is null ? a : (ExploitationState)Math.Max((int)a.Value, (int)b.Value);

    private static AffectedStatus? Status(string? value) => value?.ToLowerInvariant() switch
    {
        "affected" => AffectedStatus.Affected,
        "unaffected" => AffectedStatus.Unaffected,
        "unknown" => AffectedStatus.Unknown,
        _ => null,
    };

    /// <summary><c>n/a</c> and empty names carry nothing (master plan 6.5).</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() is "n/a" or "N/A" or "-" ? null : value.Trim();

    private static string? Provider(JsonElement container) =>
        container.ValueKind == JsonValueKind.Object && container.TryGetProperty("providerMetadata", out var provider) ? Text(provider, "shortName") : null;

    private static string? English(JsonElement container, string property) => EnglishAll(container, property).FirstOrDefault();

    private static List<string> EnglishAll(JsonElement container, string property)
    {
        if (!TryArray(container, property, out var items))
        {
            return [];
        }

        var texts = items.Select(i => (Lang: Text(i, "lang"), Value: Text(i, "value"))).Where(t => !string.IsNullOrWhiteSpace(t.Value)).ToList();
        var english = texts.Where(t => t.Lang is not null && t.Lang.StartsWith("en", StringComparison.OrdinalIgnoreCase)).Select(t => t.Value!.Trim()).ToList();
        return english.Count > 0 ? english : [.. texts.Select(t => t.Value!.Trim())];
    }

    private static bool TryArray(JsonElement element, string property, out IEnumerable<JsonElement> items)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            items = array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object);
            return true;
        }

        items = [];
        return false;
    }

    private static List<string> Strings(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Where(s => s.Length > 0)]
            : [];

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? Number(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static DateTimeOffset? Date(JsonElement element, string property) =>
        Text(element, property) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date
            : null;
}
