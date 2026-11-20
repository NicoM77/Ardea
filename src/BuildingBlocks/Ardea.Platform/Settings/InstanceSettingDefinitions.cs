namespace Ardea.Platform.Settings;

public static class InstanceSettingDefinitions
{
    public static readonly SettingDefinition Registration = new("iam.registration", "REGISTRATION", Default: "invite");

    public static readonly IReadOnlyList<string> RegistrationModes = ["invite", "open", "closed"];

    public static readonly SettingDefinition WebhookInternalHosts = new("notify.webhook_internal_hosts", "WEBHOOK_INTERNAL_HOSTS");

    public static IReadOnlyList<string> Hosts(string? value) =>
        [.. (value ?? string.Empty).Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(h => h.Trim('[', ']').ToLowerInvariant()).Distinct()];
}
