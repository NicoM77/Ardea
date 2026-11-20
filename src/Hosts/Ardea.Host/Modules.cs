using Ardea.Assessment;
using Ardea.Assets;
using Ardea.Catalog;
using Ardea.Iam;
using Ardea.Intel;
using Ardea.Notifications;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;

namespace Ardea.Host;

internal static class Modules
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new IamModule(),
        new CatalogModule(),
        new AssetsModule(),
        new IntelModule(),
        new AssessmentModule(),
        new NotificationsModule(),
    ];

    public static IReadOnlyList<IModuleDatabase> Databases { get; } =
        [PlatformDatabase.Instance, .. All.Select(m => m.Database).OfType<IModuleDatabase>()];
}
