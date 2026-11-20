using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Ardea.Host;
using Ardea.Host.Commands;
using Ardea.Notifications.Evaluation;
using Ardea.Platform.Email;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Ardea.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Ardea.Notifications.Tests;

/// <summary>
/// The real API pipeline on a test server against a freshly migrated database, with a clock the test moves
/// and the job system's work done by <see cref="RunJobsAsync"/>, the way the worker does it: one scope, the
/// job's tenant, one committed unit of work per job, and a failed attempt back in the queue.
/// </summary>
internal sealed class NotifyTestHost : IAsyncDisposable
{
    public const string SetupTokenValue = "test-setup-token";
    public const string Password = "correct horse battery staple";

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PostgresServer, Task<string>> Templates = new();

    private static int _databases;

    private readonly WebApplication _app;

    private NotifyTestHost(WebApplication app, DatabaseOptions database, FakeTimeProvider time, CapturingEmailSender? mail, CapturingLogs logs)
    {
        _app = app;
        Database = database;
        Time = time;
        Mail = mail;
        Logs = logs;
    }

    /// <summary>Everything logged at Warning and above, and every log of outgoing HTTP requests.</summary>
    public CapturingLogs Logs { get; }

    public DatabaseOptions Database { get; }

    public FakeTimeProvider Time { get; }

    /// <summary>The captured e-mails; <c>null</c> when the host sends through a real SMTP server.</summary>
    public CapturingEmailSender? Mail { get; }

    public IServiceProvider Services => _app.Services;

