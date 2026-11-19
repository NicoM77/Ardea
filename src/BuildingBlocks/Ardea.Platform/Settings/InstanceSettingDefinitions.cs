namespace Ardea.Platform.Settings;

/// <summary>Instance settings that the administration shows and several modules read.</summary>
public static class InstanceSettingDefinitions
{
    /// <summary><c>invite</c> (default after the first user, decision E12), <c>open</c> or <c>closed</c>.</summary>
    public static readonly SettingDefinition Registration = new("iam.registration", "REGISTRATION", Default: "invite");

    public static readonly IReadOnlyList<string> RegistrationModes = ["invite", "open", "closed"];

    /// <summary>
    /// Hosts a webhook may reach although they resolve to a private address (master plan 9.5): the instance
    /// administrator's release list, comma-separated. Loopback, link-local and metadata addresses stay closed.
    /// </summary>
    public static readonly SettingDefinition WebhookInternalHosts = new("notify.webhook_internal_hosts", "WEBHOOK_INTERNAL_HOSTS");

    public static IReadOnlyList<string> Hosts(string? value) =>
        [.. (value ?? string.Empty).Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(h => h.Trim('[', ']').ToLowerInvariant()).Distinct()];
}
