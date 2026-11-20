using System.Net.Http.Json;
using System.Text.Json;
using Ardea.Platform.Email;
using Ardea.Platform.Secrets;
using Ardea.Platform.Tests.Jobs;
using Ardea.TestSupport;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Tests.Secrets;

public sealed class SmtpEmailSenderTests(PostgresServer server)
{
    [Fact]
    public async Task Message_reaches_the_mail_server_with_text_and_html()
    {
        await using var mailpit = new ContainerBuilder("axllent/mailpit:v1.27")
            .WithPortBinding(1025, true)
            .WithPortBinding(8025, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPath("/readyz").ForPort(8025)))
            .Build();
        await mailpit.StartAsync();
        var environment = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SMTP:HOST"] = mailpit.Hostname,
            ["SMTP:PORT"] = mailpit.GetMappedPublicPort(1025).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SMTP:FROM"] = "ardea@example.org",
            ["SMTP:SECURITY"] = "none",
        }).Build();
        await using var host = await JobTestHost.CreateAsync(server, s => s.AddLogging().AddArdeaSecrets(new MasterKey(new byte[32])).AddSingleton<IConfiguration>(environment));

        await using var scope = host.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        Assert.True(await sender.IsConfiguredAsync(CancellationToken.None));
        await sender.SendAsync(
            EmailTemplate.Render("person@example.org", "Test <subject>", "Hello <b>Person</b>,", ["First paragraph."], "Open", new Uri("https://ardea.test/x?a=1&b=2"), "Footer"),
            CancellationToken.None);

        using var http = new HttpClient { BaseAddress = new Uri($"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(8025)}") };
        var messages = await http.GetFromJsonAsync<JsonElement>("/api/v1/messages");
        var message = messages.GetProperty("messages")[0];
        Assert.Equal("Test <subject>", message.GetProperty("Subject").GetString());
        var id = message.GetProperty("ID").GetString();
        var full = await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{id}");
        Assert.Contains("Hello &lt;b&gt;Person&lt;/b&gt;", full.GetProperty("HTML").GetString(), StringComparison.Ordinal);
        Assert.Contains("https://ardea.test/x?a=1&b=2", full.GetProperty("Text").GetString(), StringComparison.Ordinal);
    }
}
