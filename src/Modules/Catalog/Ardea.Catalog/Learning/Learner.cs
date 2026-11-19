using System.Diagnostics;
using System.Diagnostics.Metrics;
using Ardea.Catalog.Products;
using Ardea.Platform.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Ardea.Catalog.Learning;

/// <summary>What came of asking the model about one raw key: a checked proposal, or why there is none, with the calls and tokens spent.</summary>
internal sealed record LearnResult(LearnedProposal? Proposal, string? Failure, int Attempts, int Tokens, long DurationMs);

/// <summary>
/// Asks the language model about one raw key (master plan 6.9 step 3 and 4; the pattern of ADR 037). Two attempts at
/// most: an answer that fails the check is asked again once. One provider, the catalogue's own (ADR 040); the model has
/// no tools; the time limit holds per call. A call without a usage report is charged its estimate. Metrics count calls,
/// tokens and outcomes; logs carry the raw key's length, never the source's or the model's texts.
/// </summary>
internal sealed partial class Learner(IAiProviders providers, AiLimits limits, ILogger<Learner> logger)
{
    public const int Attempts = 2;
    public const int MaxOutputTokens = 600;

    /// <param name="input">The raw key's input.</param>
    /// <param name="provider">The catalogue's provider.</param>
    /// <param name="catalog">The catalogue in effect, to check the answer against.</param>
    /// <param name="keys">Every key of <c>catalog.product</c>, also of withdrawn entries.</param>
    /// <param name="budget">Calls left in the day's quota; at most <see cref="Attempts"/> are made.</param>
    /// <param name="cancellationToken">Cancels the job.</param>
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

/// <summary>The catalogue's measurements of its model use (master plan 7.8 «Telemetrie»): counts, never texts.</summary>
internal static class LearnMetrics
{
    public const string MeterName = "Ardea.Catalog.Learning";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Calls = Meter.CreateCounter<long>("ardea.catalog.learning.calls", description: "Calls to a language model for the catalogue by outcome.");

    public static readonly Counter<long> Retries = Meter.CreateCounter<long>("ardea.catalog.learning.retries", description: "Second attempts after an invalid answer.");

    /// <summary>Raw keys by what came of them: <c>rule</c>, <c>adopted</c>, <c>pending</c>, <c>unclear</c>, <c>failed</c>.</summary>
    public static readonly Counter<long> Keys = Meter.CreateCounter<long>("ardea.catalog.learning.keys", description: "Unknown product keys worked on, by outcome.");
}
