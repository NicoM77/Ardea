using Microsoft.Extensions.Logging;

namespace Ardea.Host.Tests;

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
