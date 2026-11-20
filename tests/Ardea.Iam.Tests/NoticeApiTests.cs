using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Iam.Tests;

public sealed class NoticeApiTests(PostgresServer server)
{
    [Fact]
    public async Task Members_list_notices_answer_them_and_take_an_answer_back()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var client = host.Client();
        await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "owner@example.org", displayName = "Owner", password = ApiTestHost.Password });
        var org = (await Json(await client.PostAsync("/api/v1/orgs", new { name = "Acme" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var path = $"/api/v1/orgs/{org}";
        var stack = (await Json(await client.GetAsync($"{path}/stack"))).GetProperty("projectId").GetGuid();
        var component = (await Json(await client.PostAsync($"{path}/projects/{stack}/components", new { productKey = "postgresql" }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var notice = await SeedAsync(host, org, component);

        var list = await Json(await client.GetAsync($"{path}/notices"));
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(notice, item.GetProperty("id").GetGuid());
        Assert.Equal("open", item.GetProperty("state").GetString());
        Assert.Equal("CVE-2026-0001", item.GetProperty("entry").GetProperty("key").GetString());
        Assert.Equal(9.1, item.GetProperty("cvssScore").GetDouble());
        var named = Assert.Single(item.GetProperty("components").EnumerateArray());
        Assert.Equal(("PostgreSQL", true, "none"), (named.GetProperty("name").GetString(), named.GetProperty("inStack").GetBoolean(), named.GetProperty("versionPrecision").GetString()));
        Assert.Single((await Json(await client.GetAsync($"{path}/notices?component={component}"))).GetProperty("items").EnumerateArray());
        Assert.Empty((await Json(await client.GetAsync($"{path}/notices?state=answered"))).GetProperty("items").EnumerateArray());
        await ExpectProblem(await client.GetAsync($"{path}/notices?state=closed"), HttpStatusCode.BadRequest, "invalid_filter");
        await ExpectProblem(await client.GetAsync($"{path}/notices?cursor=nonsense"), HttpStatusCode.BadRequest, "invalid_filter");
        await ExpectProblem(await client.GetAsync($"{path}/notices/{Guid.CreateVersion7()}"), HttpStatusCode.NotFound, "notice_not_found");

        var answer = $"{path}/notices/{notice}/answer";
        await ExpectProblem(await client.PostAsync(answer, new { answer = "maybe", expectedState = "open" }), HttpStatusCode.BadRequest, "invalid_answer");
        await ExpectProblem(await client.PostAsync(answer, new { answer = "not_affected", expectedState = "open", version = "17.2" }), HttpStatusCode.BadRequest, "invalid_answer");
        var answered = await Json(await client.PostAsync(answer, new { answer = "not_affected", expectedState = "open" }));
        Assert.Equal("not_affected", answered.GetProperty("state").GetString());
        Assert.NotEqual(JsonValueKind.Null, answered.GetProperty("decidedBy").ValueKind);
        await ExpectProblem(await client.PostAsync(answer, new { answer = "affects_us", expectedState = "open" }), HttpStatusCode.Conflict, "status_conflict");

        Assert.Equal("open", (await Json(await client.PostAsync($"{path}/notices/{notice}/reopen", new { expectedState = "not_affected" }))).GetProperty("state").GetString());
        var affects = await Json(await client.PostAsync(answer, new { answer = "affects_us", expectedState = "open" }));
        Assert.Equal("affects_us", affects.GetProperty("state").GetString());
        var number = affects.GetProperty("reminderNumber").GetInt32();
        var reminder = await Json(await client.GetAsync($"{path}/actions/{number}"));
        Assert.Equal(component, reminder.GetProperty("action").GetProperty("component").GetProperty("id").GetGuid());
        Assert.Equal("open", reminder.GetProperty("action").GetProperty("status").GetString());
        Assert.Equal("affects_us", (await Json(await client.GetAsync($"{path}/notices/{notice}"))).GetProperty("state").GetString());
        Assert.Equal(2L, await host.ScalarAsync<long>($"SELECT count(*) FROM platform.audit_event WHERE action = 'notice.answered' AND subject_id = '{notice}'"));
    }

    private static async Task<Guid> SeedAsync(ApiTestHost host, Guid org, Guid component)
    {
        var entry = await host.ScalarAsync<Guid>("""
            WITH s AS (
                INSERT INTO intel.source (key, name, kind, operator, operator_kind, adapter, enabled, interval, config, attribution, terms_mode, parser_version, next_sync_at, consecutive_failures, health)
                VALUES ('test', 'Test', 'record', 'Tests', 'community', 'x', false, interval '1 hour', '{}', 'Test', 'full', 1, now(), 0, 'healthy') RETURNING id),
            e AS (
                INSERT INTO intel.security_entry (kind, primary_key, title, state, changed_at, revision, material_fingerprint, remediation_fingerprint, cvss_score, exploitation, fix_available, cwe_ids, authority_warning, provenance)
                VALUES ('vulnerability', 'CVE-2026-0001', 'PostgreSQL: code execution', 'published', now(), 1, 'm', 'r', 9.1, 'none', true, '{}', false, '{}') RETURNING id),
            a AS (
                INSERT INTO intel.entry_affected (id, entry_id, source_id, product_key, fixed, status, default_status)
                SELECT gen_random_uuid(), e.id, s.id, 'postgresql', '17.7', 'affected', 'unknown' FROM e, s RETURNING entry_id)
            SELECT entry_id FROM a
            """);
        return await host.ScalarAsync<Guid>($"""
            INSERT INTO assess.notice (organization_id, entry_id, entry_key, state, component_ids, cvss_score, raised_at, changed_at)
            VALUES ('{org}', '{entry}', 'CVE-2026-0001', 'open', ARRAY['{component}'::uuid], 9.1, now(), now()) RETURNING id
            """);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.True(status == response.StatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task ExpectProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.True(status == response.StatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
