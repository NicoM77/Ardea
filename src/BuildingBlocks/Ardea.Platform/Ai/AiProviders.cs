using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace Ardea.Platform.Ai;

public static class AiProviderKinds
{
    public const string OpenAiCompatible = "openai";

    public const string Ollama = "ollama";

    public const string Anthropic = "anthropic";

    public static readonly string[] All = [OpenAiCompatible, Ollama, Anthropic];
}

public sealed record AiProviderOptions(string Name, string Kind, Uri? Endpoint, string Model, string? ApiKey, bool Local)
{
    public const string Section = "AI:PROVIDERS";

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

public interface IAiProviders
{
    IReadOnlyList<AiProviderInfo> All { get; }

    IChatClient? Client(string name);

    Task<AiConnectionResult?> TestAsync(string name, CancellationToken cancellationToken);
}

public static class AiDataPolicies
{
    public const string None = "none";
    public const string LocalOnly = "local_only";
    public const string CloudAllowed = "cloud_allowed";
    public const string Default = LocalOnly;

    public static readonly string[] All = [None, LocalOnly, CloudAllowed];

    public static IReadOnlyList<AiProviderInfo> Allowed(string? policy, IReadOnlyList<AiProviderInfo> providers) => (policy ?? Default) switch
    {
        LocalOnly => [.. providers.Where(p => p.Local)],
        CloudAllowed => [.. providers.Where(p => p.Local), .. providers.Where(p => !p.Local)],
        _ => [],
    };
}
