namespace Ardea.Platform.Settings;

/// <summary>Instance settings that the administration shows and several modules read.</summary>
public static class InstanceSettingDefinitions
{
    /// <summary><c>invite</c> (default after the first user, decision E12), <c>open</c> or <c>closed</c>.</summary>
    public static readonly SettingDefinition Registration = new("iam.registration", "REGISTRATION", Default: "invite");

    public static readonly IReadOnlyList<string> RegistrationModes = ["invite", "open", "closed"];
}
