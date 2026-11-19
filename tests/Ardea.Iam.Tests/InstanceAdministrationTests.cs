using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Iam.Tests;

/// <summary>Task 0.13: the first administration, for the instance administrator only.</summary>
public sealed class InstanceAdministrationTests(PostgresServer server)
{
    private const string AdminEmail = "admin@example.org";

    [Fact]
    public async Task Only_the_instance_administrator_reaches_the_administration()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var admin = await AdminAsync(host);
        using var member = host.Client();
        await member.PostAsync("/api/v1/auth/register", new { email = "member@example.org", displayName = "Member", password = ApiTestHost.Password });
        using var stranger = host.Client();

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/instance/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/instance/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/v1/instance/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync("/api/v1/instance/jobs/" + Guid.CreateVersion7() + "/retry")).StatusCode);
    }

    [Fact]
    public async Task The_administrator_sees_the_ai_providers_without_their_keys_and_can_test_one()
    {
        await using var host = await ApiTestHost.StartAsync(server, new()
        {
            ["AI:PROVIDERS:local:KIND"] = "ollama",
            ["AI:PROVIDERS:local:ENDPOINT"] = "http://127.0.0.1:9",
            ["AI:PROVIDERS:local:MODEL"] = "qwen3:14b",
            ["AI:PROVIDERS:local:API_KEY"] = "sk-never-shown",
        });
        using var admin = await AdminAsync(host);

        var list = await admin.GetAsync("/api/v1/instance/ai/providers");
        var text = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sk-never-shown", text, StringComparison.Ordinal);
        var provider = Assert.Single((await list.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        Assert.Equal(("local", "ollama", "qwen3:14b", true, "127.0.0.1"), (
            provider.GetProperty("name").GetString(), provider.GetProperty("kind").GetString(), provider.GetProperty("model").GetString(),
            provider.GetProperty("local").GetBoolean(), provider.GetProperty("host").GetString()));

        // Nothing listens on the discard port: the test fails, says so in words, and is audited.
        var test = await (await admin.PostAsync("/api/v1/instance/ai/providers/local/test")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(test.GetProperty("ok").GetBoolean());
        Assert.Equal("The provider could not be reached.", test.GetProperty("error").GetString());
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.audit_event WHERE action = 'instance.ai_provider_tested'"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/v1/instance/ai/providers/other/test")).StatusCode);
    }

    [Fact]
    public async Task Registration_mode_set_here_takes_effect_unless_an_environment_variable_locks_it()
    {
        await using var host = await ApiTestHost.StartAsync(server, new() { ["REGISTRATION"] = null });
        using var admin = await AdminAsync(host);
        using var visitor = host.Client();

        var before = await visitor.PostAsync("/api/v1/auth/register", new { email = "a@example.org", displayName = "A", password = ApiTestHost.Password });
        Assert.Equal(HttpStatusCode.BadRequest, before.StatusCode); // invite is the default

        var update = await admin.SendAsync(HttpMethod.Patch, "/api/v1/instance/settings", new { registration = "open" });
        Assert.Equal("open", (await update.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("registration").GetProperty("value").GetString());
        var after = await visitor.PostAsync("/api/v1/auth/register", new { email = "a@example.org", displayName = "A", password = ApiTestHost.Password });
        Assert.Equal(HttpStatusCode.NoContent, after.StatusCode);

        await using var locked = await ApiTestHost.StartAsync(server, new() { ["REGISTRATION"] = "closed" });
        using var lockedAdmin = await AdminAsync(locked);
        var settings = await (await lockedAdmin.GetAsync("/api/v1/instance/settings")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(settings.GetProperty("registration").GetProperty("locked").GetBoolean());
        var refused = await lockedAdmin.SendAsync(HttpMethod.Patch, "/api/v1/instance/settings", new { registration = "open" });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task Smtp_password_is_stored_but_never_shown()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var admin = await AdminAsync(host);

        var update = await admin.SendAsync(HttpMethod.Patch, "/api/v1/instance/settings", new { smtpHost = "mail.example.org", smtpFrom = "ardea@example.org", smtpPassword = "smtp-secret-123" });

        var body = await update.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.DoesNotContain("smtp-secret-123", body, StringComparison.Ordinal);
        var password = JsonDocument.Parse(body).RootElement.GetProperty("smtp").GetProperty("password");
        Assert.True(password.GetProperty("isSet").GetBoolean());
        Assert.Equal(JsonValueKind.Null, password.GetProperty("value").ValueKind);
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.audit_event WHERE data::text LIKE '%smtp-secret%'"));
    }

    [Fact]
    public async Task Test_mail_without_smtp_is_refused_with_a_clear_code()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var admin = await AdminAsync(host);

        var response = await admin.PostAsync("/api/v1/instance/smtp/test", new { to = AdminEmail });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("smtp_not_configured", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Abandoned_job_is_listed_and_can_be_retried()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var admin = await AdminAsync(host);
        var jobId = await host.ScalarAsync<Guid>(
            "INSERT INTO platform.job (kind, payload, run_at, priority, state, attempts, max_attempts, timeout_seconds, last_error, finished_at) " +
            "VALUES ('iam.send-password-reset', '{}', now(), 1, 'dead', 3, 3, 60, 'SocketException: refused', now()) RETURNING id");

        var listed = await (await admin.GetAsync("/api/v1/instance/jobs")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(jobId, listed[0].GetProperty("id").GetGuid());
        Assert.Equal("dead", listed[0].GetProperty("state").GetString());
        var instance = await (await admin.GetAsync("/api/v1/instance")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, instance.GetProperty("jobs").GetProperty("dead").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/instance/jobs/{jobId}/retry")).StatusCode);
        Assert.Equal("pending", await host.ScalarAsync<string>($"SELECT state FROM platform.job WHERE id = '{jobId}'"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/v1/instance/jobs/{jobId}/retry")).StatusCode);
    }

    private static async Task<ApiClient> AdminAsync(ApiTestHost host)
    {
        var admin = host.Client();
        var setup = await admin.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = AdminEmail, displayName = "Admin", password = ApiTestHost.Password });
        Assert.Equal(HttpStatusCode.NoContent, setup.StatusCode);
        return admin;
    }
}
