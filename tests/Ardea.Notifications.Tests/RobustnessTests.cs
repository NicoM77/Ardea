using System.Net;
using Ardea.Intel.Contracts;
using Ardea.Notifications.Delivering;
using Ardea.Notifications.Evaluation;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Notifications.Tests;

/// <summary>
/// What keeps notifications flowing when something goes wrong (ADR 038): events left waiting are found again, one
/// broken event does not hold back the others, a channel that throws neither leaves its message hanging nor keeps
/// the owners from hearing about a lost level 5, the rule compares the level the event reports, and a new time
/// zone moves the next digest.
/// </summary>
public sealed class RobustnessTests(PostgresServer server)
{
    [Fact]
    public async Task Events_left_waiting_are_evaluated_when_the_minute_schedule_sweeps()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("Everything", 2, ["action.raised"], Scenario.Target("in_app"));
        // The event waits, but its evaluation job never ran (a lost wake-up).
        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 4));

        await host.RunAsync(new FindDueDigests(), organization: null);
        Assert.Empty(await host.RunJobsAsync());
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.notification"));

        host.Time.Advance(EvaluateEvents.Stale + TimeSpan.FromSeconds(1));
        await host.RunAsync(new FindDueDigests(), organization: null);
        Assert.Empty(await host.RunJobsAsync());
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.notification"));
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.pending_event"));
    }

    [Fact]
    public async Task An_event_that_always_fails_is_set_aside_and_the_others_of_its_organisation_go_on()
    {
        var broken = Guid.CreateVersion7();
        await using var host = await NotifyTestHost.StartAsync(server, services: s => Decorate<IEntryQueries>(s, inner => new FailingEntries(inner, broken)));
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("Everything", 2, ["action.raised", "warning.published"], Scenario.Target("in_app"));
        await host.ExecuteAsync($"""
            INSERT INTO notify.pending_event (event_id, organization_id, kind, at, entry_id) VALUES ('{broken}', '{s.Organization}', 'warning.published', '{host.Time.GetUtcNow().AddSeconds(-1):O}', '{broken}')
            """);
        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 4));

        await s.EvaluateAsync();
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.notification"));
        Assert.Equal("1|", await host.ScalarAsync<string>("SELECT attempts || '|' || coalesce(failed_at::text, '') FROM notify.pending_event"));

        for (var i = 0; i < EvaluateEvents.MaxEventAttempts - 1; i++)
        {
            await s.EvaluateAsync();
        }

        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.pending_event WHERE failed_at IS NOT NULL AND last_error LIKE '%source down%'"));
        var status = await NotifyTestHost.JsonAsync(await s.Owner.GetAsync("/api/v1/instance/status"));
        Assert.Contains(status.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "notify_events_failed");
        Assert.Contains(status.GetProperty("metrics").EnumerateArray(), m => m.GetProperty("name").GetString() == "events_failed" && m.GetProperty("value").GetDouble() == 1);

        // Set aside: the sweep no longer brings it back.
        host.Time.Advance(TimeSpan.FromMinutes(5));
        await host.RunAsync(new FindDueDigests(), organization: null);
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM platform.job WHERE kind = 'notify.evaluate' AND state = 'pending'"));
    }

    [Fact]
    public async Task A_channel_that_throws_leaves_no_message_in_sending_and_a_lost_level_5_reaches_the_owners()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        using var member = await host.RegisterAsync("member@example.org", "Mia Member");
        await host.AddMemberAsync(s.Organization, "member@example.org", "member");
        var memberId = await host.ScalarAsync<Guid>("SELECT id FROM iam.\"user\" WHERE normalized_email = 'MEMBER@EXAMPLE.ORG'");
        await s.RuleAsync("Critical by mail", 5, ["action.raised"], Scenario.Target("email", kind: "user", reference: memberId.ToString()));
        await host.ExecuteAsync("DELETE FROM platform.job WHERE kind LIKE 'iam.%'");
        host.Mail!.Failure = () => new MailKit.Security.SslHandshakeException("The server's certificate is not trusted.");
        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 5));
        await s.EvaluateAsync();

        host.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.Single(await host.RunJobsAsync());
        Assert.Equal("failed|SslHandshakeException", await host.ScalarAsync<string>(
            "SELECT state || '|' || split_part(last_error, ':', 1) FROM notify.delivery WHERE channel = 'email'"));

        // The last attempt gives up and the owner hears that the member was reached nowhere.
        var delivery = await host.ScalarAsync<Guid>("SELECT id FROM notify.delivery WHERE channel = 'email'");
        await host.RunAsync(new Deliver(delivery), s.Organization, attempt: Deliver.MaxAttempts);
        Assert.Equal("dead", await host.ScalarAsync<string>($"SELECT state FROM notify.delivery WHERE id = '{delivery}'"));
        var owner = await s.OwnerIdAsync();
        Assert.Equal(1L, await s.CountAsync($"SELECT count(*) FROM notify.notification WHERE kind = 'delivery.failed' AND user_id = '{owner}'"));
    }

    [Fact]
    public async Task A_rule_matches_the_level_the_event_reports_not_the_level_the_action_has_reached_since()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("Critical", 5, ["action.raised", "action.level_raised"], Scenario.Target("in_app"));
        var action = await s.ActionAsync(2, level: 5);

        // Raised at 4, then raised to 5; both are evaluated after the action reached 5.
        await s.EventAsync("action.raised", action, """{"level": 4, "findings": 1, "exploitation": "none"}""");
        await s.EventAsync("action.level_raised", action, """{"from": 4, "to": 5}""");
        await s.EvaluateAsync();

        Assert.Equal(["action.level_raised|[5 Critical] #2 PostgreSQL in Platform Ops"], await host.RowsAsync("SELECT kind || '|' || title FROM notify.notification"));
    }

    [Fact]
    public async Task A_new_time_zone_moves_the_next_digest_and_the_next_run_of_the_reminders_to_the_same_local_time()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var (owner, organization) = await host.OwnerAsync();
        using var _ = owner;
        var path = $"/api/v1/orgs/{organization}";
        await host.ExecuteAsync($"""
            INSERT INTO notify.reminder_schedule (organization_id, level5, level4, level3, next_run_at, changed_at)
            VALUES ('{organization}', 'daily', 'weekly', 'monthly', '{host.Time.GetUtcNow().AddHours(1):O}', now())
            """);

        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(HttpMethod.Patch, path, new { timeZone = "Asia/Tokyo" })).StatusCode);

        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var weekly = (await NotifyTestHost.JsonAsync(await owner.GetAsync($"{path}/rules"))).EnumerateArray()
            .Single(r => r.GetProperty("preset").GetString() == "weekly").GetProperty("targets")[0];
        var next = TimeZoneInfo.ConvertTime(weekly.GetProperty("nextDigestAt").GetDateTimeOffset(), tokyo);
        Assert.Equal((DayOfWeek.Monday, 8, 0), (next.DayOfWeek, next.Hour, next.Minute));
        var run = TimeZoneInfo.ConvertTime(new DateTimeOffset(await host.ScalarAsync<DateTime>("SELECT next_run_at FROM notify.reminder_schedule")), tokyo);
        Assert.Equal((true, 8, 0), (run.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday, run.Hour, run.Minute));
        Assert.Equal(Rules.DigestSchedule.Next(ReminderRhythms.Schedule, "Asia/Tokyo", host.Time.GetUtcNow()), run);
    }

    private static void Decorate<TService>(IServiceCollection services, Func<TService, TService> wrap)
        where TService : class
    {
        var original = services.Last(d => d.ServiceType == typeof(TService));
        services.Add(new ServiceDescriptor(typeof(TService), sp => wrap((TService)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)), original.Lifetime));
    }

    /// <summary>The entries of Intel, except one whose lookup always fails.</summary>
    private sealed class FailingEntries(IEntryQueries inner, Guid broken) : IEntryQueries
    {
        public Task<EntryFacts?> FindAsync(Guid entryId, CancellationToken cancellationToken) =>
            entryId == broken ? throw new InvalidOperationException("source down") : inner.FindAsync(entryId, cancellationToken);

        public Task<IReadOnlyList<EntryFacts>> FindManyAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken) => inner.FindManyAsync(entryIds, cancellationToken);

        public Task<IReadOnlyList<Guid>> FindAffectingAsync(AffectedKeys keys, DateTimeOffset since, CancellationToken cancellationToken) => inner.FindAffectingAsync(keys, since, cancellationToken);

        public Task<IReadOnlyList<EntryText>> TextsAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken) => inner.TextsAsync(entryIds, cancellationToken);
    }
}
