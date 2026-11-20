using System.Net;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Notifications.Tests;

public sealed class RuleApiTests(PostgresServer server)
{
    [Fact]
    public async Task A_rule_names_only_what_its_organisation_has_and_its_channel_reaches()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        using var bob = await host.RegisterAsync("bob@example.org", "Bob");
        var globex = await NotifyTestHost.OrganizationAsync(bob, "Globex");
        var (foreignProject, _) = await host.ProjectAsync(bob, globex, "Globex Ops");
        var bobId = await host.ScalarAsync<Guid>("SELECT id FROM iam.\"user\" WHERE normalized_email = 'BOB@EXAMPLE.ORG'");
        var foreignRule = (await NotifyTestHost.JsonAsync(await bob.GetAsync($"/api/v1/orgs/{globex}/rules")))[0].GetProperty("id").GetGuid();
        var rules = $"{s.Path}/rules";

        static object Rule(object target, string name = "Rule", int minLevel = 4, string[]? events = null, Guid[]? projectIds = null) =>
            new { name, minLevel, events = events ?? ["action.raised"], projectIds = projectIds ?? [], targets = new[] { target } };

        var inApp = Scenario.Target("in_app");
        foreach (var (body, field) in new (object Body, string Field)[]
        {
            (Rule(inApp, name: " "), "name"),
            (Rule(inApp, name: new string('x', 201)), "name"),
            (Rule(inApp, minLevel: 1), "minLevel"),
            (Rule(inApp, events: ["action.raised", "action.closed"]), "events"),
            (Rule(inApp, events: []), "events"),
            (Rule(inApp, projectIds: [foreignProject]), "projectIds"),
            (new { name = "Nobody", minLevel = 4, events = new[] { "action.raised" }, targets = Array.Empty<object>() }, "targets"),
            (Rule(Scenario.Target("sms")), "targets[0]"),
            (Rule(Scenario.Target("push", "digest", cron: "0 8 * * *")), "targets[0]"),
            (Rule(Scenario.Target("in_app", kind: "address", reference: Guid.CreateVersion7().ToString())), "targets[0]"),
            (Rule(Scenario.Target("webhook", kind: "role")), "targets[0]"),
            (Rule(Scenario.Target("email", kind: "user", reference: bobId.ToString())), "targets[0]"),
            (Rule(Scenario.Target("email", kind: "role", reference: "superuser")), "targets[0]"),
            (Rule(Scenario.Target("email", "digest", cron: "61 8 * * *")), "targets[0]"),
            (Rule(Scenario.Target("email", "digest", cron: "0 8 30 2 *")), "targets[0]"),
            (Rule(Scenario.Target("email", "sometimes")), "targets[0]"),
        })
        {
            var problem = await NotifyTestHost.JsonAsync(await s.Owner.PostAsync(rules, body), HttpStatusCode.BadRequest);
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"{field}: {problem}");
        }

        Assert.Equal(0L, await s.CountAsync($"SELECT count(*) FROM notify.rule WHERE organization_id = '{s.Organization}'"));
        foreach (var id in new[] { Guid.CreateVersion7(), foreignRule })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await s.Owner.GetAsync($"{rules}/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await s.Owner.PutAsync($"{rules}/{id}", Rule(inApp))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await s.Owner.SendAsync(HttpMethod.Delete, $"{rules}/{id}")).StatusCode);
        }
    }

    [Fact]
    public async Task An_edited_preset_keeps_the_targets_that_stay_with_their_schedule_and_the_next_migrate_leaves_it_as_the_admin_left_it()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host, keepDefaultRules: true);
        var rules = $"{s.Path}/rules";
        var high = (await NotifyTestHost.JsonAsync(await s.Owner.GetAsync(rules))).EnumerateArray().Single(r => r.GetProperty("preset").GetString() == "high");
        var id = high.GetProperty("id").GetGuid();
        var kept = high.GetProperty("targets").EnumerateArray().Where(t => t.GetProperty("mode").GetString() == "digest").ToList();

        host.Time.Advance(TimeSpan.FromMinutes(1));
        var targets = kept.Select(t => new
        {
            channel = t.GetProperty("channel").GetString(),
            recipientKind = t.GetProperty("recipientKind").GetString(),
            recipientRef = t.GetProperty("recipientRef").GetString(),
            mode = t.GetProperty("mode").GetString(),
            digestCron = t.GetProperty("digestCron").GetString(),
            sendEmpty = t.GetProperty("sendEmpty").GetBoolean(),
        }).Append(new { channel = (string?)"email", recipientKind = (string?)"role", recipientRef = (string?)"owner", mode = (string?)"immediate", digestCron = (string?)null, sendEmpty = false });
        var edited = await NotifyTestHost.JsonAsync(await s.Owner.PutAsync($"{rules}/{id}", new
        {
            name = "High",
            minLevel = 4,
            events = new[] { "action.raised", "action.level_raised" },
            targets = targets.ToArray(),
        }));

        var after = edited.GetProperty("targets").EnumerateArray().ToList();
        Assert.Equal(2, after.Count);
        Assert.Equal(kept.Select(Describe), after.Take(1).Select(Describe));
        Assert.Equal("high", edited.GetProperty("preset").GetString());

        await host.MigrateAgainAsync();

        var reread = await NotifyTestHost.JsonAsync(await s.Owner.GetAsync($"{rules}/{id}"));
        Assert.Equal(["action.raised", "action.level_raised"], reread.GetProperty("events").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(after.Select(Describe), reread.GetProperty("targets").EnumerateArray().Select(Describe));
    }

    private static string Describe(JsonElement target) =>
        $"{target.GetProperty("id")} {target.GetProperty("channel")} {target.GetProperty("mode")} {target.GetProperty("nextDigestAt")}";
}
