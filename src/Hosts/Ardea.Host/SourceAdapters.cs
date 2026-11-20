using System.Reflection;
using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Host;

internal static class SourceAdapters
{
    public static IServiceCollection AddSourceAdapters(this IServiceCollection services)
    {
        foreach (var type in Discover())
        {
            if (typeof(ISourceProvider).IsAssignableFrom(type))
            {
                services.AddSingleton(typeof(ISourceProvider), type);
            }

            if (typeof(ISignalProvider).IsAssignableFrom(type))
            {
                services.AddSingleton(typeof(ISignalProvider), type);
            }
        }

        return services;
    }

    internal static IEnumerable<Type> Discover()
    {
        var directory = AppContext.BaseDirectory;
        foreach (var path in Directory.GetFiles(directory, "Ardea.Intel.Sources.*.dll").Order(StringComparer.Ordinal))
        {
            var name = AssemblyName.GetAssemblyName(path);
            if (name.Name is "Ardea.Intel.Sources.Abstractions" or "Ardea.Intel.Sources.Formats")
            {
                continue;
            }

            foreach (var type in Assembly.Load(name).GetExportedTypes())
            {
                if (type is { IsClass: true, IsAbstract: false }
                    && (typeof(ISourceProvider).IsAssignableFrom(type) || typeof(ISignalProvider).IsAssignableFrom(type)))
                {
                    yield return type;
                }
            }
        }
    }
}
