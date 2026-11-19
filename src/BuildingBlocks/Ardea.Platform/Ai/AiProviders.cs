using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace Ardea.Platform.Ai;

/// <summary>The kinds of language model providers in the MVP (master plan 7.8, decision E6).</summary>
public static class AiProviderKinds
{
    /// <summary>Any endpoint that speaks the OpenAI chat API: vLLM, llama.cpp, LM Studio, hosted services, Ollama's <c>/v1</c>.</summary>
    public const string OpenAiCompatible = "openai";

    /// <summary>Ollama's own API, which enforces JSON schemas through a grammar.</summary>
    public const string Ollama = "ollama";

    public const string Anthropic = "anthropic";

    public static readonly string[] All = [OpenAiCompatible, Ollama, Anthropic];
}

/// <summary>
/// One named provider as the instance configures it (master plan 7.8, 12.4):
/// <c>ARDEA_AI__PROVIDERS__&lt;Name&gt;__KIND</c>, <c>__ENDPOINT</c>, <c>__MODEL</c>, <c>__API_KEY</c>, <c>__LOCAL</c>.
/// </summary>
/// <param name="Name">The name, as written in the configuration key.</param>
/// <param name="Kind">One of <see cref="AiProviderKinds"/>.</param>
/// <param name="Endpoint">The base address; required for <c>openai</c> and <c>ollama</c>, optional for <c>anthropic</c>.</param>
/// <param name="Model">The model to ask.</param>
/// <param name="ApiKey">The key, if the provider needs one. Never shown, logged or returned.</param>
/// <param name="Local">Whether the provider counts as local: data sent to it stays under the operator's control.
/// Organisations with the policy «local only» are sent only to local providers (master plan 7.8).</param>
public sealed record AiProviderOptions(string Name, string Kind, Uri? Endpoint, string Model, string? ApiKey, bool Local)
{
    public const string Section = "AI:PROVIDERS";

    /// <summary>Reads every configured provider; <paramref name="errors"/> names what is wrong, without values.</summary>
    public static IReadOnlyList<AiProviderOptions> FromConfiguration(IConfiguration configuration, out IReadOnlyList<string> errors)
    {
        var providers = new List<AiProviderOptions>();
        var problems = new List<string>();
        foreach (var section in configuration.GetSection(Section).GetChildren().OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var name = section.Key;
            var prefix = $"ARDEA_AI__PROVIDERS__{name}__";
            var kind = section["KIND"]?.Trim().ToLowerInvariant();
            var model = section["MODEL"]?.Trim();
            var endpointText = section["ENDPOINT"]?.Trim();
            var apiKey = section["API_KEY"] is { Length: > 0 } key ? key : null;
            var localText = section["LOCAL"]?.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$"))
            {
                problems.Add($"{prefix}: the provider name may use letters, digits, '-' and '_' (at most 40).");
                continue;
            }

            if (kind is null || !AiProviderKinds.All.Contains(kind))
            {
                problems.Add($"{prefix}KIND must be one of {string.Join(", ", AiProviderKinds.All)}.");
            }

            if (string.IsNullOrEmpty(model))
            {
                problems.Add($"{prefix}MODEL is required.");
            }

            Uri? endpoint = null;
            if (endpointText is { Length: > 0 })
            {
                if (!Uri.TryCreate(endpointText, UriKind.Absolute, out endpoint) || endpoint.Scheme is not ("http" or "https"))
                {
                    problems.Add($"{prefix}ENDPOINT must be an absolute http or https address.");
                }
            }
            else if (kind is AiProviderKinds.OpenAiCompatible or AiProviderKinds.Ollama)
            {
                problems.Add($"{prefix}ENDPOINT is required for {kind}.");
            }

            if (kind == AiProviderKinds.Anthropic && apiKey is null)
            {
                problems.Add($"{prefix}API_KEY is required for anthropic.");
            }

            bool local;
            if (localText is null or "")
            {
                // Without a statement, only Ollama counts as local: it runs where the operator runs it.
                local = kind == AiProviderKinds.Ollama;
            }
            else if (!bool.TryParse(localText, out local))
            {
                problems.Add($"{prefix}LOCAL must be true or false.");
            }

            if (kind == AiProviderKinds.Anthropic && local)
            {
                problems.Add($"{prefix}LOCAL cannot be true for anthropic: it is a hosted service.");
            }

            providers.Add(new AiProviderOptions(name, kind ?? string.Empty, endpoint, model ?? string.Empty, apiKey, local));
        }

        errors = problems;
        return problems.Count == 0 ? providers : [];
    }
}

/// <summary>What the administration shows of a provider: never its key, and of the endpoint only the host.</summary>
public sealed record AiProviderInfo(string Name, string Kind, string Model, bool Local, string? Host);

/// <summary>The outcome of a connection test (master plan 14, task 4.1).</summary>
/// <param name="Ok">The provider answered.</param>
/// <param name="DurationMs">How long the answer took.</param>
/// <param name="Error">What went wrong, in words safe to show an administrator: no key, no prompt.</param>
public sealed record AiConnectionResult(bool Ok, long DurationMs, string? Error);

/// <summary>
/// The configured language model providers (master plan 7.8). Modules ask for a chat client by provider name
/// and never know which SDK answers. Without providers the list is empty and the system runs on rules and
/// templates alone.
/// </summary>
public interface IAiProviders
{
    IReadOnlyList<AiProviderInfo> All { get; }

    /// <summary>The chat client of a provider, or <c>null</c> when there is no such provider.</summary>
    IChatClient? Client(string name);

    /// <summary>Asks the provider for a one-word answer, with a time limit.</summary>
    Task<AiConnectionResult?> TestAsync(string name, CancellationToken cancellationToken);
}

/// <summary>
/// An organisation's data policy for language models (master plan 7.8, 9.4): <c>none</c> sends nothing,
/// <c>local_only</c> only to providers marked local, <c>cloud_allowed</c> to any. The default is
/// <c>local_only</c>: nothing leaves the operator's control until an administrator of the organisation allows it.
/// </summary>
public static class AiDataPolicies
{
    public const string None = "none";
    public const string LocalOnly = "local_only";
    public const string CloudAllowed = "cloud_allowed";
    public const string Default = LocalOnly;

    public static readonly string[] All = [None, LocalOnly, CloudAllowed];

    /// <summary>
    /// The providers an organisation's requests may go to, in the order to try them: local providers first,
    /// then, only under <c>cloud_allowed</c>, the others. A provider outside this list is never used, not even
    /// when every listed one fails (phase 4 acceptance).
    /// </summary>
    public static IReadOnlyList<AiProviderInfo> Allowed(string? policy, IReadOnlyList<AiProviderInfo> providers) => (policy ?? Default) switch
    {
        LocalOnly => [.. providers.Where(p => p.Local)],
        CloudAllowed => [.. providers.Where(p => p.Local), .. providers.Where(p => !p.Local)],
        _ => [],
    };
}
