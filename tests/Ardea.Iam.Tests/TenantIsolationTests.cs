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
        ["platform.instance_warning"] = "global",
        ["platform.audit_event"] = "organization",
        ["iam.organization"] = "organization",
        ["iam.membership"] = "organization",
        ["iam.invitation"] = "organization",
        ["iam.user"] = "user",
        ["iam.user_claim"] = "user",
        ["iam.user_login"] = "user",
        ["iam.user_token"] = "user",
        ["iam.session"] = "user",
        ["notify.notification"] = "user",
        ["notify.user_preference"] = "user",
        ["notify.push_subscription"] = "user",
        ["assess.assessment"] = "organization",
        ["assess.assessment_feedback"] = "organization",
    };

    /// <summary>Schemas whose every table is global: intelligence and catalogue are the same for all (master plan 4.1).</summary>
    private static readonly string[] GlobalSchemas = ["intel", "catalog"];

    /// <summary>Schemas whose every table is organisation data (master plan 4.1).</summary>
    private static readonly string[] OrganizationSchemas = ["assets", "assess", "notify"];

    /// <summary>Bob's objects that Alice tries to reach.</summary>
    private sealed record Foreign(
        Guid User, Guid Invitation, Guid Project, Guid Component, Guid Connection, Guid Link, Guid Suggestion, int Action, int Alert, Guid Finding,
        Guid Rule, Guid Endpoint, Guid Address, Guid Notice);

    /// <summary>A valid rule whose targets may name foreign recipients.</summary>
    private static object Rule(object target, Guid[]? projects = null) =>
        new { name = "Planted", minLevel = 2, events = new[] { "action.raised" }, projectIds = projects ?? [], targets = new[] { target } };

    private static object RoleTarget => new { channel = "in_app", recipientKind = "role", recipientRef = "owner", mode = "immediate" };

    /// <summary>
    /// Every organisation endpoint with a request body that passes validation, so a foreign id is what
    /// the handler meets; bodies may carry foreign ids themselves. A new endpoint fails the test until
    /// it is listed here.
    /// </summary>
    private static readonly Dictionary<string, Func<Foreign, object?>> Bodies = new(StringComparer.Ordinal)
    {
        ["GET /api/v1/orgs/{orgId}/"] = _ => null,
        ["PATCH /api/v1/orgs/{orgId}/"] = _ => new { name = "Taken over" },
        ["GET /api/v1/orgs/{orgId}/members"] = _ => null,
        ["PATCH /api/v1/orgs/{orgId}/members/{userId}"] = _ => new { role = "viewer" },
        ["DELETE /api/v1/orgs/{orgId}/members/{userId}"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/invitations"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/invitations"] = _ => new { email = "intruder@example.org", role = "admin" },
        ["DELETE /api/v1/orgs/{orgId}/invitations/{id}"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/setup"] = _ => null,
        ["PATCH /api/v1/orgs/{orgId}/setup"] = f => new { stepDone = "project", firstProjectId = f.Project },
        ["POST /api/v1/orgs/{orgId}/setup/complete"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/audit"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/projects"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/profile/coverage"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/projects"] = _ => new { name = "Planted" },
        ["GET /api/v1/orgs/{orgId}/projects/{projectId}"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/stack"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/stack/suggestions"] = _ => null,
        ["PATCH /api/v1/orgs/{orgId}/projects/{projectId}"] = _ => new { name = "Taken over", exposure = "internet" },
        ["DELETE /api/v1/orgs/{orgId}/projects/{projectId}"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/projects/{projectId}/components"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/projects/{projectId}/components"] = f => new { productKey = "php", parentId = f.Component },
        ["PUT /api/v1/orgs/{orgId}/projects/{projectId}/components"] = f => new { components = new object[] { new { id = f.Component, productKey = "php" } } },
        ["PATCH /api/v1/orgs/{orgId}/projects/{projectId}/components/{id}"] = f => new { version = "0.0.1", parentId = f.Component },
        ["DELETE /api/v1/orgs/{orgId}/projects/{projectId}/components/{id}"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/projects/{projectId}/components/{id}/detach"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/repository-connections"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/repository-connections"] = _ => new { name = "Planted" },
        ["PATCH /api/v1/orgs/{orgId}/repository-connections/{id}"] = _ => new { name = "Taken over" },
        ["DELETE /api/v1/orgs/{orgId}/repository-connections/{id}"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/repository-connections/{id}/test"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/repository-connections/{id}/repositories"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/projects/{projectId}/repositories"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/projects/{projectId}/repositories"] = f => new { connectionId = f.Connection, owner = "acme", name = "planted" },
        ["PATCH /api/v1/orgs/{orgId}/projects/{projectId}/repositories/{linkId}"] = _ => new { @ref = "main" },
        ["DELETE /api/v1/orgs/{orgId}/projects/{projectId}/repositories/{linkId}"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/projects/{projectId}/repositories/{linkId}/sync"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/projects/{projectId}/repositories/{linkId}/confirm-shrink"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/projects/{projectId}/suggestions"] = _ => null,
        ["PATCH /api/v1/orgs/{orgId}/projects/{projectId}/suggestions/{id}"] = _ => new { decision = "accept" },
        ["GET /api/v1/orgs/{orgId}/overview"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/events"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/actions"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/actions/{number}"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/actions/{number}/status"] = _ => new { status = "risk_accepted", expectedStatus = "open" },
        ["GET /api/v1/orgs/{orgId}/actions/{number}/events"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/actions/{number}/notes"] = _ => new { text = "Planted" },
        ["GET /api/v1/orgs/{orgId}/findings"] = _ => null,
        ["PATCH /api/v1/orgs/{orgId}/findings/{findingId}"] = _ => new { status = "risk_accepted", expectedStatus = "open" },
        ["POST /api/v1/orgs/{orgId}/findings/{findingId}/review"] = _ => new { decision = "correct", expectedVerdict = "affected", expectedSource = "rules", verdict = "unlikely", reason = "Planted" },
        ["GET /api/v1/orgs/{orgId}/alerts"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/alerts/{number}"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/notices"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/notices/{noticeId}"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/notices/{noticeId}/answer"] = f => new { answer = "affects_us", expectedState = "open", projectId = f.Project, version = "6.7.2" },
        ["POST /api/v1/orgs/{orgId}/notices/{noticeId}/reopen"] = _ => new { expectedState = "not_affected" },
        ["GET /api/v1/orgs/{orgId}/notification-channels"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/rules/"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/rules/"] = f => Rule(new { channel = "email", recipientKind = "user", recipientRef = f.User.ToString(), mode = "immediate" }, [f.Project]),
        ["GET /api/v1/orgs/{orgId}/rules/{ruleId}"] = _ => null,
        ["PUT /api/v1/orgs/{orgId}/rules/{ruleId}"] = _ => Rule(RoleTarget),
        ["DELETE /api/v1/orgs/{orgId}/rules/{ruleId}"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/endpoints/"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/endpoints/"] = _ => new { name = "Planted", url = "https://hooks.example.org/ardea" },
        ["PUT /api/v1/orgs/{orgId}/endpoints/{id}"] = _ => new { name = "Taken over" },
        ["DELETE /api/v1/orgs/{orgId}/endpoints/{id}"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/endpoints/{id}/test"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/external-addresses/"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/external-addresses/"] = _ => new { address = "planted@example.org" },
        ["POST /api/v1/orgs/{orgId}/external-addresses/{id}/resend"] = _ => null,
        ["DELETE /api/v1/orgs/{orgId}/external-addresses/{id}"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/deliveries"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/notifications"] = _ => null,
        ["GET /api/v1/orgs/{orgId}/notifications/unread-count"] = _ => null,
        ["POST /api/v1/orgs/{orgId}/notifications/read"] = _ => new { all = true },
        ["GET /api/v1/orgs/{orgId}/notification-preferences"] = _ => null,
        ["PUT /api/v1/orgs/{orgId}/notification-preferences"] = _ => new { mutedChannels = new[] { "email" } },
        ["GET /api/v1/orgs/{orgId}/reminder-rhythm/"] = _ => null,
        ["PUT /api/v1/orgs/{orgId}/reminder-rhythm/"] = _ => new { level5 = "off", level4 = "off", level3 = "off" },
        ["POST /api/v1/orgs/{orgId}/alerts/{number}/assessment"] = _ => null,
    };

    /// <summary>Bob's object a route parameter names: <c>{id}</c> is the object of the path segment before it.</summary>
    private static string? ForeignId(string route, string parameter, Foreign foreign) => parameter switch
    {
        "number" when route.Contains("/actions/{number}", StringComparison.Ordinal) => foreign.Action.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "number" when route.Contains("/alerts/{number}", StringComparison.Ordinal) => foreign.Alert.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "findingId" => foreign.Finding.ToString(),
        "noticeId" => foreign.Notice.ToString(),
        "userId" => foreign.User.ToString(),
        "projectId" => foreign.Project.ToString(),
        "id" when route.Contains("/components/{id}", StringComparison.Ordinal) => foreign.Component.ToString(),
        "id" when route.Contains("/invitations/{id}", StringComparison.Ordinal) => foreign.Invitation.ToString(),
        "id" when route.Contains("/repository-connections/{id}", StringComparison.Ordinal) => foreign.Connection.ToString(),
        "id" when route.Contains("/suggestions/{id}", StringComparison.Ordinal) => foreign.Suggestion.ToString(),
        "linkId" => foreign.Link.ToString(),
        "ruleId" => foreign.Rule.ToString(),
        "id" when route.Contains("/endpoints/{id}", StringComparison.Ordinal) => foreign.Endpoint.ToString(),
        "id" when route.Contains("/external-addresses/{id}", StringComparison.Ordinal) => foreign.Address.ToString(),
        _ => null,
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
            var schema = table.Split('.')[0];
            var kind = TableKinds.GetValueOrDefault(table)
                ?? (GlobalSchemas.Contains(schema) ? "global" : OrganizationSchemas.Contains(schema) ? "organization" : null);
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
        var bobsProject = (await Json(await bob.PostAsync($"/api/v1/orgs/{bobOrg}/projects", new { name = "Bob's shop" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var bobsComponent = (await Json(await bob.PostAsync($"/api/v1/orgs/{bobOrg}/projects/{bobsProject}/components", new { productKey = "wordpress", version = "6.7.1" }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var aliceProject = (await Json(await alice.PostAsync($"/api/v1/orgs/{aliceOrg}/projects", new { name = "Alice's shop" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var bobsConnection = (await Json(await bob.PostAsync($"/api/v1/orgs/{bobOrg}/repository-connections", new { name = "Bob's GitHub" }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var bobsLink = (await Json(await bob.PostAsync($"/api/v1/orgs/{bobOrg}/projects/{bobsProject}/repositories", new { connectionId = bobsConnection, owner = "bob", name = "shop" }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var bobsSuggestion = await host.ScalarAsync<Guid>($"""
            INSERT INTO assets.component_suggestion (organization_id, project_id, repository_link_id, product_key, version, evidence, fingerprint, state, created_at)
            VALUES ('{bobOrg}', '{bobsProject}', '{bobsLink}', 'nginx', '1.27', '[]', 'f', 'open', now()) RETURNING id
            """);
        // An action of Bob's with its alert and finding, as the assessment writes them (task 3.5).
        var bobsEntry = await host.ScalarAsync<Guid>("""
            INSERT INTO intel.security_entry (kind, primary_key, state, changed_at, revision, material_fingerprint, remediation_fingerprint, exploitation, fix_available, cwe_ids, authority_warning, provenance)
            VALUES ('vulnerability', 'CVE-2026-0001', 'published', now(), 1, 'm', 'r', 'none', false, '{}', false, '{}') RETURNING id
            """);
        var bobsAlert = await host.ScalarAsync<Guid>($"""
            INSERT INTO assess.alert (organization_id, entry_id, entry_key, number, level, assessed_entry_revision, created_at, changed_at)
            VALUES ('{bobOrg}', '{bobsEntry}', 'CVE-2026-0001', 1, 4, 1, now(), now()) RETURNING id
            """);
        var bobsAction = await host.ScalarAsync<Guid>($"""
            INSERT INTO assess.remediation (organization_id, project_id, component_id, number, level, open_findings, exploitation, fix_available, status, opened_at, is_backfill, changed_at)
            VALUES ('{bobOrg}', '{bobsProject}', '{bobsComponent}', 2, 4, 1, 'none', false, 'open', now(), false, now()) RETURNING id
            """);
        var bobsFinding = await host.ScalarAsync<Guid>($"""
            INSERT INTO assess.finding (organization_id, alert_id, entry_id, project_id, component_id, remediation_id, match_type, version_verdict, verdict, verdict_source, level, evidence, factors, status, status_changed_at, is_backfill, created_at, changed_at, exploitation, fix_available)
            VALUES ('{bobOrg}', '{bobsAlert}', '{bobsEntry}', '{bobsProject}', '{bobsComponent}', '{bobsAction}', 'product', 'in_range', 'affected', 'rules', 4, jsonb_build_object(), jsonb_build_object(), 'open', now(), false, now(), now(), 'none', false) RETURNING id
            """);
        // A notice of Bob's for his WordPress (task 5a.3), as the assessment writes it.
        var bobsNotice = await host.ScalarAsync<Guid>($"""
            INSERT INTO assess.notice (organization_id, entry_id, entry_key, state, component_ids, raised_at, changed_at)
            VALUES ('{bobOrg}', '{bobsEntry}', 'CVE-2026-0001', 'open', ARRAY['{bobsComponent}'::uuid], now(), now()) RETURNING id
            """);
        // Bob's notification settings and recipients (task 5.1): his default rule, a webhook target, an external address, a notification.
        var bobsRule = await host.ScalarAsync<Guid>($"SELECT id FROM notify.rule WHERE organization_id = '{bobOrg}' AND preset = 'critical'");
        var bobsEndpoint = await host.ScalarAsync<Guid>($"""
            INSERT INTO notify.endpoint (organization_id, name, url_enc, secret_enc, enabled, created_at) VALUES ('{bobOrg}', 'Bob''s SIEM', 'x', 'x', true, now()) RETURNING id
            """);
        var bobsAddress = await host.ScalarAsync<Guid>($"""
            INSERT INTO notify.external_address (organization_id, address, normalized_address, confirmed_at, created_at)
            VALUES ('{bobOrg}', 'partner@example.org', 'PARTNER@EXAMPLE.ORG', now(), now()) RETURNING id
            """);
        var bobsNotification = await host.ScalarAsync<Guid>($"""
            INSERT INTO notify.notification (user_id, organization_id, event_id, kind, title, body, created_at)
            VALUES ('{bobId}', '{bobOrg}', gen_random_uuid(), 'action.raised', 'Bob''s', 'Bob''s', now()) RETURNING id
            """);
        var foreign = new Foreign(bobId, bobsInvitation, bobsProject, bobsComponent, bobsConnection, bobsLink, bobsSuggestion, 2, 1, bobsFinding, bobsRule, bobsEndpoint, bobsAddress, bobsNotice);

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
            if (!Bodies.TryGetValue(key, out var bodyOf))
            {
                problems.Add($"{key}: not covered; add it to Bodies");
                continue;
            }

            var body = bodyOf(foreign);
            var parameters = System.Text.RegularExpressions.Regex.Matches(route, @"\{(\w+)\}").Select(m => m.Groups[1].Value).Where(p => p != "orgId").ToList();
            var missing = parameters.Where(p => ForeignId(route, p, foreign) is null).ToList();
            if (missing.Count > 0)
            {
                problems.Add($"{key}: no foreign object for {string.Join(", ", missing)}");
                continue;
            }

            var bobs = parameters.ToDictionary(p => p, p => ForeignId(route, p, foreign)!);

            // 1. Bob's organisation in the path: it does not exist for Alice.
            await ExpectHiddenAsync(problems, $"{key} with Bob's organisation", await alice.SendAsync(new HttpMethod(method), Fill(route, bobOrg, bobs), body), notFoundOnly: true);

            // 2. Alice's own organisation with ids of Bob's objects: not found either.
            if (parameters.Count > 0)
            {
                await ExpectHiddenAsync(problems, $"{key} with Bob's objects", await alice.SendAsync(new HttpMethod(method), Fill(route, aliceOrg, bobs), body), notFoundOnly: true);
            }

            // 3. Alice's own project with Bob's other objects, in the path or in the body.
            if (route.Contains("{projectId}", StringComparison.Ordinal))
            {
                var mixed = new Dictionary<string, string>(bobs) { ["projectId"] = aliceProject.ToString() };
                var foreignInBody = body is not null && (JsonSerializer.Serialize(body).Contains(foreign.Component.ToString(), StringComparison.Ordinal)
                    || JsonSerializer.Serialize(body).Contains(foreign.Connection.ToString(), StringComparison.Ordinal));
                var response = await alice.SendAsync(new HttpMethod(method), Fill(route, aliceOrg, mixed), body);
                if (parameters.Count > 1 || foreignInBody)
                {
                    // A foreign id in the body is a validation error naming it as not found; never a success.
                    await ExpectHiddenAsync(problems, $"{key} in Alice's project", response, notFoundOnly: parameters.Count > 1);
                }
            }
        }

        // Foreign ids in the body of Alice's own requests: a rule naming Bob's project or Bob's recipients is invalid,
        // and marking Bob's notification read changes nothing.
        foreach (var body in new[]
        {
            Rule(RoleTarget, [foreign.Project]),
            Rule(new { channel = "email", recipientKind = "user", recipientRef = foreign.User.ToString(), mode = "immediate" }),
            Rule(new { channel = "webhook", recipientKind = "endpoint", recipientRef = foreign.Endpoint.ToString(), mode = "immediate" }),
            Rule(new { channel = "email", recipientKind = "address", recipientRef = foreign.Address.ToString(), mode = "immediate" }),
        })
        {
            await ExpectHiddenAsync(problems, $"POST rules with Bob's ids {JsonSerializer.Serialize(body)}", await alice.PostAsync($"/api/v1/orgs/{aliceOrg}/rules", body), notFoundOnly: false);
        }

        // Alice answers her own notice «affects us» naming Bob's project: not found, and nothing lands in Bob's profile.
        var alicesComponent = (await Json(await alice.PostAsync($"/api/v1/orgs/{aliceOrg}/projects/{aliceProject}/components", new { productKey = "wordpress" }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var alicesNotice = await host.ScalarAsync<Guid>($"""
            INSERT INTO assess.notice (organization_id, entry_id, entry_key, state, component_ids, raised_at, changed_at)
            VALUES ('{aliceOrg}', '{bobsEntry}', 'CVE-2026-0001', 'open', ARRAY['{alicesComponent}'::uuid], now(), now()) RETURNING id
            """);
        await ExpectHiddenAsync(problems, "POST answer with Bob's project", await alice.PostAsync(
            $"/api/v1/orgs/{aliceOrg}/notices/{alicesNotice}/answer", new { answer = "affects_us", expectedState = "open", projectId = bobsProject, version = "6.7.2" }), notFoundOnly: true);

        await alice.PostAsync($"/api/v1/orgs/{aliceOrg}/notifications/read", new { ids = new[] { bobsNotification } });
        Assert.Empty(problems);

        // Bob's data is untouched.
        Assert.Equal(3L, await host.ScalarAsync<long>($"SELECT count(*) FROM notify.rule WHERE organization_id = '{bobOrg}'"));
        Assert.Equal(0L, await host.ScalarAsync<long>($"SELECT count(*) FROM notify.rule WHERE name = 'Planted'"));
        Assert.Equal("Bob's SIEM", await host.ScalarAsync<string>($"SELECT name FROM notify.endpoint WHERE id = '{bobsEndpoint}'"));
        Assert.Equal(1L, await host.ScalarAsync<long>($"SELECT count(*) FROM notify.external_address WHERE id = '{bobsAddress}' AND unsubscribed_at IS NULL"));
        Assert.Equal(1L, await host.ScalarAsync<long>($"SELECT count(*) FROM notify.notification WHERE id = '{bobsNotification}' AND read_at IS NULL"));
        Assert.Equal(0L, await host.ScalarAsync<long>($"SELECT count(*) FROM notify.user_preference WHERE organization_id = '{bobOrg}'"));
        Assert.Equal(0L, await host.ScalarAsync<long>($"SELECT count(*) FROM notify.reminder_schedule WHERE organization_id = '{bobOrg}'"));
        Assert.Equal(1L, await host.ScalarAsync<long>($"SELECT count(*) FROM iam.invitation WHERE organization_id = '{bobOrg}'"));
        Assert.Equal("Bob Corp", await host.ScalarAsync<string>($"SELECT name FROM iam.organization WHERE id = '{bobOrg}'"));
        Assert.Equal(0L, await host.ScalarAsync<long>($"SELECT count(*) FROM iam.organization WHERE id = '{bobOrg}' AND settings->'setup' IS NOT NULL AND settings->'setup' <> 'null'::jsonb"));
        Assert.Equal("owner", await host.ScalarAsync<string>($"SELECT role FROM iam.membership WHERE organization_id = '{bobOrg}'"));
        Assert.Equal("Bob's shop|internal|", await host.ScalarAsync<string>($"SELECT name || '|' || exposure || '|' || coalesce(archived_at::text, '') FROM assets.project WHERE id = '{bobsProject}'"));
        Assert.Equal("6.7.1|manual", await host.ScalarAsync<string>($"SELECT version || '|' || origin FROM assets.component WHERE id = '{bobsComponent}'"));
        Assert.Equal(1L, await host.ScalarAsync<long>($"SELECT count(*) FROM assets.component WHERE organization_id = '{bobOrg}'"));
        Assert.Equal("open|", await host.ScalarAsync<string>($"SELECT state || '|' || coalesce(decided_by::text, '') FROM assess.notice WHERE id = '{bobsNotice}'"));
        Assert.Equal("Bob's GitHub", await host.ScalarAsync<string>($"SELECT name FROM assets.repository_connection WHERE id = '{bobsConnection}'"));
        Assert.Equal("open", await host.ScalarAsync<string>($"SELECT state FROM assets.component_suggestion WHERE id = '{bobsSuggestion}'"));
        Assert.Equal(1L, await host.ScalarAsync<long>($"SELECT count(*) FROM assets.repository_link WHERE organization_id = '{bobOrg}'"));
        Assert.Equal("open|open", await host.ScalarAsync<string>(
            $"SELECT r.status || '|' || f.status FROM assess.remediation r JOIN assess.finding f ON f.remediation_id = r.id WHERE r.id = '{bobsAction}'"));
        Assert.Equal(1L, await host.ScalarAsync<long>($"SELECT count(*) FROM assets.project WHERE organization_id = '{bobOrg}' AND kind = 'organization_stack'"));
        Assert.Equal(0L, await host.ScalarAsync<long>($"SELECT count(*) FROM assess.assess_event"));
        Assert.Equal(0L, await host.ScalarAsync<long>($"SELECT count(*) FROM assess.assessment_feedback"));
        Assert.Equal("affected|rules", await host.ScalarAsync<string>($"SELECT verdict || '|' || verdict_source FROM assess.finding WHERE id = '{bobsFinding}'"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'assess.estimate-alert'"));
    }

    private static async Task ExpectHiddenAsync(List<string> problems, string what, HttpResponseMessage response, bool notFoundOnly)
    {
        var allowed = notFoundOnly ? response.StatusCode == HttpStatusCode.NotFound : response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest;
        if (!allowed)
        {
            problems.Add($"{what}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
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
        foreach (var table in new[] { "iam.organization", "iam.membership", "iam.invitation", "platform.audit_event", "iam.\"user\"", "iam.session", "iam.user_token", "notify.rule", "notify.rule_target" })
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
    public async Task Every_system_function_is_listed_in_adr_029_owned_by_the_migrator_and_pins_its_search_path()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        var adr = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "docs", "adr", "0029-row-level-security-as-hardening.md"), TestContext.Current.CancellationToken);
        var listed = System.Text.RegularExpressions.Regex.Matches(adr, @"^\s*\| `(\w+\.\w+)\(", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(listed);

        await using var connection = new NpgsqlConnection(host.Database.OwnerConnectionString("tests"));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT n.nspname || '.' || p.proname, pg_get_userbyid(p.proowner), coalesce(array_to_string(p.proconfig, ','), '')
            FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE p.prosecdef AND n.nspname NOT IN ('pg_catalog', 'information_schema')
            ORDER BY 1
            """, connection);
        var problems = new List<string>();
        var found = new HashSet<string>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                var (function, owner, settings) = (reader.GetString(0), reader.GetString(1), reader.GetString(2));
                found.Add(function);
                if (!listed.Contains(function))
                {
                    problems.Add($"{function}: SECURITY DEFINER but not in the list of ADR 029");
                }

                if (owner != host.Database.OwnerUser)
                {
                    problems.Add($"{function}: owned by {owner}, not by the migrator");
                }

                if (!settings.Contains("search_path=", StringComparison.Ordinal))
                {
                    problems.Add($"{function}: no fixed search_path, so a caller's schema could shadow what it calls");
                }
            }
        }

        problems.AddRange(listed.Except(found).Select(f => $"{f}: listed in ADR 029 but not a SECURITY DEFINER function"));
        Assert.Empty(problems);
    }

    [Fact]
    public async Task A_session_of_another_account_cannot_be_revoked()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var alice = await SignedInAsync(host, "alice@example.org", setup: true);
        using var bob = await SignedInAsync(host, "bob@example.org", setup: false);
        var bobsSession = await host.ScalarAsync<Guid>(
            "SELECT s.id FROM iam.session s JOIN iam.\"user\" u ON u.id = s.user_id WHERE u.normalized_email = 'BOB@EXAMPLE.ORG'");

        var response = await alice.SendAsync(HttpMethod.Delete, $"/api/v1/account/sessions/{bobsSession}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.SessionAsync()).StatusCode);
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

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ardea.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("No Ardea.slnx above the test binaries.");
    }

    private static string Normalize(string raw) =>
        System.Text.RegularExpressions.Regex.Replace(raw.StartsWith('/') ? raw : "/" + raw, @"\{(\w+)(:[^}]*)?\}", "{$1}");

    private static string Fill(string route, Guid organization, IReadOnlyDictionary<string, string> foreign)
    {
        var path = route.Replace("{orgId}", organization.ToString(), StringComparison.Ordinal);
        foreach (var (name, id) in foreign)
        {
            path = path.Replace("{" + name + "}", id, StringComparison.Ordinal);
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
