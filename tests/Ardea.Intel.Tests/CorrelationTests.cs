using Ardea.Intel.Sources.Abstractions;
using Ardea.TestSupport;

namespace Ardea.Intel.Tests;

/// <summary>From observations to consolidated entries (tasks 1.6, 1.7; phase 1 acceptance).</summary>
public sealed class CorrelationTests(PostgresServer server)
{
    private static readonly string[] Pipeline = ["intel.normalize-records", "intel.correlate-entry"];

    [Fact]
    public async Task The_same_delta_twice_changes_nothing_no_revision_and_no_follow_up_job()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        await DeliverAsync(host, ("A", """{"aliases":["CVE:CVE-2026-1"],"title":"First","score":9.8}"""));
        Assert.Equal(1, await host.ScalarAsync<int>("SELECT revision FROM intel.security_entry"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'intel.notify-entry-changed'"));

        await DeliverAsync(host, ("A", """{"aliases":["CVE:CVE-2026-1"],"title":"First","score":9.8}"""));

        Assert.Equal(1, await host.ScalarAsync<int>("SELECT revision FROM intel.security_entry"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.entry_change"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'intel.notify-entry-changed'"));
    }

    [Fact]
    public async Task A_reworded_title_is_a_revision_but_not_material()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        await DeliverAsync(host, ("A", """{"aliases":["CVE:CVE-2026-1"],"title":"First","score":9.8}"""));
        await host.RunJobsAsync("intel.notify-entry-changed");

        await DeliverAsync(host, ("A", """{"aliases":["CVE:CVE-2026-1"],"title":"First, reworded","score":9.8}"""));

        Assert.Equal(2, await host.ScalarAsync<int>("SELECT revision FROM intel.security_entry"));
        Assert.False(await host.ScalarAsync<bool>("SELECT is_material FROM intel.entry_change WHERE revision = 2"));
        Assert.Equal("text", await host.ScalarAsync<string>("SELECT change_kinds[1] FROM intel.entry_change WHERE revision = 2"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'intel.notify-entry-changed' AND state = 'pending'"));

        await DeliverAsync(host, ("A", """{"aliases":["CVE:CVE-2026-1"],"title":"First, reworded","score":5.3}"""));
        Assert.True(await host.ScalarAsync<bool>("SELECT is_material FROM intel.entry_change WHERE revision = 3"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'intel.notify-entry-changed' AND state = 'pending'"));
    }

    [Fact]
    public async Task Observations_sharing_an_alias_are_one_entry_with_all_identifiers()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        await DeliverAsync(
            host,
            ("A", """{"aliases":["CVE:CVE-2026-7"],"title":"Record"}"""),
            ("B", """{"aliases":["GHSA:GHSA-xxxx-yyyy-zzzz","CVE:CVE-2026-7"],"title":"Package advisory"}"""));

        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry"));
        Assert.Equal("CVE-2026-7", await host.ScalarAsync<string>("SELECT primary_key FROM intel.security_entry"));
        Assert.Equal(2L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.entry_alias"));
    }

    [Fact]
    public async Task An_identifier_found_later_merges_two_entries_and_the_old_key_redirects()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        await DeliverAsync(host, ("G", """{"aliases":["GHSA:GHSA-aaaa-bbbb-cccc"],"title":"Package advisory"}"""));
        await DeliverAsync(host, ("C", """{"aliases":["CVE:CVE-2026-8"],"title":"Record"}"""));
        Assert.Equal(2L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry"));

        // The package advisory now names the CVE id.
        await DeliverAsync(host, ("G", """{"aliases":["GHSA:GHSA-aaaa-bbbb-cccc","CVE:CVE-2026-8"],"title":"Package advisory"}"""));

        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry WHERE state <> 'merged'"));
        Assert.Equal("merged", await host.ScalarAsync<string>("SELECT state FROM intel.security_entry WHERE primary_key = 'GHSA-aaaa-bbbb-cccc'"));
        Assert.True(await host.ScalarAsync<bool>(
            "SELECT m.merged_into_id = w.id FROM intel.security_entry m, intel.security_entry w WHERE m.primary_key = 'GHSA-aaaa-bbbb-cccc' AND w.primary_key = 'CVE-2026-8'"));
    }

    [Fact]
    public async Task Two_entries_with_their_own_cve_ids_are_not_merged()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        await DeliverAsync(host, ("A", """{"aliases":["CVE:CVE-2026-10"]}"""), ("B", """{"aliases":["CVE:CVE-2026-11"]}"""));

        await DeliverAsync(host, ("X", """{"aliases":["GHSA:GHSA-dddd-eeee-ffff","CVE:CVE-2026-10","CVE:CVE-2026-11"]}"""));

        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry WHERE state = 'merged'"));
        Assert.Equal(2L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry"));

        // The case is on the review list once, with the identifier that joins them.
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.review_item"));
        Assert.Equal("CVE-2026-10,CVE-2026-11|open|GHSA:GHSA-dddd-eeee-ffff", await host.ScalarAsync<string>(
            "SELECT subject_key || '|' || state || '|' || array_to_string(shared_aliases, ',') FROM intel.review_item"));

        // The bridge belongs to neither entry; each keeps only its own identifier.
        Assert.Equal("CVE:CVE-2026-10,CVE:CVE-2026-11", await host.ScalarAsync<string>(
            "SELECT string_agg(scheme || ':' || value, ',' ORDER BY value) FROM intel.entry_alias"));
    }

    [Fact]
    public async Task Entries_kept_apart_still_follow_their_own_records()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        await DeliverAsync(host, ("A", """{"aliases":["CVE:CVE-2026-10"],"title":"First"}"""), ("B", """{"aliases":["CVE:CVE-2026-11"],"title":"Second"}"""));
        await DeliverAsync(host, ("X", """{"aliases":["GHSA:GHSA-dddd-eeee-ffff","CVE:CVE-2026-10","CVE:CVE-2026-11"]}"""));

        await DeliverAsync(host, ("A", """{"aliases":["CVE:CVE-2026-10"],"title":"First, revised"}"""));

        Assert.Equal("First, revised", await host.ScalarAsync<string>("SELECT title FROM intel.security_entry WHERE primary_key = 'CVE-2026-10'"));
        Assert.Equal("Second", await host.ScalarAsync<string>("SELECT title FROM intel.security_entry WHERE primary_key = 'CVE-2026-11'"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.review_item"));
    }

    [Fact]
    public async Task A_case_kept_separate_stays_decided_when_the_same_evidence_comes_again()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        await DeliverAsync(host, ("A", """{"aliases":["CVE:CVE-2026-10"]}"""), ("B", """{"aliases":["CVE:CVE-2026-11"]}"""));
        await DeliverAsync(host, ("X", """{"aliases":["GHSA:GHSA-dddd-eeee-ffff","CVE:CVE-2026-10","CVE:CVE-2026-11"]}"""));
        await host.ExecuteAsync("UPDATE intel.review_item SET state = 'kept_separate', decided_at = now()");

        await DeliverAsync(host, ("X", """{"aliases":["GHSA:GHSA-dddd-eeee-ffff","CVE:CVE-2026-10","CVE:CVE-2026-11"],"title":"again"}"""));

        Assert.Equal("kept_separate", await host.ScalarAsync<string>("SELECT state FROM intel.review_item"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM intel.security_entry WHERE state = 'merged'"));
    }

    [Fact]
    public async Task A_package_advisory_reached_from_one_side_only_still_merges_into_that_side()
    {
        await using var host = await IntelTestHost.StartAsync(server);
        await DeliverAsync(host, ("P", """{"aliases":["GHSA:GHSA-1111-2222-3333"],"title":"Only package"}"""));
        await DeliverAsync(host, ("A", """{"aliases":["CVE:CVE-2026-10"]}"""), ("B", """{"aliases":["CVE:CVE-2026-11"]}"""));
        await DeliverAsync(host, ("X", """{"aliases":["GHSA:GHSA-dddd-eeee-ffff","CVE:CVE-2026-10","CVE:CVE-2026-11"]}"""));

        // The package advisory learns its CVE id: it joins CVE-2026-10, not the bridge.
        await DeliverAsync(host, ("P", """{"aliases":["GHSA:GHSA-1111-2222-3333","CVE:CVE-2026-10"],"title":"Only package"}"""));

        Assert.Equal("merged", await host.ScalarAsync<string>("SELECT state FROM intel.security_entry WHERE primary_key = 'GHSA-1111-2222-3333'"));
        Assert.True(await host.ScalarAsync<bool>(
            "SELECT m.merged_into_id = w.id FROM intel.security_entry m, intel.security_entry w WHERE m.primary_key = 'GHSA-1111-2222-3333' AND w.primary_key = 'CVE-2026-10'"));
        Assert.Equal("CVE-2026-10", await host.ScalarAsync<string>(
            "SELECT e.primary_key FROM intel.entry_alias a JOIN intel.security_entry e ON e.id = a.entry_id WHERE a.value = 'GHSA-1111-2222-3333'"));
    }

    [Fact]
    public async Task A_warning_without_identifier_is_its_own_entry_bound_to_its_source()
    {
        await using var host = await IntelTestHost.StartAsync(server);

        await DeliverAsync(host, ("W-1", """{"kind":"warning","title":"Phishing calls in the name of the police"}"""));

        Assert.Equal("warning", await host.ScalarAsync<string>("SELECT kind FROM intel.security_entry"));
        Assert.Equal("scripted:W-1", await host.ScalarAsync<string>("SELECT primary_key FROM intel.security_entry"));
    }

    private static async Task DeliverAsync(IntelTestHost host, params (string Id, string Json)[] records)
    {
        host.Provider.Items.Clear();
        host.Provider.Items.AddRange(records.Select(r => (FetchItem)ScriptedProvider.Json(r.Id, r.Json)));
        host.Provider.Items.Add(new Checkpoint(Guid.NewGuid().ToString()));
        await host.SyncAsync();
        await host.RunJobsAsync(Pipeline);
    }
}
