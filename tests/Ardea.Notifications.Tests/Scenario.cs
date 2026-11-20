using System.Net;
using System.Text.Json;

namespace Ardea.Notifications.Tests;

internal sealed class Scenario
{
    private Scenario(NotifyTestHost host, ApiClient owner, Guid organization, Guid project, Guid component)
    {
        Host = host;
        Owner = owner;
        Organization = organization;
        Project = project;
        Component = component;
    }

    public NotifyTestHost Host { get; }

    public ApiClient Owner { get; }

    public Guid Organization { get; }

    public Guid Project { get; }

    public Guid Component { get; }

    public string Path => $"/api/v1/orgs/{Organization}";

    public static async Task<Scenario> StartAsync(NotifyTestHost host, bool keepDefaultRules = false)
    {
        var (owner, organization) = await host.OwnerAsync();
        var (project, component) = await host.ProjectAsync(owner, organization);
        var scenario = new Scenario(host, owner, organization, project, component);
        await host.ExecuteAsync("DELETE FROM platform.job WHERE kind LIKE 'assets.%' OR kind LIKE 'assess.%'");
        if (!keepDefaultRules)
        {
            foreach (var rule in (await NotifyTestHost.JsonAsync(await owner.GetAsync($"{scenario.Path}/rules"))).EnumerateArray())
            {
                Assert.Equal(HttpStatusCode.NoContent, (await owner.SendAsync(HttpMethod.Delete, $"{scenario.Path}/rules/{rule.GetProperty("id").GetGuid()}")).StatusCode);
            }
        }

        return scenario;
    }

    public async Task<Guid> OwnerIdAsync() => await Host.ScalarAsync<Guid>("SELECT id FROM iam.\"user\" WHERE normalized_email = 'OWNER@EXAMPLE.ORG'");

    public async Task<JsonElement> RuleAsync(string name, int minLevel, string[] events, params object[] targets) =>
        await NotifyTestHost.JsonAsync(
            await Owner.PostAsync($"{Path}/rules", new { name, minLevel, events, targets }),
            HttpStatusCode.Created);

    public static object Target(string channel, string mode = "immediate", string kind = "role", string reference = "owner", string? cron = null, bool sendEmpty = false) =>
        new { channel, recipientKind = kind, recipientRef = reference, mode, digestCron = cron, sendEmpty };

    public async Task<Guid> ActionAsync(int number, int level, string exploitation = "none", bool backfill = false)
    {
        var component = number == 2
            ? Component
            : (await NotifyTestHost.JsonAsync(
                await Owner.PostAsync($"{Path}/projects/{Project}/components", new { productKey = "postgresql", version = $"16.{number}" }), HttpStatusCode.Created))
                .GetProperty("id").GetGuid();
        await Host.ExecuteAsync("DELETE FROM platform.job WHERE kind LIKE 'assets.%' OR kind LIKE 'assess.%'");
        return await Host.ActionAsync(Organization, Project, component, number, level, exploitation, backfill);
    }

    public Task SetActionAsync(Guid action, int level, string exploitation = "none") => Host.SetActionAsync(action, level, exploitation);

    public Task<Guid> EventAsync(string kind, Guid? action = null, string data = "{}", Guid? project = null, Guid? notice = null) =>
        Host.EventAsync(Organization, kind, action, project, data, notice);

    public async Task<(Guid Stack, Guid Component)> StackEntryAsync(string product = "postgresql")
    {
        var stack = (await NotifyTestHost.JsonAsync(await Owner.GetAsync($"{Path}/stack"))).GetProperty("projectId").GetGuid();
        var component = (await NotifyTestHost.JsonAsync(
            await Owner.PostAsync($"{Path}/projects/{stack}/components", new { productKey = product }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await Host.ExecuteAsync("DELETE FROM platform.job WHERE kind LIKE 'assets.%' OR kind LIKE 'assess.%'");
        return (stack, component);
    }

    public Task EvaluateAsync() => Host.EvaluateAsync(Organization);

    public async Task DeliverAsync()
    {
        Host.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.Empty(await Host.RunJobsAsync());
    }

    public Task<long> CountAsync(string sql) => Host.ScalarAsync<long>(sql);
}
