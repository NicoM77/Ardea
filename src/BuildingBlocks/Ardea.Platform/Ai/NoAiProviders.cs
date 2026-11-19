using Microsoft.Extensions.AI;

namespace Ardea.Platform.Ai;

/// <summary>
/// The stand-in where a host registers no providers, e.g. the maintenance commands: assessment and catalogue run on
/// rules and templates alone (master plan 7.8 «Ohne Anbieter»). API and worker register the configured providers first.
/// </summary>
public sealed class NoAiProviders : IAiProviders
{
    public IReadOnlyList<AiProviderInfo> All => [];

    public IChatClient? Client(string name) => null;

    public Task<AiConnectionResult?> TestAsync(string name, CancellationToken cancellationToken) => Task.FromResult<AiConnectionResult?>(null);
}
