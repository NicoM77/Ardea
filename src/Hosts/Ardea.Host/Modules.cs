using Ardea.Assessment;
using Ardea.Assets;
using Ardea.Catalog;
using Ardea.Iam;
using Ardea.Intel;
using Ardea.Notifications;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;

namespace Ardea.Host;

/// <summary>The modules of the instance, listed explicitly; no discovery by reflection.</summary>
internal static class Modules
{
    /// <summary>In the order of master plan 4.11, which is also the migration order.</summary>
    public static IReadOnlyList<IModule> All { get; } =
    [
        new IamModule(),
        new CatalogModule(),
        new AssetsModule(),
        new IntelModule(),
        new AssessmentModule(),
        new NotificationsModule(),
    ];

    /// <summary>Platform first, then every module that has a schema.</summary>
    public static IReadOnlyList<IModuleDatabase> Databases { get; } =
        [PlatformDatabase.Instance, .. All.Select(m => m.Database).OfType<IModuleDatabase>()];
}
