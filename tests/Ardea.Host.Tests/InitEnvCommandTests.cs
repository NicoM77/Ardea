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

    [Fact]
    public void A_folder_it_cannot_write_gives_a_hint_instead_of_a_crash()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.UserName == "root", "Needs a folder the current user cannot write.");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Directory.CreateTempSubdirectory("ardea-init-env-");
        File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var error = new StringWriter();
        var previous = Console.Error;
        Console.SetError(error);
        try
        {
            Assert.Equal(73, InitEnvCommand.Run(["--dir", directory.FullName]));
            Assert.Contains("--user", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(previous);
            File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            directory.Delete(recursive: true);
        }
    }
}
