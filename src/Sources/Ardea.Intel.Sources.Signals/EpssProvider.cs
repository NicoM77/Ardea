using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Sources.Signals;

public sealed class EpssProvider : ISignalProvider
{
    public const string Key = "first-epss";
    internal const string Url = "https://epss.empiricalsecurity.com/epss_scores-current.csv.gz";

    public SourceDescriptor Descriptor { get; } = new()
    {
        Key = Key,
        Name = "FIRST EPSS",
        Kind = SourceKind.Signal,
        Operator = "Forum of Incident Response and Security Teams (FIRST)",
        OperatorKind = OperatorKind.Recognized,
        DefaultInterval = TimeSpan.FromHours(3),
        MinimumInterval = TimeSpan.FromHours(1),
        AllowedHosts = ["epss.empiricalsecurity.com"],
        Attribution = "EPSS scores by FIRST (first.org/epss), used with attribution.",
        RequestsPerMinute = 6,
    };

    public async IAsyncEnumerable<Signal> FetchSignalsAsync(SyncContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var validators = HttpValidators.Parse(context.Cursor);
        using var response = await context.GetIfChangedAsync(new Uri(Url), validators, cancellationToken);
        if (response.NotModified)
        {
            context.SaveCursor(validators.ToString());
            yield break;
        }

        await using var compressed = await response.Response!.Content.ReadAsStreamAsync(cancellationToken);
        await using var csv = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(csv);
        await foreach (var signal in ParseAsync(reader, cancellationToken))
        {
            yield return signal;
        }

        context.SaveCursor(response.Validators.ToString());
    }

    internal static async IAsyncEnumerable<Signal> ParseAsync(TextReader reader, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0 || line[0] == '#' || line.StartsWith("cve,", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length < 3 || !parts[0].StartsWith("CVE-", StringComparison.Ordinal)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var score)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var percentile))
            {
                continue;
            }

            yield return new Signal("CVE", parts[0]) { EpssScore = score, EpssPercentile = percentile };
        }
    }
}
