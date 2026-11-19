using System.Text.Json;
using Ardea.Iam.Contracts;
using Ardea.Platform.Ai;
using Microsoft.Extensions.AI;

namespace Ardea.Assessment.Tests.Estimating;

/// <summary>
/// A language model that answers with recorded answers and keeps every request it receives (master plan 7.9: the
/// integration is tested with recorded answers, never with a real model). The answer may depend on the request's
/// data block, e.g. on the entry key.
/// </summary>
internal sealed class RecordedModel(Func<RecordedRequest, string> answer, int tokensIn = 1800, int tokensOut = 400) : IChatClient
{
    private readonly List<RecordedRequest> _requests = [];

    /// <summary>A model that is down: every call fails like an unreachable endpoint.</summary>
    public static RecordedModel Unreachable() => new(_ => throw new HttpRequestException("connection refused"));

    public static RecordedModel Always(string answer, int tokensIn = 1800, int tokensOut = 400) => new(_ => answer, tokensIn, tokensOut);

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var request = new RecordedRequest([.. messages], options);
        lock (_requests)
        {
            _requests.Add(request);
        }

        var text = answer(request);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            Usage = new UsageDetails { InputTokenCount = tokensIn, OutputTokenCount = tokensOut },
        });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>One request as the provider received it.</summary>
internal sealed record RecordedRequest(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)
{
    /// <summary>Everything the provider saw as text.</summary>
    public string Text => string.Join("\n", Messages.Select(m => m.Text));

    /// <summary>The data block of the user message, parsed.</summary>
    public JsonElement Data
    {
        get
        {
            var user = Messages.Last(m => m.Role == ChatRole.User).Text;
            var start = user.IndexOf("<source_data>", StringComparison.Ordinal) + "<source_data>".Length;
            var end = user.LastIndexOf("</source_data>", StringComparison.Ordinal);
            return JsonDocument.Parse(user[start..end]).RootElement.Clone();
        }
    }

    public string EntryKey => Data.GetProperty("entry").GetProperty("key").GetString()!;
}

/// <summary>The configured providers of a test: name, whether local, and the recorded model behind each.</summary>
internal sealed class RecordedProviders(params (string Name, bool Local, RecordedModel Model)[] providers) : IAiProviders
{
    public IReadOnlyList<AiProviderInfo> All { get; } = [.. providers.Select(p => new AiProviderInfo(p.Name, p.Local ? "ollama" : "anthropic", p.Name + "-model", p.Local, null))];

    public IChatClient? Client(string name) => providers.FirstOrDefault(p => p.Name == name).Model;

    public Task<AiConnectionResult?> TestAsync(string name, CancellationToken cancellationToken) => Task.FromResult<AiConnectionResult?>(null);
}

/// <summary>Organisations live in Iam, which the test hosts leave out: language and data policy per organisation.</summary>
internal sealed class TestOrganizations : IOrganizationProfiles
{
    private readonly Dictionary<Guid, OrganizationProfile> _profiles = [];

    public string DefaultPolicy { get; set; } = AiDataPolicies.LocalOnly;

    public void Set(Guid organization, string policy, string locale = "de")
    {
        lock (_profiles)
        {
            _profiles[organization] = new OrganizationProfile(organization, locale, policy);
        }
    }

    public Task<OrganizationProfile?> FindAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        lock (_profiles)
        {
            return Task.FromResult<OrganizationProfile?>(_profiles.GetValueOrDefault(organizationId) ?? new OrganizationProfile(organizationId, "de", DefaultPolicy));
        }
    }
}

/// <summary>Recorded answers in <c>Estimating/Recorded</c>; <c>{C}</c> stands for the verdict list a test fills in.</summary>
internal static class Recorded
{
    public static string Load(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Estimating", "Recorded", name));

    /// <summary>A recorded answer with one verdict per component reference of the request.</summary>
    public static string For(string name, RecordedRequest request, string verdict)
    {
        var verdicts = request.Data.GetProperty("components").EnumerateArray()
            .Select(c => $$"""{"component": "{{c.GetProperty("ref").GetString()}}", "verdict": "{{verdict}}"}""");
        return Load(name).Replace("\"{VERDICTS}\"", "[" + string.Join(", ", verdicts) + "]", StringComparison.Ordinal);
    }
}
