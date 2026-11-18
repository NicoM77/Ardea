using Microsoft.Extensions.Configuration;

namespace Ardea.Platform.Configuration;

/// <summary>
/// Collects configuration errors so a process can stop at start with a message that names every
/// offending variable (master plan 3.5, 12.4).
/// </summary>
public sealed class ConfigurationCheck(IConfiguration configuration)
{
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Errors => _errors;

    public bool IsValid => _errors.Count == 0;

    public ConfigurationCheck Require(string key, string meaning)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
        {
            _errors.Add($"{ArdeaConfiguration.VariableName(key)} is required: {meaning}.");
        }

        return this;
    }

    public ConfigurationCheck RequireBase64Key(string key, int bytes, string meaning)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            _errors.Add($"{ArdeaConfiguration.VariableName(key)} is required: {meaning}.");
            return this;
        }

        var buffer = new byte[value.Length];
        if (!Convert.TryFromBase64String(value, buffer, out var written) || written != bytes)
        {
            _errors.Add($"{ArdeaConfiguration.VariableName(key)} must be {bytes} bytes encoded as Base64.");
        }

        return this;
    }

    public ConfigurationCheck RequireAbsoluteUrl(string key, string meaning)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            _errors.Add($"{ArdeaConfiguration.VariableName(key)} is required: {meaning}.");
        }
        else if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            _errors.Add($"{ArdeaConfiguration.VariableName(key)} must be an absolute http(s) URL, got '{value}'.");
        }

        return this;
    }

    public ConfigurationCheck OneOf(string key, params string[] allowed)
    {
        var value = configuration[key];
        if (!string.IsNullOrWhiteSpace(value) && !allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            _errors.Add($"{ArdeaConfiguration.VariableName(key)} must be one of {string.Join(", ", allowed)}, got '{value}'.");
        }

        return this;
    }

    public ConfigurationCheck PositiveInt(string key)
    {
        var value = configuration[key];
        if (!string.IsNullOrWhiteSpace(value) && (!int.TryParse(value, out var number) || number <= 0))
        {
            _errors.Add($"{ArdeaConfiguration.VariableName(key)} must be a positive whole number, got '{value}'.");
        }

        return this;
    }
}
