using System.Reflection;

namespace Ardea.Architecture.Tests;

internal static class Assemblies
{
    public static readonly Assembly SharedKernel = typeof(SharedKernel.Result).Assembly;
    public static readonly Assembly Platform = typeof(Platform.Modules.IModule).Assembly;
    public static readonly Assembly SourcesAbstractions = typeof(Intel.Sources.Abstractions.SourceKind).Assembly;

    public static readonly IReadOnlyDictionary<string, Assembly> Modules = new Dictionary<string, Assembly>
    {
        ["Iam"] = typeof(Iam.IamModule).Assembly,
        ["Catalog"] = typeof(Catalog.CatalogModule).Assembly,
        ["Intel"] = typeof(Intel.IntelModule).Assembly,
        ["Assets"] = typeof(Assets.AssetsModule).Assembly,
        ["Assessment"] = typeof(Assessment.AssessmentModule).Assembly,
        ["Notifications"] = typeof(Notifications.NotificationsModule).Assembly,
    };

    public static Assembly Contracts(string module) => Assembly.Load($"Ardea.{module}.Contracts");

    public static IEnumerable<Assembly> AllSources()
    {
        var dir = Path.GetDirectoryName(typeof(Assemblies).Assembly.Location)!;
        return Directory.GetFiles(dir, "Ardea.Intel.Sources.*.dll")
            .Select(path => Assembly.Load(AssemblyName.GetAssemblyName(path)));
    }

    public static IEnumerable<string> ArdeaReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Ardea.", StringComparison.Ordinal) || n == "ardea");
}
