using Ardea.Intel.Contracts;
using Ardea.Notifications.Evaluation;
using Ardea.Platform.Persistence;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Notifications.Tests;

public sealed class EventIntakeTests(PostgresServer server)
{
    [Fact]
    public async Task More_events_than_a_batch_are_taken_in_turns_and_one_whose_history_is_gone_is_dropped()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("Everything", 2, ["action.raised"], Scenario.Target("in_app"));
        await host.ExecuteAsync($"""
            INSERT INTO notify.pending_event (event_id, organization_id, kind, at)
            SELECT gen_random_uuid(), '{s.Organization}', 'action.raised', '{host.Time.GetUtcNow():O}' FROM generate_series(1, {EvaluateEvents.Batch})
            """);
        await s.EventAsync("action.raised", await s.ActionAsync(2, level: 4));

        await s.EvaluateAsync();
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM notify.pending_event"));
        Assert.Equal(1L, await s.CountAsync("SELECT count(*) FROM platform.job WHERE kind = 'notify.evaluate' AND state = 'pending'"));

        Assert.Empty(await host.RunJobsAsync());
        Assert.Equal(0L, await s.CountAsync("SELECT count(*) FROM notify.pending_event"));
        Assert.Equal(["action.raised"], await host.RowsAsync("SELECT kind FROM notify.notification"));
    }

    [Fact]
    public async Task Only_a_new_published_warning_of_the_last_two_weeks_is_distributed()
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var s = await Scenario.StartAsync(host);
        await s.RuleAsync("Warnings", 2, ["warning.published"], Scenario.Target("in_app"));
        await host.ExecuteAsync("DELETE FROM platform.job");
        var now = host.Time.GetUtcNow();

        Task<Guid> EntryAsync(string kind, string key, string state, DateTimeOffset published) => host.ScalarAsync<Guid>($"""
            INSERT INTO intel.security_entry (kind, primary_key, title, state, published_at, changed_at, revision, material_fingerprint, remediation_fingerprint, exploitation, fix_available, cwe_ids, authority_warning, provenance)
            VALUES ('{kind}', '{key}', 'Entry {key}', '{state}', '{published:O}', now(), 1, 'm', 'r', 'none', false, ARRAY[]::text[], true, jsonb_build_object()) RETURNING id
            """);

        var fresh = await EntryAsync("warning", "NCSC-2026-0100", "published", now.AddDays(-1));
        var old = await EntryAsync("warning", "NCSC-2026-0001", "published", now.AddDays(-15));
        var rejected = await EntryAsync("warning", "NCSC-2026-0101", "rejected", now);
        var vulnerability = await EntryAsync("vulnerability", "CVE-2026-0102", "published", now);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var subscriber = scope.ServiceProvider.GetServices<IEntryChangeSubscriber>().OfType<WarningSubscriber>().Single();
            foreach (var entry in new[] { fresh, old, rejected, vulnerability })
            {
                await subscriber.OnEntryChangedAsync(new EntryChanged(entry, 1, ["created"]), CancellationToken.None);
            }

            await subscriber.OnEntryChangedAsync(new EntryChanged(fresh, 2, ["changed"]), CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
        }

        Assert.Equal(4L, await s.CountAsync("SELECT count(*) FROM platform.job WHERE kind = 'notify.distribute-warning'"));
        Assert.Empty(await host.RunJobsAsync());
        Assert.Equal(["[Warning] Entry NCSC-2026-0100"], await host.RowsAsync("SELECT title FROM notify.notification"));
    }
}
