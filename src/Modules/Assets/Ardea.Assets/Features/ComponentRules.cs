using Ardea.Assets.Persistence;
using Ardea.Catalog.Contracts;

namespace Ardea.Assets.Features;

/// <summary>
/// A component as the API takes it; <see cref="Ref"/> and <see cref="ParentRef"/> link new items within one request.
/// <see cref="VersionPrecision"/> (<c>none</c>, <c>line</c>, <c>exact</c>) follows from the version when left out;
/// <see cref="NoticeThreshold"/> (<c>only_important</c>, <c>everything</c>) is <c>only_important</c> when left out
/// (master plan 17.2, ADR 044, ADR 046).
/// </summary>
internal sealed record ComponentInput(
    Guid? Id,
    string? Ref,
    string? ProductKey,
    string? CustomVendor,
    string? CustomName,
    string? Kind,
    string? Version,
    string? Packaging,
    Guid? ParentId,
    string? ParentRef,
    string? Exposure,
    string? Criticality,
    string? Protection,
    string? ProtectionNote,
    string? VersionPrecision = null,
    string? NoticeThreshold = null);

/// <summary>The checked values of a component, ready to write.</summary>
internal sealed record ComponentValues(
    string? ProductKey,
    string? CustomVendor,
    string? CustomName,
    string Kind,
    string? Version,
    string? VersionScheme,
    string Packaging,
    string? Exposure,
    string? Criticality,
    string Protection,
    string? ProtectionNote,
    string VersionPrecision,
    string NoticeThreshold)
{
    /// <summary>What identifies a component without its id when a profile is replaced (master plan 4.9).</summary>
    public string Identity => ProductKey ?? $"custom:{Normalize(CustomVendor)}/{Normalize(CustomName)}";

    public static string IdentityOf(Component c) => c.ProductKey ?? $"custom:{Normalize(c.CustomVendor)}/{Normalize(c.CustomName)}";

    public bool SameAs(Component c) =>
        c.ProductKey == ProductKey && c.CustomVendor == CustomVendor && c.CustomName == CustomName && c.Kind == Kind
        && c.Version == Version && c.VersionScheme == VersionScheme && c.Packaging == Packaging && c.Exposure == Exposure
        && c.Criticality == Criticality && c.Protection == Protection && c.ProtectionNote == ProtectionNote
        && c.VersionPrecision == VersionPrecision && c.NoticeThreshold == NoticeThreshold;

    public void ApplyTo(Component c)
    {
        c.ProductKey = ProductKey;
        c.CustomVendor = CustomVendor;
        c.CustomName = CustomName;
        c.Kind = Kind;
        c.Version = Version;
        c.VersionPrecision = VersionPrecision;
        c.NoticeThreshold = NoticeThreshold;
        c.VersionScheme = VersionScheme;
        c.Packaging = Packaging;
        c.Exposure = Exposure;
        c.Criticality = Criticality;
        c.Protection = Protection;
        c.ProtectionNote = ProtectionNote;
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}

/// <summary>Checks component input against the catalogue (master plan 4.5).</summary>
internal static class ComponentRules
{
    public static readonly string[] Kinds = [.. Enum.GetValues<ProductKind>().Select(k => k.ToString().ToLowerInvariant())];
    public static readonly string[] Packagings = ["upstream", "distribution", "unknown"];
    public static readonly string[] Exposures = ["internet", "internal", "isolated"];
    public static readonly string[] Criticalities = ["low", "normal", "high"];
    public static readonly string[] Protections = ["none", "effective"];

    /// <summary>
    /// The checked values, or field errors under <paramref name="prefix"/>. A catalogue product brings
    /// its kind and version scheme; a product the catalogue does not know needs a name and a kind.
    /// Services have no version (master plan 4.5).
    /// </summary>
    public static async Task<ComponentValues?> CheckAsync(
        ComponentInput input, IProductCatalog catalog, Dictionary<string, string[]> errors, string prefix, CancellationToken cancellationToken)
    {
        var before = errors.Count;
        void Error(string field, string message) => errors.TryAdd(prefix + field, [message]);

        string kind;
        string? productKey = null, vendor = null, name = null, scheme = null;
        if (!string.IsNullOrWhiteSpace(input.ProductKey))
        {
            var product = await catalog.FindByKeyAsync(input.ProductKey.Trim(), cancellationToken);
            if (product is null)
            {
                Error("productKey", "The catalogue has no such product.");
                return null;
            }

            productKey = product.Key;
            kind = product.Kind.ToString().ToLowerInvariant();
            scheme = product.VersionScheme;
        }
        else
        {
            name = input.CustomName?.Trim();
            vendor = string.IsNullOrWhiteSpace(input.CustomVendor) ? null : input.CustomVendor.Trim();
            kind = input.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                Error("customName", "Choose a product from the catalogue or enter its name.");
            }
            else if (name.Length > 200)
            {
                Error("customName", "Use at most 200 characters.");
            }

            if (vendor?.Length > 200)
            {
                Error("customVendor", "Use at most 200 characters.");
            }

            if (!Kinds.Contains(kind))
            {
                Error("kind", "Choose the kind of technology.");
            }
        }

        var version = string.IsNullOrWhiteSpace(input.Version) ? null : input.Version.Trim();
        if (version is not null && kind == "service")
        {
            Error("version", "Services have no version.");
        }
        else if (version?.Length > 100)
        {
            Error("version", "Use at most 100 characters.");
        }

        var packaging = input.Packaging?.Trim().ToLowerInvariant() ?? "unknown";
        var exposure = Empty(input.Exposure);
        var criticality = Empty(input.Criticality);
        var protection = input.Protection?.Trim().ToLowerInvariant() ?? "none";
        var note = Empty(input.ProtectionNote, lower: false);
        if (!Packagings.Contains(packaging))
        {
            Error("packaging", "Choose upstream, distribution or unknown.");
        }

        if (exposure is not null && !Exposures.Contains(exposure))
        {
            Error("exposure", "Choose internet, internal or isolated.");
        }

        if (criticality is not null && !Criticalities.Contains(criticality))
        {
            Error("criticality", "Choose low, normal or high.");
        }

        if (!Protections.Contains(protection))
        {
            Error("protection", "Choose none or effective.");
        }

        if (note?.Length > 1000)
        {
            Error("protectionNote", "Use at most 1000 characters.");
        }

        // A stated precision must fit the version; without one it follows from the version (ADR 046).
        var precision = Empty(input.VersionPrecision) ?? VersionPrecisions.Of(version, null, "manual");
        if (!VersionPrecisions.All.Contains(precision))
        {
            Error("versionPrecision", "Choose none, line or exact.");
        }
        else if (!VersionPrecisions.Fits(precision, version))
        {
            Error("versionPrecision", version is null ? "Without a version the precision is none." : "With a version, choose line or exact.");
        }

        var threshold = Empty(input.NoticeThreshold) ?? NoticeThresholds.OnlyImportant;
        if (!NoticeThresholds.All.Contains(threshold))
        {
            Error("noticeThreshold", "Choose only_important or everything.");
        }

        return errors.Count > before ? null
            : new ComponentValues(productKey, vendor, name, kind, version, scheme, packaging, exposure, criticality, protection, note, precision, threshold);
    }

    private static string? Empty(string? value, bool lower = true) =>
        string.IsNullOrWhiteSpace(value) ? null : lower ? value.Trim().ToLowerInvariant() : value.Trim();
}
