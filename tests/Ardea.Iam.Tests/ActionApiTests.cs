using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Iam.Tests;

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

        var answers = detail.GetProperty("answers");
        Assert.Equal("template", answers.GetProperty("origin").GetString());
        Assert.Equal("CVE-2026-0001", answers.GetProperty("whatHappened").GetProperty("entryKey").GetString());
        Assert.Equal("affected", answers.GetProperty("affected").GetProperty("verdict").GetString());
        Assert.Equal("update", answers.GetProperty("toDo").GetProperty("kind").GetString());
        Assert.Equal("16.4", answers.GetProperty("toDo").GetProperty("targetVersion").GetString());
        var urgent = (await Json(await client.GetAsync($"{path}/overview"))).GetProperty("urgent")[0];
        Assert.Equal(2, urgent.GetProperty("action").GetProperty("number").GetInt32());
        Assert.Equal("16.4", urgent.GetProperty("answers").GetProperty("toDo").GetProperty("targetVersion").GetString());
        Assert.Equal("CVE-2026-0001", (await Json(await client.GetAsync($"{path}/alerts/1"))).GetProperty("answers").GetProperty("whatHappened").GetProperty("entryKey").GetString());
        Assert.Equal(1, detail.GetProperty("findings")[0].GetProperty("alertNumber").GetInt32());

        foreach (var gone in new[] { "assignedTo", "dueAt", "overdue" })
        {
            Assert.False(item.TryGetProperty(gone, out var value), $"{gone}: {value}");
        }

        Assert.False((await Json(await client.GetAsync($"{path}/overview"))).TryGetProperty("overdue", out var overdue), overdue.ToString());
        Assert.Contains(
            (await client.SendAsync(HttpMethod.Patch, $"{path}/actions/2", new { dueAt = "2030-01-01T00:00:00Z" })).StatusCode,
            new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        await ExpectProblem(await client.PostAsync($"{path}/actions/2/status", new { status = "in_review", expectedStatus = "open" }), HttpStatusCode.BadRequest, "invalid_status_change");
        await ExpectProblem(await client.GetAsync($"{path}/actions?status=in_review"), HttpStatusCode.BadRequest, "invalid_filter");

        await ExpectProblem(await client.PostAsync($"{path}/actions/2/status", new { status = "risk_accepted", expectedStatus = "closed" }), HttpStatusCode.Conflict, "status_conflict");

        var note = await Json(await client.PostAsync($"{path}/actions/2/notes", new { text = "Window on Friday" }), HttpStatusCode.Created);
        Assert.Equal("action.note_added", note.GetProperty("kind").GetString());

        var review = $"{path}/findings/{finding}/review";
        await ExpectProblem(await client.PostAsync(review, new { decision = "correct", expectedVerdict = "affected", expectedSource = "rules", verdict = "unlikely" }), HttpStatusCode.BadRequest, "invalid_review");
        await ExpectProblem(await client.PostAsync(review, new { decision = "confirm", expectedVerdict = "likely", expectedSource = "model" }), HttpStatusCode.Conflict, "verdict_conflict");
        await ExpectProblem(await client.PostAsync(review, new { decision = "confirm", expectedVerdict = "affected", expectedSource = "rules" }), HttpStatusCode.Conflict, "nothing_to_confirm");
        await ExpectProblem(await client.PostAsync($"{path}/findings/{Guid.CreateVersion7()}/review", new { decision = "confirm", expectedVerdict = "affected", expectedSource = "model" }), HttpStatusCode.NotFound, "finding_not_found");
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assessment_feedback"));

        var closed = await Json(await client.SendAsync(HttpMethod.Patch, $"{path}/findings/{finding}", new { status = "remediated", expectedStatus = "open" }));
        Assert.Equal("remediated", closed.GetProperty("status").GetString());
        var after = await Json(await client.GetAsync($"{path}/actions/2"));
        Assert.Equal("closed", after.GetProperty("action").GetProperty("status").GetString());
        Assert.Equal("remediated", after.GetProperty("action").GetProperty("closeReason").GetString());
        Assert.Equal(
            ["action.note_added", "finding.status_changed"],
            (await Json(await client.GetAsync($"{path}/actions/2/events"))).EnumerateArray().Select(e => e.GetProperty("kind").GetString()));
        Assert.Empty((await Json(await client.GetAsync($"{path}/actions"))).GetProperty("items").EnumerateArray());
        Assert.Single((await Json(await client.GetAsync($"{path}/actions?status=closed"))).GetProperty("items").EnumerateArray());

        var alert = await Json(await client.GetAsync($"{path}/alerts/1"));
        Assert.Equal("CVE-2026-0001", alert.GetProperty("entry").GetProperty("key").GetString());
        Assert.Equal(2, alert.GetProperty("actions")[0].GetProperty("number").GetInt32());
        Assert.Single((await Json(await client.GetAsync($"{path}/alerts"))).GetProperty("items").EnumerateArray());

        await ExpectProblem(await client.PostAsync($"{path}/alerts/1/assessment", null), HttpStatusCode.Conflict, "ai_not_available");
        await ExpectProblem(await client.PostAsync($"{path}/alerts/99/assessment", null), HttpStatusCode.NotFound, "alert_not_found");
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'assess.estimate-alert'"));
        Assert.Single((await Json(await client.GetAsync($"{path}/findings?status=remediated"))).GetProperty("items").EnumerateArray());

        var overview = await Json(await client.GetAsync($"{path}/overview"));
        Assert.All(overview.GetProperty("openByLevel").EnumerateArray(), l => Assert.Equal(0, l.GetProperty("actions").GetInt32()));
        Assert.Equal(["action.note_added"], overview.GetProperty("last24Hours").EnumerateArray().Select(c => c.GetProperty("kind").GetString()));

        Assert.Equal("finding.status_changed", await host.ScalarAsync<string>($"SELECT action FROM platform.audit_event WHERE subject_type = 'finding' LIMIT 1"));
    }

    [Fact]
    public async Task History_written_before_the_reminder_leaves_out_assignment_and_review()
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
        var action = await host.ScalarAsync<Guid>($"SELECT id FROM assess.remediation WHERE organization_id = '{org}'");

        await host.ScalarAsync<int>($$"""
            INSERT INTO assess.assess_event (organization_id, remediation_id, at, kind, data) VALUES
                ('{{org}}', '{{action}}', now() - interval '5 minutes', 'action.assigned', '{"from": null, "to": "{{Guid.CreateVersion7()}}"}'),
                ('{{org}}', '{{action}}', now() - interval '4 minutes', 'action.status_changed', '{"from": "open", "to": "in_review"}'),
                ('{{org}}', '{{action}}', now() - interval '3 minutes', 'action.status_changed', '{"from": "in_review", "to": "open"}'),
                ('{{org}}', '{{action}}', now() - interval '2 minutes', 'action.status_changed', '{"from": "in_review", "to": "risk_accepted"}'),
                ('{{org}}', '{{action}}', now() - interval '1 minute', 'action.status_changed', '{"from": "closed", "to": "in_review"}')
            RETURNING 1
            """);

        var events = (await Json(await client.GetAsync($"/api/v1/orgs/{org}/actions/2/events"))).EnumerateArray()
            .Select(e => $"{e.GetProperty("kind").GetString()} {e.GetProperty("data").GetProperty("from").GetString()}>{e.GetProperty("data").GetProperty("to").GetString()}")
            .ToList();
        Assert.Equal(["action.status_changed open>risk_accepted", "action.status_changed closed>open"], events);
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

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => client.PostAsync(
            $"/api/v1/orgs/{org}/actions/2/status",
            new { status = (i % 3) switch { 0 => "remediated", 1 => "risk_accepted", _ => "not_affected" }, expectedStatus = "open" })));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assess_event WHERE kind = 'action.status_changed'"));
    }

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
            INSERT INTO assess.remediation (organization_id, project_id, component_id, number, level, target_version, open_findings, exploitation, fix_available, status, opened_at, is_backfill, changed_at)
            VALUES ('{org}', '{project}', '{component}', 2, 4, '16.4', 1, 'none', true, 'open', now(), false, now()) RETURNING id
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
