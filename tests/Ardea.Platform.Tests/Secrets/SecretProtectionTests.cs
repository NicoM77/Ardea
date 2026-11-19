using System.Security.Cryptography;
using Ardea.Platform.Audit;
using Ardea.Platform.Secrets;
using Ardea.Platform.Settings;
using Ardea.Platform.Tests.Jobs;
using Ardea.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Tests.Secrets;

/// <summary>Secrets are encrypted with keys that live in the database, themselves encrypted with the master key (master plan 9.4).</summary>
public sealed class SecretProtectionTests(PostgresServer server)
{
    private static readonly MasterKey Key = new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task Another_instance_with_the_same_master_key_reads_the_secret()
    {
        await using var host = await JobTestHost.CreateAsync(server, s => s.AddLogging().AddArdeaSecrets(Key));
        var protectedValue = host.Services.GetRequiredService<ISecretProtector>().Protect("smtp", "p4ssw0rd");

        await using var other = Provider(host, Key);
        Assert.Equal("p4ssw0rd", other.GetRequiredService<ISecretProtector>().Unprotect("smtp", protectedValue));
    }

    [Fact]
    public async Task Key_ring_in_the_database_is_unreadable_without_the_master_key()
    {
        await using var host = await JobTestHost.CreateAsync(server, s => s.AddLogging().AddArdeaSecrets(Key));
        var protectedValue = host.Services.GetRequiredService<ISecretProtector>().Protect("smtp", "p4ssw0rd");

        Assert.Equal(0, await host.CountAsync("SELECT count(*) FROM platform.data_protection_key WHERE xml LIKE '%<masterKey%'"));
        Assert.True(await host.CountAsync("SELECT count(*) FROM platform.data_protection_key WHERE xml LIKE '%<encryptedKey%'") >= 1);

        await using var thief = Provider(host, new MasterKey(RandomNumberGenerator.GetBytes(32)));
        Assert.Null(thief.GetRequiredService<ISecretProtector>().Unprotect("smtp", protectedValue));
    }

    [Fact]
    public async Task A_secret_cannot_be_read_under_another_purpose()
    {
        await using var host = await JobTestHost.CreateAsync(server, s => s.AddLogging().AddArdeaSecrets(Key));
        var secrets = host.Services.GetRequiredService<ISecretProtector>();

        Assert.Null(secrets.Unprotect("webhook", secrets.Protect("github-token", "ghp_x")));
    }

    [Fact]
    public async Task Secret_setting_is_stored_encrypted_and_environment_locks_a_setting()
    {
        var environment = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["SMTP:HOST"] = "mail.example.com" }).Build();
        await using var host = await JobTestHost.CreateAsync(server, s => s.AddLogging().AddArdeaSecrets(Key).AddSingleton<IConfiguration>(environment));
        var password = new SettingDefinition("smtp.password", "SMTP:PASSWORD", IsSecret: true);
        var smtpHost = new SettingDefinition("smtp.host", "SMTP:HOST");

        await host.InUnitOfWorkAsync(s => s.GetRequiredService<IInstanceSettings>().SetAsync(password, "hunter2-but-longer", null, CancellationToken.None));
        SettingValue? read = null, locked = null;
        await host.InUnitOfWorkAsync(async s =>
        {
            read = await s.GetRequiredService<IInstanceSettings>().GetAsync(password, CancellationToken.None);
            locked = await s.GetRequiredService<IInstanceSettings>().GetAsync(smtpHost, CancellationToken.None);
        }, commit: false);

        Assert.Equal("hunter2-but-longer", read!.Value);
        Assert.Equal(SettingSource.Database, read.Source);
        Assert.Equal(0, await host.CountAsync("SELECT count(*) FROM platform.setting WHERE secret_value LIKE '%hunter2%' OR value::text LIKE '%hunter2%'"));
        Assert.True(locked!.Locked);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.InUnitOfWorkAsync(s => s.GetRequiredService<IInstanceSettings>().SetAsync(smtpHost, "other", null, CancellationToken.None)));
    }

    [Fact]
    public async Task Audit_event_commits_with_its_change_and_carries_origin()
    {
        await using var host = await JobTestHost.CreateAsync(server, _ => { });

        await host.InUnitOfWorkAsync(s => Record(s, "settings.changed"), commit: false);
        await host.InUnitOfWorkAsync(s =>
        {
            s.GetRequiredService<AuditOrigin>().Origin = "api";
            s.GetRequiredService<AuditOrigin>().IpAddress = "203.0.113.7";
            return Record(s, "settings.changed");
        });

        // Instance events are for the operator: the application role writes them but cannot read them back.
        Assert.Equal(1, await host.CountAsOwnerAsync("SELECT count(*) FROM platform.audit_event WHERE origin = 'api' AND ip_address = '203.0.113.7' AND organization_id IS NULL"));
        Assert.Equal(0, await host.CountAsync("SELECT count(*) FROM platform.audit_event"));

        static Task Record(IServiceProvider s, string action) =>
            s.GetRequiredService<IAuditLog>().RecordAsync(new AuditEntry(action, "setting", "smtp.host") { InstanceEvent = true }, CancellationToken.None);
    }

    private static ServiceProvider Provider(JobTestHost host, MasterKey key)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(host.DataSource);
        services.AddArdeaSecrets(key);
        return services.BuildServiceProvider();
    }
}
