using System.Security.Cryptography;
using Ardea.Host.Commands;
using Ardea.Host.Features;
using Ardea.Platform.Email;
using Ardea.Platform.Persistence;
using Ardea.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Ardea.Host.Tests;

/// <summary>
/// The instance tells its administrators when it warns about itself (master plan 12.7, task 6.6, ADR 043): rounds of
/// the self-check against a migrated database, with a fake clock and a capturing mail sender. The timer of the hosted
/// service is taken out; the tests run the rounds themselves.
/// </summary>
public sealed class InstanceSelfCheckTests(PostgresServer server)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Raising_sends_one_bundled_mail_to_every_instance_administrator_and_none_to_others()
    {
        var mail = new CapturingEmailSender();
        await using var host = await SelfCheckHost.StartAsync(server, mail);
        await host.AddUserAsync("first@example.org", admin: true, confirmed: true);
        await host.AddUserAsync("second@example.org", admin: true, confirmed: true);
        await host.AddUserAsync("unconfirmed@example.org", admin: true, confirmed: false);
        await host.AddUserAsync("member@example.org", admin: false, confirmed: true);
        await host.AddStaleSourceAsync("osv", "OSV");

        Assert.True(await host.RoundAsync());

        Assert.Equal(["first@example.org", "second@example.org"], mail.Sent.Select(m => m.To).Order(StringComparer.Ordinal));
        foreach (var message in mail.Sent)
        {
            Assert.Equal("Ardea: 2 new warnings about the instance", message.Subject);
            Assert.Contains("No worker has started yet", message.Text, StringComparison.Ordinal);
            Assert.Contains("OSV has not been fetched", message.Text, StringComparison.Ordinal);
            Assert.Contains("https://ardea.test/admin", message.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_second_round_sends_nothing_and_a_lasting_warning_is_mailed_again_after_24_hours()
    {
        var mail = new CapturingEmailSender();
        await using var host = await SelfCheckHost.StartAsync(server, mail);
        await host.AddUserAsync("admin@example.org", admin: true, confirmed: true);
        await host.RoundAsync();
        Assert.Single(mail.Sent);

        host.Time.Advance(TimeSpan.FromMinutes(5));
        await host.RoundAsync();
        Assert.Single(mail.Sent);

        host.Time.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(5));
        await host.RoundAsync();
        Assert.Equal(2, mail.Sent.Count);
        Assert.Equal("Ardea: 1 warning still active", mail.Sent[1].Subject);
        Assert.Contains("Reported since 2026-11-19 08:00 UTC", mail.Sent[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_resolved_mail_is_sent_only_after_the_warning_stayed_clear_for_one_round()
    {
        var mail = new CapturingEmailSender();
        await using var host = await SelfCheckHost.StartAsync(server, mail);
        await host.AddUserAsync("admin@example.org", admin: true, confirmed: true);
        await host.RoundAsync();
        Assert.Single(mail.Sent);
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.instance_warning WHERE code = 'worker_stalled' AND notified_at IS NOT NULL"));

        host.Time.Advance(TimeSpan.FromMinutes(5));
        await host.BeatAsync();
        await host.RoundAsync();
        Assert.Single(mail.Sent);

        host.Time.Advance(TimeSpan.FromMinutes(5));
        await host.BeatAsync();
        await host.RoundAsync();
        Assert.Equal(2, mail.Sent.Count);
        Assert.Equal("Ardea: 1 warning over", mail.Sent[1].Subject);
    }

    [Fact]
    public async Task Without_smtp_a_round_sends_nothing_fails_nothing_and_keeps_the_warning()
    {
        // The real sender: no SMTP settings in the environment or the database.
        await using var host = await SelfCheckHost.StartAsync(server, mail: null);
        await host.AddUserAsync("admin@example.org", admin: true, confirmed: true);

        Assert.True(await host.RoundAsync());
        host.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await host.RoundAsync());

        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.instance_warning WHERE code = 'worker_stalled' AND notified_at IS NULL"));
    }

    [Fact]
    public async Task Two_processes_at_once_send_the_mail_once()
    {
        var mail = new CapturingEmailSender { Delay = TimeSpan.FromMilliseconds(500) };
        await using var api = await SelfCheckHost.StartAsync(server, mail);
        await using var worker = await SelfCheckHost.StartAsync(server, mail, api.Database);
        await api.AddUserAsync("admin@example.org", admin: true, confirmed: true);

        var rounds = await Task.WhenAll(api.RoundAsync(), worker.RoundAsync());

        Assert.Single(mail.Sent);
        Assert.Single(rounds, ran => !ran);
        Assert.True(await worker.RoundAsync());
        Assert.Single(mail.Sent);
    }

    [Fact]
    public async Task A_stalled_worker_is_reported_by_the_round_of_the_api_role_without_the_job_queue()
    {
        var mail = new CapturingEmailSender();
        await using var host = await SelfCheckHost.StartAsync(server, mail);
        await host.AddUserAsync("admin@example.org", admin: true, confirmed: true);
        await host.BeatAsync(TimeSpan.FromMinutes(-10));

        await host.RoundAsync();

        Assert.True(host.ApiRunsTheSelfCheck);
        Assert.Contains("The worker has stopped", Assert.Single(mail.Sent).Text, StringComparison.Ordinal);
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job"));
    }

    [Fact]
    public async Task A_failed_mail_gives_its_claim_back_and_the_next_round_sends_it()
    {
        var mail = new CapturingEmailSender { Failure = () => new IOException("connection reset") };
        await using var host = await SelfCheckHost.StartAsync(server, mail);
        await host.AddUserAsync("admin@example.org", admin: true, confirmed: true);

        Assert.True(await host.RoundAsync());
        Assert.Empty(mail.Sent);
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.instance_warning WHERE notified_at IS NULL"));

        mail.Failure = null;
        host.Time.Advance(TimeSpan.FromMinutes(5));
        await host.RoundAsync();
        Assert.Equal("Ardea: 1 new warning about the instance", Assert.Single(mail.Sent).Subject);
    }

    private sealed class SelfCheckHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private SelfCheckHost(WebApplication app, DatabaseOptions database, FakeTimeProvider time, bool apiRunsTheSelfCheck)
        {
            _app = app;
            Database = database;
            Time = time;
            ApiRunsTheSelfCheck = apiRunsTheSelfCheck;
        }

        public DatabaseOptions Database { get; }

        public FakeTimeProvider Time { get; }

        /// <summary>Whether the API role registered the self-check's hosted service.</summary>
        public bool ApiRunsTheSelfCheck { get; }

        /// <param name="server">The test server.</param>
        /// <param name="mail">The capturing sender; null keeps the real one.</param>
        /// <param name="shared">A database another host already uses, as a second process would.</param>
        public static async Task<SelfCheckHost> StartAsync(PostgresServer server, CapturingEmailSender? mail, DatabaseOptions? shared = null)
        {
            var database = shared ?? await MigrateAsync(server);
            var time = new FakeTimeProvider(Start);
            var registered = false;
            var app = ApiRole.Build([], out var check, builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DB_HOST"] = database.Host,
                    ["DB_PORT"] = database.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["DB_NAME"] = database.Database,
                    ["DB_APP_PASSWORD"] = database.AppPassword,
                    ["MASTER_KEY"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    ["PUBLIC_URL"] = "https://ardea.test",
                });
                builder.Logging.SetMinimumLevel(LogLevel.Warning);
            }, services =>
            {
                services.AddSingleton<TimeProvider>(time);
                if (mail is not null)
                {
                    services.AddScoped<IEmailSender>(_ => mail);
                }

                var hosted = services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(InstanceSelfCheckService));
                registered = true;
                services.Remove(hosted);
            }) ?? throw new InvalidOperationException(string.Join(" ", check.Errors));
            await app.StartAsync();
            return new SelfCheckHost(app, database, time, registered);
        }

        public Task<bool> RoundAsync() => _app.Services.GetRequiredService<InstanceSelfCheck>().RunRoundAsync(CancellationToken.None);

        public Task AddUserAsync(string email, bool admin, bool confirmed) => ExecuteAsync(
            """
            INSERT INTO iam."user" (display_name, is_instance_admin, user_name, normalized_user_name, email, normalized_email, email_confirmed,
                                    phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count)
            VALUES ($1, $2, $3, upper($3), $3, upper($3), $4, false, false, true, 0)
            """,
            email[..email.IndexOf('@', StringComparison.Ordinal)], admin, email, confirmed);

        public Task AddStaleSourceAsync(string key, string name) => ExecuteAsync(
            """
            INSERT INTO intel.source (key, name, kind, operator, operator_kind, adapter, enabled, interval, config, attribution, terms_mode, parser_version, next_sync_at, consecutive_failures, health, created_at)
            VALUES ($1, $2, 'record', 'X', 'community', 'x', true, interval '1 hour', '{}', 'X', 'full', 1, $3, 3, 'failing', $4)
            """,
            key, name, Time.GetUtcNow(), Time.GetUtcNow().AddHours(-7));

        /// <summary>A worker's heartbeat at the fake clock's now, shifted by <paramref name="offset"/>.</summary>
        public Task BeatAsync(TimeSpan offset = default) => ExecuteAsync(
            """
            INSERT INTO platform.worker_heartbeat (worker_id, started_at, beat_at, version) VALUES ('worker-1', $1, $1, 'test')
            ON CONFLICT (worker_id) DO UPDATE SET beat_at = excluded.beat_at
            """,
            Time.GetUtcNow() + offset);

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new NpgsqlConnection(Database.OwnerConnectionString("tests"));
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private async Task ExecuteAsync(string sql, params object[] values)
        {
            await using var connection = new NpgsqlConnection(Database.OwnerConnectionString("tests"));
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var value in values)
            {
                command.Parameters.Add(new NpgsqlParameter { Value = value });
            }

            await command.ExecuteNonQueryAsync();
        }

        private static async Task<DatabaseOptions> MigrateAsync(PostgresServer server)
        {
            var database = new DatabaseOptions
            {
                Host = server.Host,
                Port = server.Port,
                Database = await server.CreateDatabaseAsync("selfcheck"),
                OwnerUser = PostgresServer.OwnerUser,
                OwnerPassword = PostgresServer.OwnerPassword,
                AppPassword = "app-test-password",
            };
            await using var owner = new NpgsqlConnection(database.OwnerConnectionString("tests"));
            await owner.OpenAsync();
            await new DatabaseMigrator(database, Modules.Databases, new NoBackup(), NullLogger<DatabaseMigrator>.Instance).MigrateAsync(owner, CancellationToken.None);
            return database;
        }
    }

    private sealed class NoBackup : IBackupWriter
    {
        public Task<string?> WriteAsync(NpgsqlConnection owner, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class CapturingEmailSender : IEmailSender
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

        /// <summary>How long the mail server takes to accept a message.</summary>
        public TimeSpan Delay { get; init; }

        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            await Task.Delay(Delay, cancellationToken);
            if (Failure is { } failure)
            {
                throw failure();
            }

            lock (_sent)
            {
                _sent.Add(message);
            }
        }
    }
}