    /// <param name="server">The test run's database server.</param>
    /// <param name="settings">Further configuration, e.g. SMTP for a real mail server.</param>
    /// <param name="captureMail">Replace SMTP by a capturing sender, configured unless the test turns it off.</param>
    /// <param name="start">The fake clock's start; now by default. Sessions are checked against the database's clock, so the API is used before the clock moves far.</param>
    /// <param name="services">Replaces services after every registration.</param>
    public static async Task<NotifyTestHost> StartAsync(
        PostgresServer server, Dictionary<string, string?>? settings = null, bool captureMail = true, DateTimeOffset? start = null,
        Action<IServiceCollection>? services = null)
    {
        var database = Options(server, await CloneAsync(server, await Templates.GetValue(server, MigrateTemplateAsync)));
        await using (var owner = new NpgsqlConnection(database.OwnerConnectionString("tests")))
        {
            await owner.OpenAsync();
            await new DatabaseMigrator(database, Modules.Databases, NoBackup.Instance, NullLogger<DatabaseMigrator>.Instance)
                .EnsureAppRoleAsync(owner, CancellationToken.None);
        }

        var values = new Dictionary<string, string?>
        {
            ["DB_HOST"] = database.Host,
            ["DB_PORT"] = database.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["DB_NAME"] = database.Database,
            ["DB_APP_PASSWORD"] = database.AppPassword,
            ["MASTER_KEY"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["PUBLIC_URL"] = "https://ardea.test",
            ["SETUP_TOKEN"] = SetupTokenValue,
            ["REGISTRATION"] = "open",
            ["PASSWORD_HASH_ITERATIONS"] = "1000",
            ["RATE_LIMIT:AUTH_PER_MINUTE"] = "1000",
            ["RATE_LIMIT:OUTBOUND_PER_MINUTE"] = "1000",
        };
        foreach (var (key, value) in settings ?? [])
        {
            values[key] = value;
        }

        var time = new FakeTimeProvider(start ?? DateTimeOffset.UtcNow);
        var mail = captureMail ? new CapturingEmailSender() : null;
        var logs = new CapturingLogs();
        var app = ApiRole.Build([], out var check, builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(values);
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Trace);
        }, collection =>
        {
            collection.AddSingleton<TimeProvider>(time);
            // The self-check's timer would run rounds whenever the test moves the clock and mail the owner, who is the
            // instance administrator, about the stalled worker of the test (ADR 043). Its own tests are in Ardea.Host.Tests.
            collection.Remove(collection.Single(d => d.ImplementationType == typeof(Host.Features.InstanceSelfCheckService)));
            // Here, after the host replaced the providers with its JSON console.
            collection.AddSingleton<ILoggerProvider>(logs);
            if (mail is not null)
            {
                collection.AddScoped<IEmailSender>(_ => mail);
            }

            services?.Invoke(collection);
        }) ?? throw new InvalidOperationException(string.Join(" ", check.Errors));
        await app.StartAsync();
        return new NotifyTestHost(app, database, time, mail, logs);
    }

    public ApiClient Client() => new(_app.GetTestServer(), "https://ardea.test");

    /// <summary>Runs <c>migrate</c> again, as an upgrade does: nothing to migrate, every module's step after it runs.</summary>
    public async Task MigrateAgainAsync()
    {
        await using var owner = new NpgsqlConnection(Database.OwnerConnectionString("tests"));
        await owner.OpenAsync();
        await new DatabaseMigrator(Database, Modules.Databases, NoBackup.Instance, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(owner, CancellationToken.None);
    }

    /// <summary>A plain client without cookies or CSRF token, as a mail client's server is.</summary>
    public HttpClient RawClient()
    {
        var client = _app.GetTestServer().CreateClient();
        client.BaseAddress = new Uri("https://ardea.test");
        return client;
    }

    /// <summary>Sets up the instance with its first user, who founds an organisation.</summary>
    public async Task<(ApiClient Client, Guid Organization)> OwnerAsync(string email = "owner@example.org", string organization = "Acme")
    {
        var client = Client();
        var setup = await client.PostAsync("/api/v1/setup", new { setupToken = SetupTokenValue, email, displayName = "Owner", password = Password });
        Assert.Equal(HttpStatusCode.NoContent, setup.StatusCode);
        return (client, await OrganizationAsync(client, organization));
    }

    public async Task<ApiClient> RegisterAsync(string email, string displayName)
    {
        var client = Client();
        var response = await client.PostAsync("/api/v1/auth/register", new { email, displayName, password = Password });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }

    public static async Task<Guid> OrganizationAsync(ApiClient client, string name) =>
        (await JsonAsync(await client.PostAsync("/api/v1/orgs", new { name }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    /// <summary>Makes a registered account a member of the organisation, as accepting an invitation does.</summary>
    public Task AddMemberAsync(Guid organization, string email, string role) => ExecuteAsync($"""
        INSERT INTO iam.membership (id, organization_id, user_id, role, created_at)
        SELECT uuidv7(), '{organization}', id, '{role}', now() FROM iam."user" WHERE normalized_email = upper('{email}')
        """);

    public async Task<(Guid Project, Guid Component)> ProjectAsync(ApiClient client, Guid organization, string project = "Platform Ops", string product = "postgresql", string version = "16.2")
    {
        var projectId = (await JsonAsync(await client.PostAsync($"/api/v1/orgs/{organization}/projects", new { name = project }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var component = (await JsonAsync(await client.PostAsync($"/api/v1/orgs/{organization}/projects/{projectId}/components", new { productKey = product, version }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        return (projectId, component);
    }

    /// <summary>An action with one open finding, as the assessment writes it (task 3.5).</summary>
    public async Task<Guid> ActionAsync(
        Guid organization, Guid project, Guid component, int number, int level, string exploitation = "none", bool backfill = false, string? key = null)
    {
        key ??= $"CVE-2026-{number:D4}";
        var entry = await ScalarAsync<Guid>($$"""
            INSERT INTO intel.security_entry (kind, primary_key, title, state, changed_at, revision, material_fingerprint, remediation_fingerprint, exploitation, fix_available, cwe_ids, authority_warning, provenance)
            VALUES ('vulnerability', '{{key}}', 'PostgreSQL: privilege escalation', 'published', now(), 1, 'm', 'r', '{{exploitation}}', true, '{}', false, '{}') RETURNING id
            """);
        var alert = await ScalarAsync<Guid>($"""
            INSERT INTO assess.alert (organization_id, entry_id, entry_key, number, level, assessed_entry_revision, created_at, changed_at)
            VALUES ('{organization}', '{entry}', '{key}', {number + 1000}, {level}, 1, now(), now()) RETURNING id
            """);
        var action = await ScalarAsync<Guid>($"""
            INSERT INTO assess.remediation (organization_id, project_id, component_id, number, level, target_version, open_findings, exploitation, fix_available, status, opened_at, is_backfill, changed_at)
            VALUES ('{organization}', '{project}', '{component}', {number}, {level}, '16.4', 1, '{exploitation}', true, 'open', now(), {(backfill ? "true" : "false")}, now()) RETURNING id
            """);
        await ExecuteAsync($"""
            INSERT INTO assess.finding (organization_id, alert_id, entry_id, project_id, component_id, remediation_id, match_type, version_verdict, verdict, verdict_source, level, evidence, factors, status, status_changed_at, is_backfill, created_at, changed_at, exploitation, fix_available, fixed_version)
            VALUES ('{organization}', '{alert}', '{entry}', '{project}', '{component}', '{action}', 'product', 'in_range', 'affected', 'rules', {level}, jsonb_build_object(), jsonb_build_object(), 'open', now(), {(backfill ? "true" : "false")}, now(), now(), '{exploitation}', true, '16.4')
            """);
        return action;
    }

    /// <summary>An open notice naming the components, as the assessment writes it (ADR 047).</summary>
    public async Task<Guid> NoticeAsync(Guid organization, Guid[] components, string key, string exploitation = "none", double score = 9.8, bool backfill = false)
    {
        var entry = await ScalarAsync<Guid>($$"""
            INSERT INTO intel.security_entry (kind, primary_key, title, state, changed_at, revision, material_fingerprint, remediation_fingerprint, exploitation, fix_available, cwe_ids, authority_warning, provenance)
            VALUES ('vulnerability', '{{key}}', 'PostgreSQL: remote code execution', 'published', now(), 1, 'm', 'r', '{{exploitation}}', true, '{}', false, '{}') RETURNING id
            """);
        return await ScalarAsync<Guid>($"""
            INSERT INTO assess.notice (id, organization_id, entry_id, entry_key, state, component_ids, cvss_score, exploitation, is_backfill, raised_at, changed_at)
            VALUES (uuidv7(), '{organization}', '{entry}', '{key}', 'open', ARRAY[{string.Join(", ", components.Select(c => $"'{c}'::uuid"))}], {score.ToString(System.Globalization.CultureInfo.InvariantCulture)}, '{exploitation}', {(backfill ? "true" : "false")}, now(), now())
            RETURNING id
            """);
    }

    /// <summary>Changes an action as the assessment would, before the event that reports it.</summary>
    public Task SetActionAsync(Guid action, int level, string exploitation = "none") =>
        ExecuteAsync($"UPDATE assess.remediation SET level = {level}, exploitation = '{exploitation}' WHERE id = '{action}'");

    /// <summary>
    /// A history row and its place in <c>notify.pending_event</c>, as the hand-off from Assessment writes them
    /// in one transaction (ADR 038). Returns the event's id.
    /// </summary>
    public async Task<Guid> EventAsync(Guid organization, string kind, Guid? action = null, Guid? project = null, string data = "{}", Guid? notice = null)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync($"""
            INSERT INTO assess.assess_event (id, organization_id, remediation_id, project_id, notice_id, at, kind, data)
            VALUES ('{id}', '{organization}', {Sql(action)}, {Sql(project)}, {Sql(notice)}, '{Time.GetUtcNow():O}', '{kind}', '{data}');
            INSERT INTO notify.pending_event (event_id, organization_id, kind, at) VALUES ('{id}', '{organization}', '{kind}', '{Time.GetUtcNow():O}');
            """);
        return id;
    }

    /// <summary>Puts an event back into the queue, as a second run of the same hand-off would.</summary>
    public Task RequeueAsync(Guid organization, Guid eventId, string kind) => ExecuteAsync($"""
        INSERT INTO notify.pending_event (event_id, organization_id, kind, at) VALUES ('{eventId}', '{organization}', '{kind}', '{Time.GetUtcNow():O}')
        ON CONFLICT DO NOTHING
        """);

    /// <summary>Runs the organisation's evaluation job, as the hand-off queues it.</summary>
    public Task EvaluateAsync(Guid organization) => RunAsync(new EvaluateEvents(), organization);

    /// <summary>Runs one job directly in its own scope and unit of work.</summary>
    public async Task RunAsync<TJob>(TJob job, Guid? organization, int attempt = 1)
        where TJob : IJob
    {
        var registry = Services.GetRequiredService<JobRegistry>();
        await using var scope = Services.CreateAsyncScope();
        if (organization is { } org)
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetOrganization(org);
        }

        using var payload = JsonSerializer.SerializeToDocument(job, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await registry[TJob.Kind].Invoke(scope.ServiceProvider, payload, new JobContext(Guid.CreateVersion7(), TJob.Kind, attempt, organization), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
    }

    /// <summary>
    /// Runs every waiting job that is due by the fake clock, whatever its kind, until none is left. A job that
    /// throws goes back to the queue with its attempt counted, due again after the back-off; its error is returned.
    /// </summary>
    public async Task<IReadOnlyList<string>> RunJobsAsync()
    {
        var registry = Services.GetRequiredService<JobRegistry>();
        var errors = new List<string>();
        for (var round = 0; round < 50; round++)
        {
            var due = new List<(Guid Id, string Kind, string Payload, Guid? Organization, int Attempt)>();
            await using (var connection = new NpgsqlConnection(Database.OwnerConnectionString("tests")))
            {
                await connection.OpenAsync();
                await using var claim = new NpgsqlCommand(
                    "UPDATE platform.job SET state = 'running', attempts = attempts + 1 WHERE state = 'pending' AND run_at <= $1 RETURNING id, kind, payload::text, org_id, attempts",
                    connection);
                claim.Parameters.Add(new NpgsqlParameter { Value = Time.GetUtcNow() });
                await using var reader = await claim.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    due.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.GetInt32(4)));
                }
            }

            if (due.Count == 0)
            {
                return errors;
            }

            foreach (var (id, kind, payload, organization, attempt) in due.OrderBy(d => d.Id))
            {
                string? error = null;
                try
                {
                    await using var scope = Services.CreateAsyncScope();
                    if (organization is { } org)
                    {
                        scope.ServiceProvider.GetRequiredService<TenantContext>().SetOrganization(org);
                    }

                    using var json = JsonDocument.Parse(payload);
                    await registry[kind].Invoke(scope.ServiceProvider, json, new JobContext(id, kind, attempt, organization), CancellationToken.None);
                    await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    error = $"{kind}: {ex.Message}";
                    errors.Add(error);
                }

                await ExecuteAsync(error is null
                    ? $"UPDATE platform.job SET state = 'succeeded', finished_at = now() WHERE id = '{id}'"
                    : $"UPDATE platform.job SET state = CASE WHEN attempts >= max_attempts THEN 'dead' ELSE 'pending' END, run_at = '{(Time.GetUtcNow() + Backoff.After(attempt, 0.5)):O}' WHERE id = '{id}'");
            }
        }

        throw new InvalidOperationException("Jobs keep coming.");
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.OwnerConnectionString("tests"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async Task<IReadOnlyList<string>> RowsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.OwnerConnectionString("tests"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.IsDBNull(0) ? string.Empty : reader.GetValue(0).ToString()!);
        }

        return rows;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.OwnerConnectionString("tests"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static string Sql(Guid? id) => id is { } value ? $"'{value}'" : "NULL";

    private static DatabaseOptions Options(PostgresServer server, string name) => new()
    {
        Host = server.Host,
        Port = server.Port,
        Database = name,
        OwnerUser = PostgresServer.OwnerUser,
        OwnerPassword = PostgresServer.OwnerPassword,
        AppPassword = "app-test-password",
    };

    private static async Task<string> MigrateTemplateAsync(PostgresServer server)
    {
        var database = Options(server, await server.CreateDatabaseAsync("notify_template"));
        await using (var owner = new NpgsqlConnection(database.OwnerConnectionString("tests")))
        {
            await owner.OpenAsync();
            await new DatabaseMigrator(database, Modules.Databases, NoBackup.Instance, NullLogger<DatabaseMigrator>.Instance)
                .MigrateAsync(owner, CancellationToken.None);
        }

        foreach (var application in new[] { "tests", "ardea-migrate-lock" })
        {
            await using var pooled = new NpgsqlConnection(database.OwnerConnectionString(application));
            NpgsqlConnection.ClearPool(pooled);
        }

        await using var admin = new NpgsqlConnection(server.OwnerConnectionString("postgres_admin"));
        await admin.OpenAsync();
        await using var close = new NpgsqlCommand($"""
            ALTER DATABASE "{database.Database}" WITH ALLOW_CONNECTIONS false;
            SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{database.Database}';
            """, admin);
        await close.ExecuteNonQueryAsync();
        return database.Database;
    }

    private static async Task<string> CloneAsync(PostgresServer server, string template)
    {
        var name = $"notify_{Interlocked.Increment(ref _databases)}_{Guid.NewGuid():N}"[..40];
        await using var admin = new NpgsqlConnection(server.OwnerConnectionString("postgres_admin"));
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\" TEMPLATE \"{template}\"", admin);
        await create.ExecuteNonQueryAsync();
        return name;
    }

    private sealed class NoBackup : IBackupWriter
    {
        public static readonly NoBackup Instance = new();

        public Task<string?> WriteAsync(NpgsqlConnection owner, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}

/// <summary>Takes the e-mails instead of a mail server; configured unless a test turns SMTP off.</summary>
internal sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];

    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    /// <summary>When set, sending throws this instead, as a broken mail server would.</summary>
    public Func<Exception>? Failure { get; set; }

    public Func<bool> Configured { get; set; } = () => true;

    public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken) => Task.FromResult(Configured());

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            throw failure();
        }

        lock (_sent)
        {
            _sent.Add(message);
        }

        return Task.CompletedTask;
    }
}

/// <summary>A browser-like client: keeps cookies and echoes the CSRF token like the web app.</summary>
internal sealed class ApiClient : IDisposable
{
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _http;
    private readonly Uri _base;

    public ApiClient(TestServer server, string baseAddress)
    {
        _base = new Uri(baseAddress);
        _http = new HttpClient(new CookieHandler(_cookies, server.CreateHandler())) { BaseAddress = _base };
    }

    public Task<HttpResponseMessage> GetAsync(string path) => _http.GetAsync(new Uri(path, UriKind.Relative));

    public Task<HttpResponseMessage> StreamAsync(string path, CancellationToken cancellationToken) =>
        _http.GetAsync(new Uri(path, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, bool csrf = true)
    {
        if (csrf && Xsrf() is null)
        {
            (await _http.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative))).Dispose();
        }

        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (csrf && Xsrf() is { } token)
        {
            request.Headers.Add("X-XSRF-TOKEN", token);
        }

        var response = await _http.SendAsync(request);
        if (response.IsSuccessStatusCode && (path.StartsWith("/api/v1/auth/", StringComparison.Ordinal) || path == "/api/v1/setup"))
        {
            (await _http.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative))).Dispose();
        }

        return response;
    }

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null, bool csrf = true) => SendAsync(HttpMethod.Post, path, body, csrf);

    public Task<HttpResponseMessage> PutAsync(string path, object? body) => SendAsync(HttpMethod.Put, path, body);

    public void Dispose() => _http.Dispose();

    private string? Xsrf() => _cookies.GetCookies(_base).FirstOrDefault(c => c.Name == "XSRF-TOKEN")?.Value;

    private sealed class CookieHandler(CookieContainer cookies, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var header = cookies.GetCookieHeader(request.RequestUri!);
            if (header.Length > 0)
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var cookie in setCookies)
                {
                    cookies.SetCookies(request.RequestUri!, cookie);
                }
            }

            return response;
        }
    }
}

/// <summary>Collects log messages with their category.</summary>
internal sealed class CapturingLogs : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

    public IReadOnlyList<string> Messages => [.. _messages];

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _messages);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue($"{category}: {formatter(state, exception)}");
    }
}
