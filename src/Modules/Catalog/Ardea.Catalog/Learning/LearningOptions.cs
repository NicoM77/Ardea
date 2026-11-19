using System.Globalization;
using Ardea.Platform.Ai;
using Ardea.Platform.Settings;
using Microsoft.Extensions.Configuration;

namespace Ardea.Catalog.Learning;

/// <summary>
/// The limits of the catalogue that learns (master plan 6.9 «Grenzen», 7.8): <c>ARDEA_AI__CATALOG_DAILY_CALLS</c> calls a
/// day (200), a key from two records on or at once when one of them is in KEV, and at most
/// <see cref="KeysPerRun"/> keys per run of the job; the next run continues.
/// </summary>
/// <param name="DailyCalls">Calls to the model per UTC day, retries included; the catalogue's own quota.</param>
/// <param name="MinRecords">Records a raw key needs before it is worked on, unless one of them is in KEV.</param>
internal sealed record LearningOptions(int DailyCalls, int MinRecords)
{
    public const string DailyCallsKey = "AI:CATALOG_DAILY_CALLS";

    /// <summary>Keys asked about in one run of <c>GrowCatalog</c>: with two calls of at most two minutes each, a run stays within its time limit.</summary>
    public const int KeysPerRun = 10;

    /// <summary>Keys the mapping without a model works on in one unit of work.</summary>
    public int RuleBatchSize { get; init; } = 250;

    /// <summary>
    /// Batches of the mapping without a model in one run; the rest goes to a continuation. After a first load of 180 days
    /// thousands of keys wait: 20 batches of 250 keep a run well inside its time limit.
    /// </summary>
    public int RuleBatchesPerRun { get; init; } = 20;

    public static readonly LearningOptions Default = new(200, 2);

    /// <summary>
    /// Which provider the catalogue asks (ADR 040): a provider's name, or <c>none</c>. Without a value the first local
    /// provider; a provider that is not local only when the instance administrator names it here. Settable in the
    /// administration, or locked by <c>ARDEA_AI__CATALOG_PROVIDER</c>.
    /// </summary>
    public static readonly SettingDefinition Provider = new("catalog.learning_provider", "AI:CATALOG_PROVIDER");

    /// <summary>The value of <see cref="Provider"/> that switches the model off for the catalogue.</summary>
    public const string NoProvider = "none";

    public static LearningOptions FromConfiguration(IConfiguration configuration) =>
        new(
            int.TryParse(configuration[DailyCallsKey], NumberStyles.None, CultureInfo.InvariantCulture, out var calls) && calls >= 0 ? calls : Default.DailyCalls,
            Default.MinRecords);

    /// <summary>
    /// The provider the catalogue may ask, or <c>null</c>: the one the setting names (also one that is not local: naming
    /// it is the administrator's release), <c>none</c> for none, and without a setting the first local provider. The
    /// catalogue sends public data of the sources only, so no organisation's data policy applies (master plan 7.8).
    /// </summary>
    public static AiProviderInfo? Choose(string? setting, IReadOnlyList<AiProviderInfo> providers) => setting?.Trim() switch
    {
        null or "" => providers.FirstOrDefault(p => p.Local),
        NoProvider => null,
        var name => providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal)),
    };
}
