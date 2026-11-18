using Ardea.Platform.Configuration;
using Microsoft.Extensions.Configuration;

namespace Ardea.Platform.Tests.Configuration;

public sealed class ConfigurationCheckTests
{
    [Fact]
    public void Every_missing_value_is_reported_with_its_variable_name()
    {
        var check = new ConfigurationCheck(Build([]))
            .Require("DB_APP_PASSWORD", "application role password")
            .Require("SMTP:HOST", "mail server");

        Assert.Collection(
            check.Errors,
            e => Assert.StartsWith("ARDEA_DB_APP_PASSWORD is required", e, StringComparison.Ordinal),
            e => Assert.StartsWith("ARDEA_SMTP__HOST is required", e, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("tooshort")]
    [InlineData("not base64 at all!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void Master_key_must_be_exactly_32_bytes(string value)
    {
        var check = new ConfigurationCheck(Build(new() { ["MASTER_KEY"] = value })).RequireBase64Key("MASTER_KEY", 32, "master key");

        Assert.Contains("must be 32 bytes", Assert.Single(check.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void A_32_byte_master_key_passes()
    {
        var key = Convert.ToBase64String(new byte[32]);
        var check = new ConfigurationCheck(Build(new() { ["MASTER_KEY"] = key })).RequireBase64Key("MASTER_KEY", 32, "master key");

        Assert.True(check.IsValid);
    }

    [Theory]
    [InlineData("ardea.example.com")]
    [InlineData("ftp://ardea.example.com")]
    public void Public_url_must_be_absolute_http(string value)
    {
        var check = new ConfigurationCheck(Build(new() { ["PUBLIC_URL"] = value })).RequireAbsoluteUrl("PUBLIC_URL", "public address");

        Assert.False(check.IsValid);
    }

    [Fact]
    public void Registration_mode_outside_the_list_is_rejected()
    {
        var check = new ConfigurationCheck(Build(new() { ["REGISTRATION"] = "everyone" })).OneOf("REGISTRATION", "invite", "open", "closed");

        Assert.Contains("invite, open, closed", Assert.Single(check.Errors), StringComparison.Ordinal);
    }

    private static IConfigurationRoot Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
