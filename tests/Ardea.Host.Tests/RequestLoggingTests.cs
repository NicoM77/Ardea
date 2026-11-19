using Microsoft.Extensions.Logging;

namespace Ardea.Host.Tests;

/// <summary>
/// Request lines name the query string, and some links carry a token there (the RFC 8058 one-click address of an
/// e-mail, ADR 041): no role writes them, whatever the endpoint.
/// </summary>
public sealed class RequestLoggingTests
{
    [Fact]
    public void Request_lines_that_name_the_query_string_are_not_logged()
    {
        using var loggers = LoggerFactory.Create(HostConfiguration.AddJsonLogging);

        Assert.False(loggers.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics").IsEnabled(LogLevel.Information));
        Assert.True(loggers.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics").IsEnabled(LogLevel.Warning));
        Assert.True(loggers.CreateLogger("Ardea.Notifications").IsEnabled(LogLevel.Information));
    }
}
