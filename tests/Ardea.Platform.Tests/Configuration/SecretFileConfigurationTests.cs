using System.Collections;
using Ardea.Platform.Configuration;
using Microsoft.Extensions.Configuration;

namespace Ardea.Platform.Tests.Configuration;

public sealed class SecretFileConfigurationTests : IDisposable
{
    private readonly string _file = Path.GetTempFileName();

    [Fact]
    public void File_variant_supplies_the_value_without_suffix_and_trims_it()
    {
        File.WriteAllText(_file, "s3cret\n");
        var configuration = Build(new Hashtable { ["ARDEA_DB_APP_PASSWORD_FILE"] = _file });

        Assert.Equal("s3cret", configuration["DB_APP_PASSWORD"]);
    }

    [Fact]
    public void Nested_keys_use_double_underscores()
    {
        File.WriteAllText(_file, "smtp-pass");
        var configuration = Build(new Hashtable { ["ARDEA_SMTP__PASSWORD_FILE"] = _file });

        Assert.Equal("smtp-pass", configuration["SMTP:PASSWORD"]);
    }

    [Fact]
    public void Directly_set_variable_wins_over_its_file_variant()
    {
        File.WriteAllText(_file, "from-file");
        var configuration = Build(new Hashtable
        {
            ["ARDEA_MASTER_KEY"] = "direct",
            ["ARDEA_MASTER_KEY_FILE"] = _file,
        });

        Assert.Null(configuration["MASTER_KEY"]);
    }

    [Fact]
    public void Missing_file_names_the_variable()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Build(new Hashtable { ["ARDEA_MASTER_KEY_FILE"] = "/does/not/exist" }));

        Assert.Contains("ARDEA_MASTER_KEY_FILE", error.Message, StringComparison.Ordinal);
    }

    public void Dispose() => File.Delete(_file);

    private static IConfigurationRoot Build(Hashtable environment) =>
        new ConfigurationBuilder().Add(new SecretFileConfigurationSource(ArdeaConfiguration.Prefix, () => environment)).Build();
}
