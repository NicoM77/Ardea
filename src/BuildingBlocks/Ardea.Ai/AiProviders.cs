using System.ClientModel;
using System.Diagnostics;
using Anthropic;
using Ardea.Platform.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OpenAI;

namespace Ardea.Ai;

/// <summary>
/// The configured providers behind <see cref="IAiProviders"/> (master plan 7.8): one <see cref="IChatClient"/> per
/// provider, built from its kind's SDK and wrapped for telemetry. Telemetry records calls, tokens, duration
/// and errors, never prompt or answer texts.
/// </summary>
internal sealed partial class AiProviders : IAiProviders, IDisposable
{
    public const string TelemetrySource = "Ardea.Ai";

    internal static readonly TimeSpan DefaultTestTimeout = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, (AiProviderOptions Options, IChatClient Client)> _providers;
    private readonly ILogger<AiProviders> _logger;
    private readonly TimeSpan _testTimeout;

    public AiProviders(IReadOnlyList<AiProviderOptions> providers, ILoggerFactory loggerFactory, Func<AiProviderOptions, IChatClient>? factory = null, TimeSpan? testTimeout = null)
    {
        _logger = loggerFactory.CreateLogger<AiProviders>();
        _testTimeout = testTimeout ?? DefaultTestTimeout;
        factory ??= Create;
        _providers = providers.ToDictionary(
            p => p.Name,
            p => (p, factory(p).AsBuilder().UseOpenTelemetry(loggerFactory, TelemetrySource, c => c.EnableSensitiveData = false).Build()),
            StringComparer.OrdinalIgnoreCase);
        All = [.. providers.Select(p => new AiProviderInfo(p.Name, p.Kind, p.Model, p.Local, p.Endpoint?.Host))];
    }

    public IReadOnlyList<AiProviderInfo> All { get; }

    public IChatClient? Client(string name) => _providers.TryGetValue(name, out var provider) ? provider.Client : null;

    public async Task<AiConnectionResult?> TestAsync(string name, CancellationToken cancellationToken)
    {
        if (Client(name) is not { } client)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_testTimeout);
        var watch = Stopwatch.StartNew();
        try
        {
            var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "Reply with the single word OK.")],
                new ChatOptions { MaxOutputTokens = 16, Temperature = 0 },
                timeout.Token);
            return string.IsNullOrWhiteSpace(response.Text)
                ? new AiConnectionResult(false, watch.ElapsedMilliseconds, "The provider answered with an empty text.")
                : new AiConnectionResult(true, watch.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AiConnectionResult(false, watch.ElapsedMilliseconds, $"No answer within {_testTimeout.TotalSeconds:F0} seconds.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTestFailed(name, ex.GetType().Name);
            return new AiConnectionResult(false, watch.ElapsedMilliseconds, Describe(ex));
        }
    }

    public void Dispose()
    {
        foreach (var (_, client) in _providers.Values)
        {
            client.Dispose();
        }
    }

    /// <summary>A message for the administrator: the kind of failure and an HTTP status, never the request or a key.</summary>
    internal static string Describe(Exception exception) => exception switch
    {
        ClientResultException { Status: 401 or 403 } => "The provider refused the key (HTTP 401/403).",
        ClientResultException { Status: 404 } => "The provider does not know the model or the address (HTTP 404).",
        ClientResultException { Status: > 0 } e => $"The provider answered with HTTP {e.Status}.",
        HttpRequestException { StatusCode: { } status } => $"The provider answered with HTTP {(int)status}.",
        HttpRequestException => "The provider could not be reached.",
        _ => $"The call failed ({exception.GetType().Name}).",
    };

    private static IChatClient Create(AiProviderOptions options) => options.Kind switch
    {
        AiProviderKinds.OpenAiCompatible => new OpenAIClient(
                new ApiKeyCredential(options.ApiKey ?? "none"), new OpenAIClientOptions { Endpoint = options.Endpoint })
            .GetChatClient(options.Model).AsIChatClient(),
        AiProviderKinds.Ollama => new OllamaApiClient(options.Endpoint!, options.Model),
        AiProviderKinds.Anthropic => new AnthropicClient { ApiKey = options.ApiKey!, BaseUrl = options.Endpoint?.ToString() ?? "https://api.anthropic.com" }
            .AsIChatClient(options.Model),
        _ => throw new InvalidOperationException($"Unknown provider kind {options.Kind}."),
    };

    [LoggerMessage(EventId = 1401, Level = LogLevel.Warning, Message = "Connection test of AI provider {Provider} failed: {Error}")]
    private partial void LogTestFailed(string provider, string error);
}

public static class AiProviderServices
{
    /// <summary>
    /// Registers the providers of the configuration (master plan 7.8, 12.4). A broken provider configuration is
    /// reported by the start-up check (<see cref="AiProviderOptions.FromConfiguration"/>), not here.
    /// </summary>
    public static IServiceCollection AddAiProviders(this IServiceCollection services, IConfiguration configuration)
    {
        var providers = AiProviderOptions.FromConfiguration(configuration, out _);
        services.AddSingleton<IAiProviders>(sp => new AiProviders(providers, sp.GetRequiredService<ILoggerFactory>()));
        return services;
    }
}
