namespace Ardea.Host.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("1.4.2+3f2a9c1", "1.4.2")]
    [InlineData("0.2.0-rc.1+abc", "0.2.0-rc.1")]
    public void A_release_build_reports_the_version_it_was_built_with(string informational, string expected)
    {
        Assert.Equal(expected, AppVersion.FromInformationalVersion(informational));
    }

    [Theory]
    [InlineData("0.0.0")]
    [InlineData("0.0.0+3f2a9c1")]
    [InlineData("")]
    [InlineData(null)]
    public void A_build_without_a_release_version_reports_dev(string? informational)
    {
        Assert.Equal(AppVersion.Development, AppVersion.FromInformationalVersion(informational));
    }

    [Fact]
    public void A_local_build_of_the_repository_reports_dev()
    {
        Assert.Equal("dev", AppVersion.Current);
    }
}
