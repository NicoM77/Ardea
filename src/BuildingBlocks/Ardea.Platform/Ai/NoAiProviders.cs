using Microsoft.Extensions.AI;

namespace Ardea.Platform.Ai;

public sealed class NoAiProviders : IAiProviders
{
    public IReadOnlyList<AiProviderInfo> All => [];

    public IChatClient? Client(string name) => null;

    public Task<AiConnectionResult?> TestAsync(string name, CancellationToken cancellationToken) => Task.FromResult<AiConnectionResult?>(null);
}
