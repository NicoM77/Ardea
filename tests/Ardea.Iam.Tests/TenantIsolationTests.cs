using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ardea.Iam.Tests;

/// <summary>
/// Tenant separation (task 2.2; master plan 4.1, 9.2): every table has one kind with its rule in the
/// database, and no organisation endpoint leads to another organisation's data, neither through the
/// organisation in the path nor through ids of its objects.
/// </summary>
public sealed class TenantIsolationTests(PostgresServer server)
{
    /// <summary>The three kinds of master plan 4.1; a new table must be added to exactly one.</summary>
    private static readonly Dictionary<string, string> TableKinds = new(StringComparer.Ordinal)
    {
        ["platform.job"] = "global",
        ["platform.schedule"] = "global",
        ["platform.setting"] = "global",
        ["platform.data_protection_key"] = "global",
        ["platform.worker_heartbeat"] = "global",
        ["platform.audit_event"] = "organization",
        ["iam.organization"] = "organization",
        ["iam.membership"] = "organization",
        ["iam.invitation"] = "organization",
        ["iam.user"] = "user",
        ["iam.user_claim"] = "user",
        ["iam.user_login"] = "user",
        ["iam.user_token"] = "user",
        ["iam.session"] = "user",
    };

    /// <summary>Schemas whose every table is global: intelligence and catalogue are the same for all (master plan 4.1).</summary>
    private static readonly string[] GlobalSchemas = ["intel", "catalog"];

    /// <summary>
    /// Every organisation endpoint with a request body that passes validation, so a foreign id is what
    /// the handler meets. A new endpoint fails the test until it is listed here.
    /// </summary>
    private static readonly Dictionary<string, object?> Bodies = new(StringComparer.Ordinal)
    {
        ["GET /api/v1/orgs/{orgId}/"] = null,
        ["PATCH /api/v1/orgs/{orgId}/"] = new { name = "Taken over" },
        ["GET /api/v1/orgs/{orgId}/members"] = null,
        ["PATCH /api/v1/orgs/{orgId}/members/{userId}"] = new { role = "viewer" },
        ["DELETE /api/v1/orgs/{orgId}/members/{userId}"] = null,
        ["GET /api/v1/orgs/{orgId}/invitations"] = null,
        ["POST /api/v1/orgs/{orgId}/invitations"] = new { email = "intruder@example.org", role = "admin" },
        ["DELETE /api/v1/orgs/{orgId}/invitations/{id}"] = null,
    };

