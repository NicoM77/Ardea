using System.Diagnostics;
using System.Diagnostics.Metrics;
using Ardea.Catalog.Products;
using Ardea.Platform.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Ardea.Catalog.Learning;

internal sealed record LearnResult(LearnedProposal? Proposal, string? Failure, int Attempts, int Tokens, long DurationMs);

internal sealed partial class Learner(IAiProviders providers, AiLimits limits, ILogger<Learner> logger)
{
    public const int Attempts = 2;
    public const int MaxOutputTokens = 600;

    public async Task<LearnResult> LearnAsync(LearnInput input, AiProviderInfo provider, CatalogLayers catalog, ISet<string> keys, int budget, CancellationToken cancellationToken)
    {
        if (providers.Client(provider.Name) is not { } client)
        {
            return new LearnResult(null, "no_provider", 0, 0, 0);
        }

        var messages = LearnPrompt.Messages(input);
        var maxAttempts = Math.Clamp(budget, 0, Attempts);
        var estimatedIn = messages.Sum(m => m.Text.Length) / 4;
        var options = new ChatOptions { Temperature = 0, MaxOutputTokens = MaxOutputTokens, Tools = null };
        var watch = Stopwatch.StartNew();
        int tokens = 0, attempts = 0;
        string? failure = null;
        while (attempts < maxAttempts)
        {
            attempts++;
            if (attempts > 1)
            {
                LearnMetrics.Retries.Add(1);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(limits.CallTimeout);
            try
            {
                var response = await client.GetResponseAsync<LearnAnswer>(messages, LearnAnswer.Json, options, useJsonSchemaResponseFormat: true, timeout.Token);
                tokens += response.Usage is { InputTokenCount: { } reportedIn, OutputTokenCount: { } reportedOut }
                    ? (int)(reportedIn + reportedOut)
                    : estimatedIn + MaxOutputTokens;
                var (proposal, problem) = LearnCheck.Check(response.Text, input, catalog, keys);
                LearnMetrics.Calls.Add(1, new KeyValuePair<string, object?>("outcome", problem is null ? "ok" : "invalid_output"));
                if (proposal is not null)
                {
                    return new LearnResult(proposal with { Provider = provider.Name, Model = provider.Model, PromptVersion = LearnPrompt.Version }, null, attempts, tokens, watch.ElapsedMilliseconds);
                }

                failure = Holds.InvalidOutput;
                LogInvalidOutput(provider.Name, problem!, attempts);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failure = "timeout";
                tokens += estimatedIn + MaxOutputTokens;
                LearnMetrics.Calls.Add(1, new KeyValuePair<string, object?>("outcome", failure));
                LogProviderFailed(provider.Name, failure, attempts);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failure = "provider_error";
                tokens += estimatedIn + MaxOutputTokens;
                LearnMetrics.Calls.Add(1, new KeyValuePair<string, object?>("outcome", failure));
                LogProviderFailed(provider.Name, ex.GetType().Name, attempts);
                break;
            }
        }

        return new LearnResult(null, failure ?? "no_budget", attempts, tokens, watch.ElapsedMilliseconds);
    }

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning, Message = "The catalogue's answer of AI provider {Provider} failed the check {Check} (attempt {Attempt})")]
    private partial void LogInvalidOutput(string provider, string check, int attempt);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Warning, Message = "AI provider {Provider} failed for the catalogue: {Error} (attempt {Attempt})")]
    private partial void LogProviderFailed(string provider, string error, int attempt);
}

internal static class LearnMetrics
{
    public const string MeterName = "Ardea.Catalog.Learning";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Calls = Meter.CreateCounter<long>("ardea.catalog.learning.calls", description: "Calls to a language model for the catalogue by outcome.");

    public static readonly Counter<long> Retries = Meter.CreateCounter<long>("ardea.catalog.learning.retries", description: "Second attempts after an invalid answer.");

    public static readonly Counter<long> Keys = Meter.CreateCounter<long>("ardea.catalog.learning.keys", description: "Unknown product keys worked on, by outcome.");
}
