using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Iam.Tests;

/// <summary>
/// The API of actions and alerts (task 3.5; master plan 10.2 «Massnahmen und Meldungen»): lists with
/// filters, details with findings and history, status changes with the expected starting status.
/// The assessment itself is tested in Ardea.Assessment.Tests; here its rows are written directly.
/// </summary>
public sealed class ActionApiTests(PostgresServer server)
{
    [Fact]
    public async Task Members_read_actions_and_alerts_and_change_their_status()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var client = host.Client();
        await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "owner@example.org", displayName = "Owner", password = ApiTestHost.Password });
        using var _ = client;
        var org = (await Json(await client.PostAsync("/api/v1/orgs", new { name = "Acme" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var project = (await Json(await client.PostAsync($"/api/v1/orgs/{org}/projects", new { name = "Platform Ops" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var component = (await Json(await client.PostAsync($"/api/v1/orgs/{org}/projects/{project}/components", new { productKey = "postgresql", version = "16.2" }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var finding = await SeedAsync(host, org, project, component);
        var path = $"/api/v1/orgs/{org}";

        var list = await Json(await client.GetAsync($"{path}/actions?level=4,5&project={project}"));
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(2, item.GetProperty("number").GetInt32());
        Assert.Equal("high", item.GetProperty("levelName").GetString());
        Assert.Equal("PostgreSQL", item.GetProperty("component").GetProperty("name").GetString());
        Assert.Equal("Platform Ops", item.GetProperty("project").GetProperty("name").GetString());
        Assert.Equal(["CVE-2026-0001"], item.GetProperty("entries").EnumerateArray().Select(e => e.GetString()));
        Assert.Empty((await Json(await client.GetAsync($"{path}/actions?level=5"))).GetProperty("items").EnumerateArray());
        Assert.Single((await Json(await client.GetAsync($"{path}/actions?q=cve-2026-0001"))).GetProperty("items").EnumerateArray());
        Assert.Single((await Json(await client.GetAsync($"{path}/actions?q=postgres"))).GetProperty("items").EnumerateArray());
        await ExpectProblem(await client.GetAsync($"{path}/actions?level=9"), HttpStatusCode.BadRequest, "invalid_filter");

        var detail = await Json(await client.GetAsync($"{path}/actions/2"));
        Assert.Equal(finding, detail.GetProperty("findings")[0].GetProperty("id").GetGuid());
        Assert.Equal(1, detail.GetProperty("findings")[0].GetProperty("alertNumber").GetInt32());

        // The status the caller saw must still hold.
        await ExpectProblem(await client.PostAsync($"{path}/actions/2/status", new { status = "in_review", expectedStatus = "in_review" }), HttpStatusCode.Conflict, "status_conflict");
        var inReview = await Json(await client.PostAsync($"{path}/actions/2/status", new { status = "in_review", expectedStatus = "open", note = "Checking" }));
        Assert.Equal("in_review", inReview.GetProperty("action").GetProperty("status").GetString());
        Assert.Equal("in_review", inReview.GetProperty("findings")[0].GetProperty("status").GetString());

        var note = await Json(await client.PostAsync($"{path}/actions/2/notes", new { text = "Window on Friday" }), HttpStatusCode.Created);
        Assert.Equal("action.note_added", note.GetProperty("kind").GetString());

        // The outcome of the single finding closes the action.
        var closed = await Json(await client.SendAsync(HttpMethod.Patch, $"{path}/findings/{finding}", new { status = "remediated", expectedStatus = "in_review" }));
        Assert.Equal("remediated", closed.GetProperty("status").GetString());
        var after = await Json(await client.GetAsync($"{path}/actions/2"));
        Assert.Equal("closed", after.GetProperty("action").GetProperty("status").GetString());
        Assert.Equal("remediated", after.GetProperty("action").GetProperty("closeReason").GetString());
        Assert.Equal(
            ["action.status_changed", "action.note_added", "finding.status_changed"],
            (await Json(await client.GetAsync($"{path}/actions/2/events"))).EnumerateArray().Select(e => e.GetProperty("kind").GetString()));
        Assert.Empty((await Json(await client.GetAsync($"{path}/actions"))).GetProperty("items").EnumerateArray());
        Assert.Single((await Json(await client.GetAsync($"{path}/actions?status=closed"))).GetProperty("items").EnumerateArray());

        var alert = await Json(await client.GetAsync($"{path}/alerts/1"));
        Assert.Equal("CVE-2026-0001", alert.GetProperty("entry").GetProperty("key").GetString());
        Assert.Equal(2, alert.GetProperty("actions")[0].GetProperty("number").GetInt32());
        Assert.Single((await Json(await client.GetAsync($"{path}/alerts"))).GetProperty("items").EnumerateArray());
        Assert.Single((await Json(await client.GetAsync($"{path}/findings?status=remediated"))).GetProperty("items").EnumerateArray());

        var overview = await Json(await client.GetAsync($"{path}/overview"));
        Assert.All(overview.GetProperty("openByLevel").EnumerateArray(), l => Assert.Equal(0, l.GetProperty("actions").GetInt32()));
        Assert.Equal(2, overview.GetProperty("last24Hours").GetArrayLength());

        Assert.Equal("action.status_changed", await host.ScalarAsync<string>($"SELECT action FROM platform.audit_event WHERE subject_type = 'action' LIMIT 1"));
    }

    [Fact]
    public async Task Concurrent_status_changes_from_the_same_status_let_exactly_one_through()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var client = host.Client();
        await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "owner@example.org", displayName = "Owner", password = ApiTestHost.Password });
        using var _ = client;
        var org = (await Json(await client.PostAsync("/api/v1/orgs", new { name = "Acme" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var project = (await Json(await client.PostAsync($"/api/v1/orgs/{org}/projects", new { name = "Platform Ops" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var component = (await Json(await client.PostAsync($"/api/v1/orgs/{org}/projects/{project}/components", new { productKey = "postgresql", version = "16.2" }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        await SeedAsync(host, org, project, component);

        // Several people decide at once from what they all saw: open. One decision wins, the others reload.
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => client.PostAsync(
            $"/api/v1/orgs/{org}/actions/2/status",
            i % 2 == 0 ? new { status = "in_review", expectedStatus = "open" } : (object)new { status = "risk_accepted", expectedStatus = "open" })));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assess_event WHERE kind = 'action.status_changed'"));
    }

    /// <summary>An alert, an action and a finding as the assessment would have written them.</summary>
    private static async Task<Guid> SeedAsync(ApiTestHost host, Guid org, Guid project, Guid component)
    {
        var entry = await host.ScalarAsync<Guid>("""
            INSERT INTO intel.security_entry (kind, primary_key, title, state, changed_at, revision, material_fingerprint, remediation_fingerprint, exploitation, fix_available, cwe_ids, authority_warning, provenance)
            VALUES ('vulnerability', 'CVE-2026-0001', 'PostgreSQL: privilege escalation', 'published', now(), 1, 'm', 'r', 'none', true, '{}', false, '{}') RETURNING id
            """);
        var alert = await host.ScalarAsync<Guid>($"""
            INSERT INTO assess.alert (organization_id, entry_id, entry_key, number, level, assessed_entry_revision, first_raised_at, created_at, changed_at)
            VALUES ('{org}', '{entry}', 'CVE-2026-0001', 1, 4, 1, now(), now(), now()) RETURNING id
            """);
        var action = await host.ScalarAsync<Guid>($"""
            INSERT INTO assess.remediation (organization_id, project_id, component_id, number, level, target_version, open_findings, exploitation, fix_available, status, due_at, due_at_manual, opened_at, is_backfill, changed_at)
            VALUES ('{org}', '{project}', '{component}', 2, 4, '16.4', 1, 'none', true, 'open', now() + interval '7 days', false, now(), false, now()) RETURNING id
            """);
        await host.ScalarAsync<int>($"INSERT INTO assess.org_counter (organization_id, next_number) VALUES ('{org}', 3) RETURNING 1");
        return await host.ScalarAsync<Guid>($"""
            INSERT INTO assess.finding (organization_id, alert_id, entry_id, project_id, component_id, remediation_id, match_type, version_verdict, version_reason, fixed_version, verdict, verdict_source, level, evidence, factors, status, status_changed_at, is_backfill, created_at, changed_at, exploitation, fix_available)
            VALUES ('{org}', '{alert}', '{entry}', '{project}', '{component}', '{action}', 'cpe', 'in_range', 'in_affected_range', '16.4', 'affected', 'rules', 4, jsonb_build_object(), jsonb_build_object('rulesetVersion', 1), 'open', now(), false, now(), now(), 'none', true) RETURNING id
            """);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.True(status == response.StatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task ExpectProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