    [Fact]
    public async Task Every_table_has_a_kind_and_organisation_tables_are_guarded_by_the_database()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        await using var connection = new NpgsqlConnection(host.Database.OwnerConnectionString("tests"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT n.nspname || '.' || c.relname, c.relrowsecurity,
                   (SELECT count(*) FROM pg_policy p WHERE p.polrelid = c.oid),
                   EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'organization_id' AND NOT a.attisdropped)
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p') AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'public') AND n.nspname NOT LIKE 'pg_%'
              AND c.relname <> '__ef_migrations_history'
            ORDER BY 1
            """, connection);
        var problems = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var (table, rls, policies, hasOrganization) = (reader.GetString(0), reader.GetBoolean(1), reader.GetInt64(2), reader.GetBoolean(3));
            var kind = TableKinds.GetValueOrDefault(table) ?? (GlobalSchemas.Contains(table.Split('.')[0]) ? "global" : null);
            if (kind is null)
            {
                problems.Add($"{table}: no kind; add it to TableKinds (master plan 4.1)");
            }
            else if (kind is "organization" or "user" && (!rls || policies == 0))
            {
                problems.Add($"{table}: {kind} data without row-level security and a policy (ADR 029)");
            }
            else if (kind == "organization" && !hasOrganization && table != "iam.organization")
            {
                problems.Add($"{table}: organisation data without organization_id");
            }
            else if (kind == "global" && rls)
            {
                problems.Add($"{table}: global data must not depend on a tenant");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public async Task No_organisation_endpoint_reaches_another_organisation()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var alice = await SignedInAsync(host, "alice@example.org", setup: true);
        using var bob = await SignedInAsync(host, "bob@example.org", setup: false);
        var aliceOrg = await CreateOrgAsync(alice, "Alice Corp");
        var bobOrg = await CreateOrgAsync(bob, "Bob Corp");
        var bobsInvitation = (await Json(await bob.PostAsync($"/api/v1/orgs/{bobOrg}/invitations", new { email = "carol@example.org", role = "member" }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var bobId = await host.ScalarAsync<Guid>("SELECT id FROM iam.\"user\" WHERE normalized_email = 'BOB@EXAMPLE.ORG'");

        // Objects of Bob's organisation, by the name of the route parameter that carries them.
        var foreign = new Dictionary<string, Guid> { ["userId"] = bobId, ["id"] = bobsInvitation };

        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("/orgs/{orgId", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (Method: m, Route: Normalize(e.RoutePattern.RawText!))))
            .Distinct()
            .ToList();
        Assert.NotEmpty(endpoints);

        var problems = new List<string>();
        foreach (var (method, route) in endpoints)
        {
            var key = $"{method} {route}";
            if (!Bodies.TryGetValue(key, out var body))
            {
                problems.Add($"{key}: not covered; add it to Bodies");
                continue;
            }

            var parameters = System.Text.RegularExpressions.Regex.Matches(route, @"\{(\w+)\}").Select(m => m.Groups[1].Value).Where(p => p != "orgId").ToList();
            var missing = parameters.Where(p => !foreign.ContainsKey(p)).ToList();
            if (missing.Count > 0)
            {
                problems.Add($"{key}: no foreign object for {string.Join(", ", missing)}");
                continue;
            }

            // 1. Bob's organisation in the path: it does not exist for Alice.
            var path = Fill(route, bobOrg, foreign);
            var response = await alice.SendAsync(new HttpMethod(method), path, body);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                problems.Add($"{key} with Bob's organisation: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            }

            // 2. Alice's own organisation with ids of Bob's objects: not found either.
            if (parameters.Count > 0)
            {
                response = await alice.SendAsync(new HttpMethod(method), Fill(route, aliceOrg, foreign), body);
                if (response.StatusCode != HttpStatusCode.NotFound)
                {
                    problems.Add($"{key} with Bob's objects: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                }
            }
        }

        Assert.Empty(problems);

        // Bob's data is untouched.
        Assert.Equal(1L, await host.ScalarAsync<long>($"SELECT count(*) FROM iam.invitation WHERE organization_id = '{bobOrg}'"));
        Assert.Equal("Bob Corp", await host.ScalarAsync<string>($"SELECT name FROM iam.organization WHERE id = '{bobOrg}'"));
        Assert.Equal("owner", await host.ScalarAsync<string>($"SELECT role FROM iam.membership WHERE organization_id = '{bobOrg}'"));
    }

    [Fact]
    public async Task Without_a_tenant_the_application_role_sees_no_organisation_or_user_rows()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var alice = await SignedInAsync(host, "alice@example.org", setup: true);
        await CreateOrgAsync(alice, "Alice Corp");
        await alice.PostAsync($"/api/v1/orgs/{await host.ScalarAsync<Guid>("SELECT id FROM iam.organization")}/invitations", new { email = "x@example.org", role = "viewer" });

        // Plain SQL as the application role, with no tenant set: the third layer hides everything.
        await using var dataSource = NpgsqlDataSource.Create(host.Database.AppConnectionString("tests"));
        foreach (var table in new[] { "iam.organization", "iam.membership", "iam.invitation", "platform.audit_event", "iam.\"user\"", "iam.session", "iam.user_token" })
        {
            await using var command = dataSource.CreateCommand($"SELECT count(*) FROM {table}");
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task A_user_context_sees_only_its_own_account_and_sessions()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var alice = await SignedInAsync(host, "alice@example.org", setup: true);
        using var bob = await SignedInAsync(host, "bob@example.org", setup: false);
        var aliceId = await host.ScalarAsync<Guid>("SELECT id FROM iam.\"user\" WHERE normalized_email = 'ALICE@EXAMPLE.ORG'");

        await using var dataSource = NpgsqlDataSource.Create(host.Database.AppConnectionString("tests"));
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand($"SELECT set_config('ardea.user_id', '{aliceId}', true)", connection, transaction))
        {
            await set.ExecuteNonQueryAsync();
        }

        await using var users = new NpgsqlCommand("SELECT string_agg(email, ',') FROM iam.\"user\"", connection, transaction);
        Assert.Equal("alice@example.org", await users.ExecuteScalarAsync());
        await using var sessions = new NpgsqlCommand($"SELECT count(*) FILTER (WHERE user_id <> '{aliceId}') FROM iam.session", connection, transaction);
        Assert.Equal(0L, await sessions.ExecuteScalarAsync());

        // Writing another account is refused by the policy.
        await using var write = new NpgsqlCommand("UPDATE iam.\"user\" SET display_name = 'x' WHERE normalized_email = 'BOB@EXAMPLE.ORG'", connection, transaction);
        Assert.Equal(0, await write.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Signing_in_as_another_account_in_a_signed_in_browser_is_refused()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var alice = await SignedInAsync(host, "alice@example.org", setup: true);
        using var bob = await SignedInAsync(host, "bob@example.org", setup: false);

        var response = await alice.PostAsync("/api/v1/auth/login", new { email = "bob@example.org", password = ApiTestHost.Password });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("signed_in", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    private static string Normalize(string raw) =>
        System.Text.RegularExpressions.Regex.Replace(raw.StartsWith('/') ? raw : "/" + raw, @"\{(\w+)(:[^}]*)?\}", "{$1}");

    private static string Fill(string route, Guid organization, Dictionary<string, Guid> foreign)
    {
        var path = route.Replace("{orgId}", organization.ToString(), StringComparison.Ordinal);
        foreach (var (name, id) in foreign)
        {
            path = path.Replace("{" + name + "}", id.ToString(), StringComparison.Ordinal);
        }

        return path;
    }

    private static async Task<ApiClient> SignedInAsync(ApiTestHost host, string email, bool setup)
    {
        var client = host.Client();
        var response = setup
            ? await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email, displayName = email, password = ApiTestHost.Password })
            : await client.PostAsync("/api/v1/auth/register", new { email, displayName = email, password = ApiTestHost.Password });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }

    private static async Task<Guid> CreateOrgAsync(ApiClient client, string name) =>
        (await Json(await client.PostAsync("/api/v1/orgs", new { name }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
