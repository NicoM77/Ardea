namespace Ardea.Intel.Sources.Abstractions;

/// <summary>Who runs a source (master plan 5.6). It labels evidence; it does not rank sources when merging.</summary>
public enum OperatorKind
{
    /// <summary>A national authority, e.g. the NCSC/BACS.</summary>
    Authority,

    /// <summary>An organisation with a public mandate, e.g. CISA, the CVE Program.</summary>
    Mandated,

    /// <summary>A recognised community body, e.g. FIRST, OSV.</summary>
    Recognized,

    Vendor,

    Community,
}

/// <summary>How much of a source's text Ardea may keep (master plan 5.6, 15.1).</summary>
public enum TermsMode
{
    Full,

    /// <summary>Only title, summary and link; no full texts.</summary>
    MetadataOnly,
}

/// <summary>Everything the core needs to know about a source before talking to it (master plan 5.2).</summary>
public sealed record SourceDescriptor
{
    /// <summary>Stable key, lower case with dashes, e.g. <c>cve-program</c>.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required SourceKind Kind { get; init; }

    /// <summary>The operator's name as shown with evidence, e.g. <c>CVE Program</c>.</summary>
    public required string Operator { get; init; }

    public required OperatorKind OperatorKind { get; init; }

    public required TimeSpan DefaultInterval { get; init; }

    /// <summary>The shortest interval an operator may configure; protects the source.</summary>
    public TimeSpan MinimumInterval { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Hosts the adapter may call; any other host is refused by the platform's HTTP client.</summary>
    public required IReadOnlyList<string> AllowedHosts { get; init; }

    /// <summary>Attribution shown with the source's data, e.g. "© CVE Program, used under its terms of use".</summary>
    public required string Attribution { get; init; }

    public TermsMode Terms { get; init; } = TermsMode.Full;

    /// <summary>Requests per minute per host and worker process; the source's documented limit as default.</summary>
    public int RequestsPerMinute { get; init; } = 60;

    /// <summary>Largest response the platform accepts from this source in a normal run.</summary>
    public long MaxResponseBytes { get; init; } = 50L * 1024 * 1024;

    /// <summary>Largest single download in backfill mode, e.g. a full archive.</summary>
    public long MaxBackfillBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>Whether the source is on when first registered. Operators can switch any source.</summary>
    public bool EnabledByDefault { get; init; } = true;

    /// <summary>Parser version of <c>Normalize</c>; raising it lets the core re-normalise stored raw records.</summary>
    public int ParserVersion { get; init; } = 1;
}
