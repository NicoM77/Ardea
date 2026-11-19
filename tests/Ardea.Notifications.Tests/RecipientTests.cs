using System.Net;
using Ardea.Platform.Email;
using Ardea.TestSupport;
using Microsoft.AspNetCore.WebUtilities;

namespace Ardea.Notifications.Tests;

/// <summary>
/// Recipients (master plan 8.2, 8.3; tasks 5.1, 5.7): the default rules of a new organisation, external
/// addresses that receive nothing until confirmed and unsubscribe without signing in, the personal mute with
/// its exception for level 5, and the assignee who hears about an assignment.
/// </summary>
public sealed class RecipientTests(PostgresServer server)
{
    [Fact]
    public async Task A_new_organisation_starts_with_the_three_default_rules()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var (owner, organization) = await host.OwnerAsync();
        using var _ = owner;

        var rules = (await NotifyTestHost.JsonAsync(await owner.GetAsync($"/api/v1/orgs/{organization}/rules"))).EnumerateArray().ToList();
        Assert.Equal(["critical|5", "high|4", "weekly|2"], rules.Select(r => $"{r.GetProperty("preset").GetString()}|{r.GetProperty("minLevel").GetInt32()}"));
        Assert.Equal(
            ["in_app role admin immediate", "email role admin immediate", "push role admin immediate"],
            rules[0].GetProperty("targets").EnumerateArray().Select(Describe));
        Assert.Equal(
            ["in_app role viewer immediate", "email role viewer digest 0 8 * * 1-5"],
            rules[1].GetProperty("targets").EnumerateArray().Select(Describe));
        var weekly = rules[2].GetProperty("targets")[0];
        Assert.Equal("email role viewer digest 0 8 * * 1", Describe(weekly));
        Assert.True(weekly.GetProperty("sendEmpty").GetBoolean());
        Assert.Contains("warning.published", rules[2].GetProperty("events").EnumerateArray().Select(e => e.GetString()));

