using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Sources.Signals;

/// <summary>
/// CISA's catalogue of known exploited vulnerabilities (master plan 4.4, 5.5, decision E3): one file
/// of about 1,800 entries. CISA ignores the ETag but honours <c>If-Modified-Since</c> (measured
/// 18 Nov 2026), so an unchanged catalogue costs one request with an empty answer.
/// </summary>
public sealed class CisaKevProvider : ISignalProvider
{
    public const string Key = "cisa-kev";
    internal const string Url = "https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json";

    public SourceDescriptor Descriptor { get; } = new()
    {
        Key = Key,
        Name = "CISA Known Exploited Vulnerabilities",
        Kind = SourceKind.Signal,
        Operator = "Cybersecurity and Infrastructure Security Agency (CISA)",
        OperatorKind = OperatorKind.Mandated,
        DefaultInterval = TimeSpan.FromHours(2),
        MinimumInterval = TimeSpan.FromMinutes(30),
        AllowedHosts = ["www.cisa.gov"],
        Attribution = "CISA Known Exploited Vulnerabilities Catalog, CC0 1.0.",
        RequestsPerMinute = 6,
    };

    public async IAsyncEnumerable<Signal> FetchSignalsAsync(SyncContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var validators = HttpValidators.Parse(context.Cursor);
        // Only Last-Modified counts here; CISA answers 200 to If-None-Match.
        using var response = await context.GetIfChangedAsync(new Uri(Url), validators with { ETag = null }, cancellationToken);
        if (response.NotModified)
        {
            context.SaveCursor(validators.ToString());
            yield break;
        }

        await using var stream = await response.Response!.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        foreach (var entry in document.RootElement.GetProperty("vulnerabilities").EnumerateArray())
        {
            var cve = entry.TryGetProperty("cveID", out var id) ? id.GetString() : null;
            if (cve is null || !cve.StartsWith("CVE-", StringComparison.Ordinal))
            {
                continue;
            }

            yield return new Signal("CVE", cve)
            {
                KevListedOn = entry.TryGetProperty("dateAdded", out var added) && DateOnly.TryParseExact(added.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
                KevUrl = new Uri($"https://www.cisa.gov/known-exploited-vulnerabilities-catalog?field_cve={cve}"),
                KevKnownRansomware = entry.TryGetProperty("knownRansomwareCampaignUse", out var ransomware) ? string.Equals(ransomware.GetString(), "Known", StringComparison.OrdinalIgnoreCase) : null,
            };
        }

        context.SaveCursor(response.Validators.ToString());
    }
}
