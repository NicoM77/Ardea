using System.Net;
using Ardea.Platform.Email;
using Ardea.TestSupport;
using Microsoft.AspNetCore.WebUtilities;

namespace Ardea.Notifications.Tests;

public sealed class AddressLinkTests(PostgresServer server)
{
    [Fact]
    public async Task A_confirmation_link_works_only_unchanged_as_the_latest_before_it_expires_and_only_to_confirm()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        var path = $"{s.Path}/external-addresses";
        var address = (await NotifyTestHost.JsonAsync(await s.Owner.PostAsync(path, new { address = "oncall@partner.example" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        Assert.Empty(await host.RunJobsAsync());
        var first = Token(Assert.Single(host.Mail!.Sent));
        using var stranger = host.Client();

        async Task<HttpStatusCode> LinkAsync(string action, string? token) =>
            (await stranger.PostAsync($"/api/v1/notify/addresses/{action}", new { token }, csrf: false)).StatusCode;

        var parts = first.Split('.');
        var changed = parts[3][0] == 'A' ? 'B' + parts[3][1..] : 'A' + parts[3][1..];
        foreach (var token in new[] { $"c.{parts[1]}.{parts[2]}.{changed}", $"c.{Guid.CreateVersion7():N}.{parts[2]}.{parts[3]}", first + "A", null, "  ", new string('c', 1001) })
        {
            Assert.Equal(HttpStatusCode.NotFound, await LinkAsync("confirm", token));
        }

        Assert.Equal(HttpStatusCode.NotFound, await LinkAsync("unsubscribe", first));
        Assert.Equal(HttpStatusCode.NotFound, await LinkAsync("resubscribe", first));
        Assert.Equal(HttpStatusCode.OK, await LinkAsync("preview", first));

        host.Time.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(HttpStatusCode.OK, (await s.Owner.PostAsync($"{path}/{address}/resend")).StatusCode);
        Assert.Empty(await host.RunJobsAsync());
        var second = Token(host.Mail.Sent[^1]);
        Assert.Equal(HttpStatusCode.NotFound, await LinkAsync("preview", first));
        Assert.Equal(HttpStatusCode.NotFound, await LinkAsync("confirm", first));

        await host.ExecuteAsync($"UPDATE notify.external_address SET token_expires_at = '{host.Time.GetUtcNow().AddSeconds(-1):O}' WHERE id = '{address}'");
        Assert.Equal(HttpStatusCode.NotFound, await LinkAsync("preview", second));
        Assert.Equal(HttpStatusCode.NotFound, await LinkAsync("confirm", second));
        Assert.Equal("expired", (await NotifyTestHost.JsonAsync(await s.Owner.GetAsync(path)))[0].GetProperty("state").GetString());

        host.Time.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(HttpStatusCode.OK, (await s.Owner.PostAsync($"{path}/{address}/resend")).StatusCode);
        Assert.Empty(await host.RunJobsAsync());
        Assert.Equal(HttpStatusCode.NoContent, await LinkAsync("confirm", Token(host.Mail.Sent[^1])));
        Assert.Equal(HttpStatusCode.Conflict, (await s.Owner.PostAsync($"{path}/{address}/resend")).StatusCode);
        Assert.Equal(3, host.Mail.Sent.Count);

        var spare = (await NotifyTestHost.JsonAsync(await s.Owner.PostAsync(path, new { address = "spare@partner.example" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.SendAsync(HttpMethod.Delete, $"{path}/{spare}")).StatusCode);
        Assert.Empty(await host.RunJobsAsync());
        Assert.Equal(3, host.Mail.Sent.Count);
        await s.RuleAsync("Partner", 4, ["action.raised"], Scenario.Target("email", kind: "address", reference: address.ToString()));
        Assert.Equal(HttpStatusCode.Conflict, (await s.Owner.SendAsync(HttpMethod.Delete, $"{path}/{address}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Owner.SendAsync(HttpMethod.Delete, $"{path}/{spare}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Owner.PostAsync($"{path}/{spare}/resend")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Owner.PostAsync(path, new { address = "ONCALL@partner.example" })).StatusCode);
        foreach (var invalid in new[] { "", "not an address", "Mia <mia@partner.example>", new string('a', 310) + "@example.org" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync(path, new { address = invalid })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, await LinkAsync("preview", "garbage"));
        Assert.Equal(3, host.Mail.Sent.Count);
    }

    private static string Token(EmailMessage message)
    {
        var start = message.Text.IndexOf("https://ardea.test/notifications/confirm", StringComparison.Ordinal);
        var end = message.Text.IndexOf('\n', start);
        return QueryHelpers.ParseQuery(new Uri(message.Text[start..end].Trim()).Fragment.TrimStart('#'))["token"].ToString();
    }
}