        // Monday 08:00 in the organisation's time zone (Europe/Zurich by default).
        var zurich = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich");
        var next = TimeZoneInfo.ConvertTime(weekly.GetProperty("nextDigestAt").GetDateTimeOffset(), zurich);
        Assert.Equal((DayOfWeek.Monday, 8, 0), (next.DayOfWeek, next.Hour, next.Minute));
        Assert.Empty(rules[0].GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task An_external_address_receives_nothing_until_confirmed_and_unsubscribes_with_the_signed_link_without_signing_in()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        var added = await NotifyTestHost.JsonAsync(await s.Owner.PostAsync($"{s.Path}/external-addresses", new { address = "oncall@partner.example" }), HttpStatusCode.Created);
        Assert.Equal("pending", added.GetProperty("state").GetString());
        var address = added.GetProperty("id").GetGuid();
        await s.RuleAsync("Partner", 4, ["action.raised"], Scenario.Target("email", kind: "address", reference: address.ToString()));
        Assert.Empty(await host.RunJobsAsync());
        var confirmation = Assert.Single(host.Mail!.Sent);
        Assert.Equal("oncall@partner.example", confirmation.To);
        // The link carries the token in its fragment, which the browser never sends: no request log sees it.
        var token = QueryHelpers.ParseQuery(Link(confirmation, "/notifications/confirm").Fragment.TrimStart('#'))["token"].ToString();

        var first = await s.ActionAsync(2, level: 4);
        await s.EventAsync("action.raised", first);
        await s.EvaluateAsync();
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.delivery"));

        // No session, no CSRF token: the link is the proof.
        using var stranger = host.Client();
        var preview = await NotifyTestHost.JsonAsync(await stranger.PostAsync("/api/v1/notify/addresses/preview", new { token }, csrf: false));
        Assert.Equal("Acme", preview.GetProperty("organizationName").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await stranger.PostAsync("/api/v1/notify/addresses/confirm", new { token }, csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync("/api/v1/notify/addresses/confirm", new { token }, csrf: false)).StatusCode);

        var second = await s.ActionAsync(3, level: 5);
        await s.EventAsync("action.raised", second);
        await s.EvaluateAsync();
        await s.DeliverAsync();
        var message = host.Mail.Sent[^1];
        Assert.Equal("oncall@partner.example", message.To);
        Assert.Equal("List-Unsubscribe=One-Click", message.Headers!["List-Unsubscribe-Post"]);
        var oneClick = new Uri(message.Headers["List-Unsubscribe"].Trim('<', '>'));
        Assert.Contains("https://ardea.test/notifications/unsubscribe#token=", message.Text, StringComparison.Ordinal);

        // A mail client posts the one-click unsubscribe from its own servers (RFC 8058): a form body, no cookie, no CSRF token.
        using var mailClient = host.RawClient();
        using var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("List-Unsubscribe", "One-Click")]);
        Assert.Equal(HttpStatusCode.NoContent, (await mailClient.PostAsync(oneClick, form, TestContext.Current.CancellationToken)).StatusCode);
        var third = await s.ActionAsync(4, level: 5);
        await s.EventAsync("action.raised", third);
        await s.EvaluateAsync();
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.delivery"));
        Assert.Equal("unsubscribed", (await NotifyTestHost.JsonAsync(await s.Owner.GetAsync($"{s.Path}/external-addresses")))[0].GetProperty("state").GetString());

        // A forged or foreign token finds nothing.
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync("/api/v1/notify/addresses/unsubscribe/one-click?token=forged", csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync("/api/v1/notify/addresses/unsubscribe", new { token = "forged" }, csrf: false)).StatusCode);

        // The organisation cannot ask again, nor remove the address to add it anew; only the recipient can take it back.
        Assert.Equal("address_unsubscribed", await CodeAsync(await s.Owner.PostAsync($"{s.Path}/external-addresses/{address}/resend")));
        Assert.Equal("address_unsubscribed", await CodeAsync(await s.Owner.SendAsync(HttpMethod.Delete, $"{s.Path}/external-addresses/{address}")));
        var signed = QueryHelpers.ParseQuery(oneClick.Query)["token"].ToString();
        Assert.Equal(HttpStatusCode.NoContent, (await stranger.PostAsync("/api/v1/notify/addresses/resubscribe", new { token = signed }, csrf: false)).StatusCode);
        Assert.Equal("confirmed", (await NotifyTestHost.JsonAsync(await s.Owner.GetAsync($"{s.Path}/external-addresses")))[0].GetProperty("state").GetString());
    }

    [Fact]
    public async Task Confirmation_mails_have_a_cooldown_a_limit_per_address_and_organisation_and_a_cap_on_addresses()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        var path = $"{s.Path}/external-addresses";
        var address = (await NotifyTestHost.JsonAsync(await s.Owner.PostAsync(path, new { address = "victim@example.net" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        Assert.Equal("confirmation_limit", await CodeAsync(await s.Owner.PostAsync($"{path}/{address}/resend")));
        host.Time.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(HttpStatusCode.OK, (await s.Owner.PostAsync($"{path}/{address}/resend")).StatusCode);
        host.Time.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(HttpStatusCode.OK, (await s.Owner.PostAsync($"{path}/{address}/resend")).StatusCode);
        host.Time.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal("confirmation_limit", await CodeAsync(await s.Owner.PostAsync($"{path}/{address}/resend")));

        // Over the instance: an address asked by three organisations today is not asked by a fourth.
        await host.ExecuteAsync($"""
            INSERT INTO notify.external_address (organization_id, address, normalized_address, confirmation_sent_at, confirmations_sent, created_at)
            SELECT o.id, 'target@example.net', 'TARGET@EXAMPLE.NET', now(), 1, now() FROM (VALUES (gen_random_uuid()), (gen_random_uuid()), (gen_random_uuid())) AS o(id)
            """);
        Assert.Equal("confirmation_limit", await CodeAsync(await s.Owner.PostAsync(path, new { address = "target@example.net" })));

        // Per organisation and day, and in all.
        await host.ExecuteAsync($"""
            INSERT INTO notify.external_address (organization_id, address, normalized_address, confirmation_sent_at, confirmations_sent, created_at)
            SELECT '{s.Organization}', 'bulk' || i || '@example.net', 'BULK' || i || '@EXAMPLE.NET', '{host.Time.GetUtcNow():O}', 1, now() FROM generate_series(1, 19) AS i
            """);
        Assert.Equal("confirmation_limit", await CodeAsync(await s.Owner.PostAsync(path, new { address = "one-more@example.net" })));
        await host.ExecuteAsync($"""
            INSERT INTO notify.external_address (organization_id, address, normalized_address, created_at)
            SELECT '{s.Organization}', 'old' || i || '@example.net', 'OLD' || i || '@EXAMPLE.NET', now() FROM generate_series(1, 30) AS i
            """);
        Assert.Equal("address_limit", await CodeAsync(await s.Owner.PostAsync(path, new { address = "fifty-first@example.net" })));
    }

    [Fact]
    public async Task A_muted_channel_stays_silent_except_for_level_5_unless_the_organisation_allows_muting_it()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("Everything", 2, ["action.raised"], Scenario.Target("in_app"), Scenario.Target("email"));
        var muted = await NotifyTestHost.JsonAsync(await s.Owner.PutAsync($"{s.Path}/notification-preferences", new { mutedChannels = new[] { "email" } }));
        Assert.False(muted.GetProperty("canMuteCritical").GetBoolean());

        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 4));
        await s.EventAsync("action.raised", await s.ActionAsync(3, level: 5));
        await s.EvaluateAsync();
        Assert.Equal(2L, await s.CountAsync("SELECT count(*) FROM notify.notification"));
        Assert.Equal(["5"], await host.RowsAsync("SELECT level FROM notify.delivery_event WHERE channel = 'email'"));

        Assert.Equal(HttpStatusCode.OK, (await s.Owner.SendAsync(HttpMethod.Patch, s.Path, new { allowMuteCritical = true })).StatusCode);
        await s.EventAsync("action.raised", await s.ActionAsync(4, level: 5));
        await s.EvaluateAsync();
        Assert.Equal(3L, await s.CountAsync("SELECT count(*) FROM notify.notification"));
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.delivery_event WHERE channel = 'email'"));
    }

    [Fact]
    public async Task A_status_change_written_through_the_api_reaches_its_rule_through_the_queue_in_its_transaction()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        using var member = await host.RegisterAsync("member@example.org", "Mia Member");
        await host.AddMemberAsync(s.Organization, "member@example.org", "member");
        var memberId = await host.ScalarAsync<Guid>("SELECT id FROM iam.\"user\" WHERE normalized_email = 'MEMBER@EXAMPLE.ORG'");
        await s.ActionAsync(2, level: 3);

        // A reminder has no assignee (master plan 17.5): the team's work reaches people only through a rule that names it,
        // and in the app only (master plan 8.2).
        var rule = new
        {
            name = "Team work",
            minLevel = 2,
            events = new[] { "action.status_changed" },
            projectIds = Array.Empty<Guid>(),
            targets = new[] { new { channel = "in_app", recipientKind = "user", recipientRef = memberId.ToString(), mode = "immediate" } },
        };
        Assert.Equal(HttpStatusCode.Created, (await s.Owner.PostAsync($"{s.Path}/rules", rule)).StatusCode);

        // The API writes the history row; the hand-off queues it for the rules in the same transaction (ADR 038).
        var changed = await s.Owner.PostAsync($"{s.Path}/actions/2/status", new { status = "risk_accepted", expectedStatus = "open" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(["action.status_changed"], await host.RowsAsync("SELECT kind FROM notify.pending_event"));
        Assert.Equal(1L, await s.CountAsync($"SELECT count(*) FROM platform.job WHERE kind = 'notify.evaluate' AND state = 'pending' AND org_id = '{s.Organization}'"));

        Assert.Empty(await host.RunJobsAsync());
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.pending_event"));
        Assert.Equal("Status: risk accepted.", await host.ScalarAsync<string>($"SELECT body FROM notify.notification WHERE user_id = '{memberId}'"));
        await s.DeliverAsync();
        Assert.Empty(Notifications(host));
    }

    /// <summary>The notification e-mails, without the account e-mails of registration.</summary>
    private static List<EmailMessage> Notifications(NotifyTestHost host) => [.. host.Mail!.Sent.Where(m => m.Subject.StartsWith('['))];

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    private static string Describe(System.Text.Json.JsonElement target) =>
        string.Join(' ', new[] { "channel", "recipientKind", "recipientRef", "mode", "digestCron" }
            .Select(p => target.GetProperty(p).GetString()).Where(v => v is not null));

    private static Uri Link(EmailMessage message, string path)
    {
        var start = message.Text.IndexOf("https://ardea.test" + path, StringComparison.Ordinal);
        var end = message.Text.IndexOf('\n', start);
        return new Uri(message.Text[start..end]);
    }
}
