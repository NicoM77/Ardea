using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Iam.Tests;

public sealed class EntryApiTests(PostgresServer server)
{
    [Fact]
    public async Task Entries_need_a_signed_in_user()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var stranger = host.Client();

        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/v1/entries")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/v1/sources")).StatusCode);
    }

    [Fact]
    public async Task Search_finds_by_any_alias_and_by_words_and_pages_through_the_rest()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var user = await SignedInAsync(host);
        await SeedAsync(host);

        var byAlias = await Page(user, "/api/v1/entries?q=GHSA-aaaa-bbbb-cccc");
        Assert.Equal("CVE-2026-1", Assert.Single(byAlias.GetProperty("items").EnumerateArray()).GetProperty("key").GetString());

        var byWords = await Page(user, "/api/v1/entries?q=remote%20code%20execution");
        Assert.Equal("CVE-2026-1", Assert.Single(byWords.GetProperty("items").EnumerateArray()).GetProperty("key").GetString());

        var first = await Page(user, "/api/v1/entries?limit=2");
        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        var second = await Page(user, "/api/v1/entries?limit=2&cursor=" + Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!));
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        Assert.DoesNotContain(first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()), e => e.GetProperty("key").GetString() == "GHSA-old-merged");
    }

    [Fact]
    public async Task A_merged_key_leads_to_the_entry_it_was_merged_into()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var user = await SignedInAsync(host);
        await SeedAsync(host);

        var detail = await (await user.GetAsync("/api/v1/entries/GHSA-old-merged")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("CVE-2026-1", detail.GetProperty("entry").GetProperty("key").GetString());
        Assert.Equal("GHSA-old-merged", detail.GetProperty("redirectedFrom").GetString());
        Assert.Equal("critical", detail.GetProperty("entry").GetProperty("severity").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync("/api/v1/entries/CVE-1999-0000")).StatusCode);
    }

    [Fact]
    public async Task Members_see_sources_and_only_the_administrator_steers_them()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var admin = await SignedInAsync(host);
        using var member = host.Client();
        await member.PostAsync("/api/v1/auth/register", new { email = "member@example.org", displayName = "Member", password = ApiTestHost.Password });
        await host.ScalarAsync<int>("INSERT INTO intel.source (key, name, kind, operator, operator_kind, adapter, enabled, interval, config, attribution, terms_mode, parser_version, next_sync_at, consecutive_failures, health) VALUES ('ncsc-hub', 'NCSC Hub', 'advisory', 'NCSC', 'authority', 'x', true, interval '30 minutes', '{}', 'NCSC', 'full', 1, now() + interval '1 hour', 0, 'healthy') RETURNING 1");

        var list = await (await member.GetAsync("/api/v1/sources")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(list.EnumerateArray(), s => s.GetProperty("key").GetString() == "ncsc-hub" && s.GetProperty("operatorKind").GetString() == "authority");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.SendAsync(HttpMethod.Patch, "/api/v1/instance/sources/ncsc-hub", new { enabled = false })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Patch, "/api/v1/instance/sources/ncsc-hub", new { intervalMinutes = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Patch, "/api/v1/instance/sources/ncsc-hub", new { intervalMinutes = 60 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/v1/instance/sources/ncsc-hub/sync")).StatusCode);
        Assert.True(await host.ScalarAsync<bool>("SELECT next_sync_at <= now() AND interval = interval '60 minutes' FROM intel.source WHERE key = 'ncsc-hub'"));
    }

    [Fact]
    public async Task The_administrator_sees_cve_conflicts_and_keeps_them_separate()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var admin = await SignedInAsync(host);
        using var member = host.Client();
        await member.PostAsync("/api/v1/auth/register", new { email = "member@example.org", displayName = "Member", password = ApiTestHost.Password });
        await SeedAsync(host);
        await host.ScalarAsync<int>(
            """
            INSERT INTO intel.review_item (kind, subject_key, entry_ids, shared_aliases, state, first_seen_at, last_seen_at)
            SELECT 'cve_conflict', 'CVE-2026-1,CVE-2026-2', array_agg(id ORDER BY primary_key), '{GHSA:GHSA-dddd-eeee-ffff}', 'open', now(), now()
            FROM intel.security_entry WHERE primary_key IN ('CVE-2026-1', 'CVE-2026-2')
            RETURNING 1
            """);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/instance/review")).StatusCode);

        var open = await Page(admin, "/api/v1/instance/review");
        var item = Assert.Single(open.EnumerateArray());
        Assert.Equal("cve_conflict", item.GetProperty("kind").GetString());
        Assert.Equal(["CVE-2026-1", "CVE-2026-2"], item.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("primaryKey").GetString()));
        Assert.Equal("GHSA:GHSA-dddd-eeee-ffff", Assert.Single(item.GetProperty("sharedAliases").EnumerateArray()).GetString());

        var id = item.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync($"/api/v1/instance/review/{id}/keep-separate")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/instance/review/{id}/keep-separate")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/v1/instance/review/{Guid.NewGuid()}/keep-separate")).StatusCode);

        Assert.Empty((await Page(admin, "/api/v1/instance/review")).EnumerateArray());
        Assert.Single((await Page(admin, "/api/v1/instance/review?state=kept_separate")).EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/instance/review?state=nonsense")).StatusCode);
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.audit_event WHERE action = 'instance.review_kept_separate'"));
    }

    private static async Task<ApiClient> SignedInAsync(ApiTestHost host)
    {
        var client = host.Client();
        await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "admin@example.org", displayName = "Admin", password = ApiTestHost.Password });
        return client;
    }

    private static async Task<JsonElement> Page(ApiClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task SeedAsync(ApiTestHost host) => host.ScalarAsync<int>(
        """
        WITH e1 AS (
            INSERT INTO intel.security_entry (kind, primary_key, title, description, state, changed_at, revision, material_fingerprint, remediation_fingerprint, cvss_score, exploitation, fix_available, authority_warning, provenance, cwe_ids)
            VALUES ('vulnerability', 'CVE-2026-1', 'Remote code execution in Acme Portal', 'An attacker can run code remotely.', 'published', now() - interval '1 minute', 1, 'a', 'b', 9.8, 'active', true, false, '{}', '{}') RETURNING id
        ), e2 AS (
            INSERT INTO intel.security_entry (kind, primary_key, title, state, changed_at, revision, material_fingerprint, remediation_fingerprint, exploitation, fix_available, authority_warning, provenance, cwe_ids)
            VALUES ('vulnerability', 'CVE-2026-2', 'Cross-site scripting in Acme Shop', 'published', now() - interval '2 minutes', 1, 'a', 'b', 'none', false, false, '{}', '{}') RETURNING id
        ), e3 AS (
            INSERT INTO intel.security_entry (kind, primary_key, title, state, changed_at, revision, material_fingerprint, remediation_fingerprint, exploitation, fix_available, authority_warning, provenance, cwe_ids)
            VALUES ('warning', 'bacs-warnings:42', 'Phishing in the name of a bank', 'published', now() - interval '3 minutes', 1, 'a', 'b', 'none', false, true, '{}', '{}') RETURNING id
        ), merged AS (
            INSERT INTO intel.security_entry (kind, primary_key, state, merged_into_id, changed_at, revision, material_fingerprint, remediation_fingerprint, exploitation, fix_available, authority_warning, provenance, cwe_ids)
            SELECT 'vulnerability', 'GHSA-old-merged', 'merged', id, now(), 1, 'a', 'b', 'none', false, false, '{}', '{}' FROM e1 RETURNING id
        )
        INSERT INTO intel.entry_alias (entry_id, scheme, value)
        SELECT id, 'CVE', 'CVE-2026-1' FROM e1 UNION ALL SELECT id, 'GHSA', 'GHSA-aaaa-bbbb-cccc' FROM e1 UNION ALL SELECT id, 'CVE', 'CVE-2026-2' FROM e2
        RETURNING 1
        """);
}
