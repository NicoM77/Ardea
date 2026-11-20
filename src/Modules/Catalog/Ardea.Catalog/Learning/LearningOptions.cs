using System.Globalization;
using Ardea.Platform.Ai;
using Ardea.Platform.Settings;
using Microsoft.Extensions.Configuration;

namespace Ardea.Catalog.Learning;

internal sealed record LearningOptions(int DailyCalls, int MinRecords)
{
    public const string DailyCallsKey = "AI:CATALOG_DAILY_CALLS";

    public const int KeysPerRun = 10;

    public int RuleBatchSize { get; init; } = 250;

    public int RuleBatchesPerRun { get; init; } = 20;

    public static readonly LearningOptions Default = new(200, 2);

    public static readonly SettingDefinition Provider = new("catalog.learning_provider", "AI:CATALOG_PROVIDER");

    public const string NoProvider = "none";

    public static LearningOptions FromConfiguration(IConfiguration configuration) =>
        new(
            int.TryParse(configuration[DailyCallsKey], NumberStyles.None, CultureInfo.InvariantCulture, out var calls) && calls >= 0 ? calls : Default.DailyCalls,
            Default.MinRecords);

    public static AiProviderInfo? Choose(string? setting, IReadOnlyList<AiProviderInfo> providers) => setting?.Trim() switch
    {
        null or "" => providers.FirstOrDefault(p => p.Local),
        NoProvider => null,
        var name => providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal)),
    };
}
