using Ardea.Intel.Sources.Abstractions;

namespace Ardea.Intel.Sync;

internal sealed class SourceRegistry
{
    private readonly Dictionary<string, ISourceProvider> _providers;
    private readonly Dictionary<string, ISignalProvider> _signals;

    public SourceRegistry(IEnumerable<ISourceProvider> providers, IEnumerable<ISignalProvider> signals)
    {
        _providers = providers.ToDictionary(p => p.Descriptor.Key, StringComparer.Ordinal);
        _signals = signals.ToDictionary(p => p.Descriptor.Key, StringComparer.Ordinal);
        var duplicate = _providers.Keys.Intersect(_signals.Keys, StringComparer.Ordinal).FirstOrDefault();
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Source key {duplicate} is used by two adapters.");
        }
    }

    public IEnumerable<SourceDescriptor> Descriptors =>
        _providers.Values.Select(p => p.Descriptor).Concat(_signals.Values.Select(s => s.Descriptor));

    public bool TryGetProvider(string key, out ISourceProvider provider) => _providers.TryGetValue(key, out provider!);

    public bool TryGetSignalProvider(string key, out ISignalProvider provider) => _signals.TryGetValue(key, out provider!);

    public string AdapterName(string key) =>
        _providers.TryGetValue(key, out var p) ? p.GetType().FullName! : _signals[key].GetType().FullName!;
}
