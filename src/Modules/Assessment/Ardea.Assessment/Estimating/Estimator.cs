using System.Diagnostics;
using System.Diagnostics.Metrics;
using Ardea.Platform.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Ardea.Assessment.Estimating;

/// <summary>What came of asking the model: a checked answer, or why there is none, with the tokens spent either way.</summary>
internal sealed record EstimateResult(StoredEstimate? Estimate, string? Failure, AiProviderInfo? Provider, int Attempts, int TokensIn, int TokensOut, long DurationMs);

/// <summary>
/// Asks the language model for one alert (master plan 7.5, 7.8; tasks 4.2, 4.6). Two attempts at most: an answer that
/// fails the check is asked again once; a provider that does not answer gives way to the next allowed one, local ones
/// first. Only the providers the organisation's data policy allows are ever called, also when all of them fail.
/// The model has no tools; the time limit holds per call. Metrics count calls, tokens, duration, errors and retries;
/// logs never carry prompt or answer texts.
/// </summary>
internal sealed partial class Estimator(IAiProviders providers, AiLimits limits, ILogger<Estimator> logger)
{
    public const int Attempts = 2;
    public const int MaxOutputTokens = 2000;

    public async Task<EstimateResult> EstimateAsync(EstimateInput input, IReadOnlyList<AiProviderInfo> allowed, CancellationToken cancellationToken)
    {
        var messages = EstimatePrompt.Messages(input);
        var options = new ChatOptions { Temperature = 0, MaxOutputTokens = MaxOutputTokens, Tools = null };
        var watch = Stopwatch.StartNew();
        int tokensIn = 0, tokensOut = 0, attempts = 0, index = 0;
        string? failure = null;
        AiProviderInfo? provider = null;
        while (attempts < Attempts && index < allowed.Count)
        {
            provider = allowed[index];
            if (providers.Client(provider.Name) is not { } client)
            {
                index++;
                continue;
            }

            attempts++;
            if (attempts > 1)
            {
                EstimateMetrics.Retries.Add(1, new KeyValuePair<string, object?>("provider", provider.Name));
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(limits.CallTimeout);
            var call = Stopwatch.StartNew();
            try
            {
                var response = await client.GetResponseAsync<EstimateAnswer>(messages, StoredEstimate.Json, options, useJsonSchemaResponseFormat: true, timeout.Token);
                var used = ((int)(response.Usage?.InputTokenCount ?? 0), (int)(response.Usage?.OutputTokenCount ?? 0));
                tokensIn += used.Item1;
                tokensOut += used.Item2;
                EstimateMetrics.Tokens(provider.Name, used.Item1, used.Item2);
                var (estimate, problem) = EstimateCheck.Check(response.Text, input);
                EstimateMetrics.Call(provider.Name, problem is null ? "ok" : "invalid_output", call.Elapsed);
                if (estimate is not null)
                {
                    return new EstimateResult(estimate, null, provider, attempts, tokensIn, tokensOut, watch.ElapsedMilliseconds);
                }

                failure = "invalid_output";
                LogInvalidOutput(provider.Name, problem!, attempts);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failure = "timeout";
                EstimateMetrics.Call(provider.Name, failure, call.Elapsed);
                LogProviderFailed(provider.Name, failure, attempts);
                index = Math.Min(index + 1, allowed.Count - 1);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failure = "provider_error";
                EstimateMetrics.Call(provider.Name, failure, call.Elapsed);
                LogProviderFailed(provider.Name, ex.GetType().Name, attempts);
                index = Math.Min(index + 1, allowed.Count - 1);
            }
        }

        return new EstimateResult(null, failure ?? "no_provider", provider, attempts, tokensIn, tokensOut, watch.ElapsedMilliseconds);
    }

    [LoggerMessage(EventId = 6101, Level = LogLevel.Warning, Message = "The answer of AI provider {Provider} failed the check {Check} (attempt {Attempt})")]
    private partial void LogInvalidOutput(string provider, string check, int attempt);

    [LoggerMessage(EventId = 6102, Level = LogLevel.Warning, Message = "AI provider {Provider} failed: {Error} (attempt {Attempt})")]
    private partial void LogProviderFailed(string provider, string error, int attempt);
}

/// <summary>The assessment's measurements (master plan 7.8 «Telemetrie»): counts, tokens and durations, never texts.</summary>
internal static class EstimateMetrics
{
    public const string MeterName = "Ardea.Assessment.Ai";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Calls = Meter.CreateCounter<long>("ardea.ai.calls", description: "Calls to a language model by outcome.");

    private static readonly Counter<long> TokenCount = Meter.CreateCounter<long>("ardea.ai.tokens", "{token}", "Tokens sent to and received from a language model.");

    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("ardea.ai.call.duration", "s", "Duration of one call to a language model.");

    private static readonly Counter<long> Errors = Meter.CreateCounter<long>("ardea.ai.errors", description: "Calls without a usable answer: invalid output, timeout, provider error.");

    public static readonly Counter<long> Retries = Meter.CreateCounter<long>("ardea.ai.retries", description: "Second attempts after an invalid answer or a failed call.");

    /// <summary>Assessments by outcome: <c>stored</c>, <c>without_model</c>, or why none was asked for.</summary>
    public static readonly Counter<long> Estimates = Meter.CreateCounter<long>("ardea.ai.estimates", description: "Assessments of alerts by outcome.");

    public static void Call(string provider, string outcome, TimeSpan elapsed)
    {
        var tags = new TagList { { "provider", provider }, { "outcome", outcome } };
        Calls.Add(1, tags);
        Duration.Record(elapsed.TotalSeconds, tags);
        if (outcome != "ok")
        {
            Errors.Add(1, tags);
        }
    }

    public static void Tokens(string provider, int input, int output)
    {
        TokenCount.Add(input, new TagList { { "provider", provider }, { "direction", "input" } });
        TokenCount.Add(output, new TagList { { "provider", provider }, { "direction", "output" } });
    }
}
