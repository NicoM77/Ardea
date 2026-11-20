using Ardea.Ai;
using Ardea.Platform.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ardea.Platform.Tests.Ai;

public sealed class AiProviderTests
{
    [Fact]
    public void Providers_are_read_by_name_with_kind_model_endpoint_and_whether_they_are_local()
    {
        var providers = AiProviderOptions.FromConfiguration(Configuration(new()
        {
            ["AI:PROVIDERS:local:KIND"] = "ollama",
            ["AI:PROVIDERS:local:ENDPOINT"] = "http://ollama:11434",
            ["AI:PROVIDERS:local:MODEL"] = "qwen3:14b",
            ["AI:PROVIDERS:vllm:KIND"] = "OpenAI",
            ["AI:PROVIDERS:vllm:ENDPOINT"] = "https://llm.internal.example/v1",
            ["AI:PROVIDERS:vllm:MODEL"] = "gpt-oss-20b",
            ["AI:PROVIDERS:vllm:LOCAL"] = "true",
            ["AI:PROVIDERS:claude:KIND"] = "anthropic",
            ["AI:PROVIDERS:claude:MODEL"] = "claude-haiku-5-5",
            ["AI:PROVIDERS:claude:API_KEY"] = "sk-secret",
        }), out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            ["claude anthropic claude-haiku-5-5 False ", "local ollama qwen3:14b True ollama", "vllm openai gpt-oss-20b True llm.internal.example"],
            providers.Select(p => $"{p.Name} {p.Kind} {p.Model} {p.Local} {p.Endpoint?.Host}"));
    }

    [Fact]
    public void A_wrong_provider_configuration_names_every_problem_and_never_a_value()
    {
        AiProviderOptions.FromConfiguration(Configuration(new()
        {
            ["AI:PROVIDERS:a:KIND"] = "gemini",
            ["AI:PROVIDERS:b:KIND"] = "openai",
            ["AI:PROVIDERS:b:MODEL"] = "m",
            ["AI:PROVIDERS:c:KIND"] = "anthropic",
            ["AI:PROVIDERS:c:MODEL"] = "m",
            ["AI:PROVIDERS:c:LOCAL"] = "true",
            ["AI:PROVIDERS:d:KIND"] = "ollama",
            ["AI:PROVIDERS:d:MODEL"] = "m",
            ["AI:PROVIDERS:d:ENDPOINT"] = "ftp://secret-host",
            ["AI:PROVIDERS:d:API_KEY"] = "sk-secret",
        }), out var errors);

        Assert.Equal(
            [
                "ARDEA_AI__PROVIDERS__a__KIND must be one of openai, ollama, anthropic.",
                "ARDEA_AI__PROVIDERS__a__MODEL is required.",
                "ARDEA_AI__PROVIDERS__b__ENDPOINT is required for openai.",
                "ARDEA_AI__PROVIDERS__c__API_KEY is required for anthropic.",
                "ARDEA_AI__PROVIDERS__c__LOCAL cannot be true for anthropic: it is a hosted service.",
                "ARDEA_AI__PROVIDERS__d__ENDPOINT must be an absolute http or https address.",
            ],
            errors);
        Assert.DoesNotContain(errors, e => e.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_providers_the_list_is_empty()
    {
        Assert.Empty(AiProviderOptions.FromConfiguration(Configuration([]), out var errors));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task The_connection_test_reports_an_answer_a_failure_and_a_timeout_without_details_of_the_request()
    {
        AiProviderOptions Provider(string name) => new(name, AiProviderKinds.OpenAiCompatible, new Uri("http://llm"), "m", "sk-secret", Local: true);
        using var providers = new AiProviders(
            [Provider("ok"), Provider("refused"), Provider("slow")],
            NullLoggerFactory.Instance,
            p => p.Name switch
            {
                "ok" => new FakeChat((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "OK")))),
                "refused" => new FakeChat((_, _) => throw new HttpRequestException("sk-secret was refused", null, System.Net.HttpStatusCode.Unauthorized)),
                _ => new FakeChat(async (_, token) =>
                {
                    await Task.Delay(Timeout.Infinite, token);
                    return new ChatResponse();
                }),
            },
            testTimeout: TimeSpan.FromMilliseconds(200));

        Assert.True((await providers.TestAsync("OK", TestContext.Current.CancellationToken))!.Ok);
        var refused = await providers.TestAsync("refused", TestContext.Current.CancellationToken);
        Assert.Equal((false, "The provider answered with HTTP 401."), (refused!.Ok, refused.Error));
        var slow = await providers.TestAsync("slow", TestContext.Current.CancellationToken);
        Assert.Equal((false, "No answer within 0 seconds."), (slow!.Ok, slow.Error));
        Assert.Null(await providers.TestAsync("missing", TestContext.Current.CancellationToken));
        Assert.Equal(["ok", "refused", "slow"], providers.All.Select(p => p.Name));
        Assert.All(providers.All, p => Assert.Equal("llm", p.Host));
    }

    [Theory]
    [InlineData(AiProviderKinds.OpenAiCompatible)]
    [InlineData(AiProviderKinds.Ollama)]
    [InlineData(AiProviderKinds.Anthropic)]
    public async Task No_provider_follows_a_redirect_so_neither_its_key_nor_an_internal_answer_moves_elsewhere(string kind)
    {
        using var listener = new System.Net.HttpListener();
        var port = FreePort();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var redirected = 0;
        var server = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                System.Net.HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (System.Net.HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (context.Request.Url!.AbsolutePath.StartsWith("/elsewhere", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref redirected);
                    context.Response.StatusCode = 200;
                }
                else
                {
                    context.Response.StatusCode = 307;
                    context.Response.RedirectLocation = $"http://127.0.0.1:{port}/elsewhere";
                }

                context.Response.Close();
            }
        }, TestContext.Current.CancellationToken);

        var endpoint = kind == AiProviderKinds.OpenAiCompatible ? $"http://127.0.0.1:{port}/v1" : $"http://127.0.0.1:{port}";
        using var providers = new AiProviders([new AiProviderOptions("p", kind, new Uri(endpoint), "m", "sk-secret", Local: true)], NullLoggerFactory.Instance);

        var result = await providers.TestAsync("p", TestContext.Current.CancellationToken);

        listener.Stop();
        await server;
        Assert.False(result!.Ok);
        Assert.Equal(0, redirected);
    }

    [Fact]
    public void The_data_policy_decides_which_providers_an_organisation_may_use_local_ones_first()
    {
        AiProviderInfo[] providers = [new("claude", "anthropic", "m", false, null), new("ollama", "ollama", "m", true, "ollama"), new("vllm", "openai", "m", true, "llm")];
        Assert.Empty(AiDataPolicies.Allowed("none", providers));
        Assert.Equal(["ollama", "vllm"], AiDataPolicies.Allowed("local_only", providers).Select(p => p.Name));
        Assert.Equal(["ollama", "vllm"], AiDataPolicies.Allowed(null, providers).Select(p => p.Name));
        Assert.Equal(["ollama", "vllm", "claude"], AiDataPolicies.Allowed("cloud_allowed", providers).Select(p => p.Name));
        Assert.Empty(AiDataPolicies.Allowed("local_only", [providers[0]]));
        Assert.Empty(AiDataPolicies.Allowed("unknown", providers));
    }

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        return ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class FakeChat(Func<IEnumerable<ChatMessage>, CancellationToken, Task<ChatResponse>> answer) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            answer(messages, cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
