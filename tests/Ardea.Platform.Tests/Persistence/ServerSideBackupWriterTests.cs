using Ardea.Platform.Persistence;
using Ardea.TestSupport;

namespace Ardea.Platform.Tests.Persistence;

public sealed class ServerSideBackupWriterTests(PostgresServer server)
{
    [Fact]
    public async Task Server_writes_a_restorable_dump_and_keeps_three()
    {
        var db = await MigrationDatabase.CreateAsync(server);
        await using var owner = await db.OpenOwnerAsync();
        await db.Migrator().MigrateAsync(owner, TestContext.Current.CancellationToken);

        string? last = null;
        for (var i = 0; i < 4; i++)
        {
            var writer = new ServerSideBackupWriter(
                db.Options, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 10, 0, i, TimeSpan.Zero)),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ServerSideBackupWriter>.Instance);
            last = await writer.WriteAsync(owner, TestContext.Current.CancellationToken);
        }

        Assert.Equal($"{ServerSideBackupWriter.Directory}/ardea-20261118T100003Z.dump", last);
        var listing = await server.Container.ExecAsync(["sh", "-c", $"ls -1 {ServerSideBackupWriter.Directory}"], TestContext.Current.CancellationToken);
        var files = listing.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, files.Length);
        Assert.DoesNotContain("ardea-20261118T100000Z.dump", files);

        var restoreList = await server.Container.ExecAsync(["pg_restore", "--list", last!], TestContext.Current.CancellationToken);
        Assert.Equal(0, restoreList.ExitCode);
        Assert.Contains("TABLE platform job", restoreList.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Role_without_server_programs_skips_the_dump()
    {
        var db = await MigrationDatabase.CreateAsync(server);
        await using (var owner = await db.OpenOwnerAsync())
        {
            await db.Migrator().MigrateAsync(owner, TestContext.Current.CancellationToken);
        }

        await using var app = await db.OpenAppAsync();
        var path = await db.Backup().WriteAsync(app, TestContext.Current.CancellationToken);

        Assert.Null(path);
    }
}
