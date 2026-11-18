using Ardea.Host.Commands;

namespace Ardea.Host.Tests;

public sealed class InitEnvCommandTests
{
    private static readonly InitEnvCommand.InitialValues Values =
        new("owner-pw", "app-pw", Convert.ToBase64String(new byte[32]), "setup-token", "https://ardea.example.com");

    [Fact]
    public void App_environment_never_contains_the_owner_password()
    {
        var app = InitEnvCommand.Render.EnvApp(Values);

        Assert.DoesNotContain("owner-pw", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ARDEA_DB_OWNER_PASSWORD", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_files_carry_the_same_application_password()
    {
        Assert.Contains("ARDEA_DB_APP_PASSWORD=app-pw", InitEnvCommand.Render.Env(Values), StringComparison.Ordinal);
        Assert.Contains("ARDEA_DB_APP_PASSWORD=app-pw", InitEnvCommand.Render.EnvApp(Values), StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_secrets_are_safe_in_env_files_and_urls()
    {
        var secret = InitEnvCommand.Secret(24);

        Assert.Matches("^[A-Za-z0-9_-]{32}$", secret);
    }
}
