using Ardea.TestSupport;
using Npgsql;

namespace Ardea.Notifications.Tests;

/// <summary>
/// The member's inbox and the administrator's delivery log read a page, newest first, and the header's unread
/// counter does not read the whole inbox (master plan 8.5, 8.6, 10.2). Each query is written as EF Core sends it,
/// with the tenant filter; the plan must name the index and must not sort the whole list.
/// </summary>
public sealed class ListPlanTests(PostgresServer server)
{
    [Theory]
    // Inbox: all, only unread; the counter beside it.
    [InlineData("SELECT * FROM notify.notification n WHERE n.organization_id = @org AND n.user_id = @user ORDER BY n.created_at DESC, n.id DESC LIMIT 21 OFFSET 20", "ix_notification_user")]
    [InlineData("SELECT * FROM notify.notification n WHERE n.organization_id = @org AND n.user_id = @user AND n.read_at IS NULL ORDER BY n.created_at DESC, n.id DESC LIMIT 21", "ix_notification_user|ix_notification_unread")]
    [InlineData("SELECT count(*) FROM notify.notification n WHERE n.organization_id = @org AND n.user_id = @user AND n.read_at IS NULL", "ix_notification_unread")]
    // Delivery log: all, by state, by both state and channel.
    [InlineData("SELECT * FROM notify.delivery d WHERE d.organization_id = @org ORDER BY d.created_at DESC, d.id DESC LIMIT 51", "ix_delivery_org_created")]
    [InlineData("SELECT * FROM notify.delivery d WHERE d.organization_id = @org AND d.state = 'failed' ORDER BY d.created_at DESC, d.id DESC LIMIT 51", "ix_delivery_org_created")]
    [InlineData("SELECT * FROM notify.delivery d WHERE d.organization_id = @org AND d.channel = 'email' AND d.state = 'sent' ORDER BY d.created_at DESC, d.id DESC LIMIT 51 OFFSET 50", "ix_delivery_org_created")]
    // What a delivery covered, for the log's counters.
    [InlineData("SELECT d.delivery_id, count(*) FROM notify.delivery_event d WHERE d.delivery_id = ANY (@ids) GROUP BY d.delivery_id", "ix_delivery_event_delivery")]
    public async Task Inbox_and_delivery_log_read_a_page_in_index_order(string sql, string index)
    {
        await using var host = await NotifyTestHost.StartAsync(server);
        var plan = await QueryPlans.ExplainAsync(
            host.Database.OwnerConnectionString("tests"), sql, TestContext.Current.CancellationToken,
            new NpgsqlParameter("org", Guid.CreateVersion7()), new NpgsqlParameter("user", Guid.CreateVersion7()),
            new NpgsqlParameter("ids", new[] { Guid.CreateVersion7() }));

        Assert.True(plan.Any(line => index.Split('|').Any(name => line.Contains(name, StringComparison.Ordinal))), string.Join('\n', plan));
        Assert.False(QueryPlans.SortsEverything(plan), string.Join('\n', plan));
    }
}
