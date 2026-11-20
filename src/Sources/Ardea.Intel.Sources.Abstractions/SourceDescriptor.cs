namespace Ardea.Intel.Sources.Abstractions;

public enum OperatorKind
{
    Authority,

    Mandated,

    Recognized,

    Vendor,

    Community,
}

public enum TermsMode
{
    Full,

    MetadataOnly,
}

public sealed record SourceDescriptor
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required SourceKind Kind { get; init; }

    public required string Operator { get; init; }

    public required OperatorKind OperatorKind { get; init; }

    public required TimeSpan DefaultInterval { get; init; }

    public TimeSpan MinimumInterval { get; init; } = TimeSpan.FromMinutes(15);

    public required IReadOnlyList<string> AllowedHosts { get; init; }

    public required string Attribution { get; init; }

    public TermsMode Terms { get; init; } = TermsMode.Full;

    public int RequestsPerMinute { get; init; } = 60;

    public long MaxResponseBytes { get; init; } = 50L * 1024 * 1024;

    public long MaxBackfillBytes { get; init; } = 1024L * 1024 * 1024;

    public bool EnabledByDefault { get; init; } = true;

    public int ParserVersion { get; init; } = 1;
}
