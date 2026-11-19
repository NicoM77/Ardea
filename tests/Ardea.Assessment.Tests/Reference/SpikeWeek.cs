using System.IO.Compression;
using System.Text.Json;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Assessment.Tests.Reference;

/// <summary>
/// The spike week as a test dataset (task 3.7; <c>prototypes/relevance-funnel</c>): every CVE record that
/// appeared or changed from 11 to 17 November 2026, newest state each, and the three example projects of
/// the requirements. Their versions are the ones installed before that week's security updates, the
/// situation the spike asks about: what does one week bring to an organisation that was up to date.
/// </summary>
internal static class SpikeWeek
{
    public static readonly IReadOnlyList<(string Name, ReferenceProject Project, IReadOnlyList<ReferenceComponent> Components)> Projects =
    [
        ("A: .NET, Angular, Umbraco, Azure SQL", new ReferenceProject("normal", "internet"),
        [
            Product("dotnet", "runtime", "10.0.403"),
            Product("angular", "framework", "22.2.0"),
            Product("umbraco", "cms", "17.2.0"),
            // A managed service: the catalogue does not know it, and self-hosted SQL Server records do not concern it.
            new ReferenceComponent(null, null, "Azure SQL Database", "service", null, "unknown", null),
        ]),
        ("B: WordPress, PHP, MySQL", new ReferenceProject("normal", "internet"),
        [
            Product("wordpress", "cms", "6.9.1"),
            Product("php", "runtime", "8.4.26"),
            Product("mysql", "database", "8.4.13"),
        ]),
        ("C: Kubernetes, Linux, Redis, PostgreSQL", new ReferenceProject("high", "internal"),
        [
            Product("kubernetes", "container", "1.34.2"),
            new ReferenceComponent("linux-kernel", null, "Linux kernel", "os", "6.12.58", "distribution", null),
            Product("redis", "database", "8.2.3"),
            Product("postgresql", "database", "17.11"),
        ]),
    ];

    public static IReadOnlyList<RawRecord> Records()
    {
        using var file = File.OpenRead(Path.Combine(ReferenceSet.Folder, "spike-week.jsonl.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var records = new List<RawRecord>();
        while (reader.ReadLine() is { Length: > 0 } line)
        {
            using var record = JsonDocument.Parse(line);
            records.Add(new RawRecord(record.RootElement.GetProperty("cveMetadata").GetProperty("cveId").GetString()!, line, "application/json"));
        }

        return records;
    }

    private static ReferenceComponent Product(string key, string kind, string version) => new(key, null, key, kind, version, "upstream", null);
}
