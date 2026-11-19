using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ardea.Assets.Repositories;
using Ardea.Assets.Repositories.Indexing;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Iam.Tests;

/// <summary>
/// Reading a repository into the profile (task 2.13; master plan 16.2, 16.5, 16.6; phase 2
/// acceptance), against a scripted repository host.
/// </summary>
public sealed class IndexRepositoryTests(PostgresServer server)
{
    [Fact]
    public async Task A_commit_that_only_raises_one_package_version_changes_exactly_that_component_and_keeps_every_id()
    {
        var github = new ScriptedRepository();
        github.Commit("a1", Lockfile(("lodash", "4.17.20", false, true), ("jest", "29.7.0", true, true), ("jest-util", "29.7.0", true, false)), ("Dockerfile", "FROM node:20-alpine\n"));
        await using var host = await StartAsync(github);
        var (client, orgId, projectId, linkId) = await LinkAsync(host);
        using var _ = client;

        await RunAsync(host, orgId, linkId);

        var first = await ComponentsAsync(host, projectId);
        Assert.Equal(["jest 29.7.0 development direct", "jest-util 29.7.0 development transitive", "lodash 4.17.20 runtime direct"], first.Select(c => c.Describe));
        Assert.Equal("partial", await host.ScalarAsync<string>($"SELECT state FROM assets.repository_link WHERE id = '{linkId}'"));
        Assert.Equal("nodejs 20", await host.ScalarAsync<string>("SELECT product_key || ' ' || version FROM assets.component_suggestion WHERE state = 'open'"));
        Assert.Equal($"repository:acme/portal@a1", await host.ScalarAsync<string>("SELECT origin FROM platform.audit_event WHERE action = 'repository.indexed'"));

        // Dependabot raised lodash; the next reading changes that one component.
        github.Commit("b2", Lockfile(("lodash", "4.17.21", false, true), ("jest", "29.7.0", true, true), ("jest-util", "29.7.0", true, false)), ("Dockerfile", "FROM node:20-alpine\n"));
        await RunAsync(host, orgId, linkId);

        var second = await ComponentsAsync(host, projectId);
        Assert.Equal(first.Select(c => c.Id).Order(), second.Select(c => c.Id).Order());
        Assert.Equal("4.17.21", second.Single(c => c.Name == "lodash").Version);
        Assert.Equal("0/1/0", await host.ScalarAsync<string>(
            "SELECT concat_ws('/', data->>'created', data->>'updated', data->>'removed') FROM platform.audit_event WHERE action = 'repository.indexed' ORDER BY at DESC LIMIT 1"));
        // The suggestion keeps its identity: still one, not a second for the same evidence.
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assets.component_suggestion"));

        // The same commit again: nothing is read.
        var reads = github.FileReads;
        await RunAsync(host, orgId, linkId);
        Assert.Equal(reads, github.FileReads);
    }

    [Fact]
    public async Task A_much_smaller_state_is_held_back_until_confirmed_and_a_failed_reading_removes_nothing()
    {
        var github = new ScriptedRepository();
        github.Commit("a1", Lockfile([.. Enumerable.Range(1, 12).Select(i => ($"pkg-{i}", "1.0.0", false, true))]));
        await using var host = await StartAsync(github);
        var (client, orgId, projectId, linkId) = await LinkAsync(host);
        using var _ = client;
        await RunAsync(host, orgId, linkId);
        Assert.Equal(12, (await ComponentsAsync(host, projectId)).Count);

        // The lockfile went missing in the new commit: 12 → 2 packages.
        github.Commit("b2", Lockfile(("pkg-1", "1.0.0", false, true), ("pkg-2", "1.0.0", false, true)));
        await RunAsync(host, orgId, linkId);
        Assert.Equal(12, (await ComponentsAsync(host, projectId)).Count);
        var link = (await Json(await client.GetAsync($"/api/v1/orgs/{orgId}/projects/{projectId}/repositories"))).EnumerateArray().Single();
        Assert.True(link.GetProperty("shrinkPending").GetBoolean());

        // A person confirms: the held-back state applies.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/orgs/{orgId}/projects/{projectId}/repositories/{linkId}/confirm-shrink")).StatusCode);
        await RunAsync(host, orgId, linkId, applyShrink: true);
        Assert.Equal(["pkg-1", "pkg-2"], (await ComponentsAsync(host, projectId)).Select(c => c.Name).Order());

        // GitHub refuses access: the link fails, the profile stays.
        github.Commit("c3", Lockfile(("pkg-9", "9.0.0", false, true)));
        github.Failure = RepositoryFailure.Forbidden;
        await RunAsync(host, orgId, linkId);
        Assert.Equal("failing", await host.ScalarAsync<string>($"SELECT state FROM assets.repository_link WHERE id = '{linkId}'"));
        Assert.Equal(["pkg-1", "pkg-2"], (await ComponentsAsync(host, projectId)).Select(c => c.Name).Order());
    }

    [Fact]
    public async Task Three_days_of_failure_raise_repository_unavailable_once()
    {
        var github = new ScriptedRepository { Failure = RepositoryFailure.NotFound };
        await using var host = await StartAsync(github);
        var (client, orgId, _, linkId) = await LinkAsync(host);
        using var _ = client;

        await RunAsync(host, orgId, linkId);
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.audit_event WHERE action = 'repository.unavailable'"));

        await host.ScalarAsync<int>($"UPDATE assets.repository_link SET failing_since = now() - interval '4 days' WHERE id = '{linkId}' RETURNING 1");
        await RunAsync(host, orgId, linkId);
        await RunAsync(host, orgId, linkId);
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.audit_event WHERE action = 'repository.unavailable'"));

        // Back to normal: the failure ends.
        github.Failure = null;
        github.Commit("a1", Lockfile(("lodash", "4.17.21", false, true)));
        await RunAsync(host, orgId, linkId);
        Assert.Equal("partial|", await host.ScalarAsync<string>($"SELECT state || '|' || coalesce(failing_since::text, '') FROM assets.repository_link WHERE id = '{linkId}'"));
    }

    [Fact]
    public async Task The_schedule_queues_due_links_in_their_organisation()
    {
        await using var host = await StartAsync(new ScriptedRepository());
        var (client, orgId, _, linkId) = await LinkAsync(host);
        using var _ = client;
        await host.ScalarAsync<int>("UPDATE platform.job SET state = 'succeeded' WHERE kind = 'assets.index-repository' RETURNING 1");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ScheduleDueRepositoriesHandler>()
                .HandleAsync(new ScheduleDueRepositories(), new JobContext(Guid.NewGuid(), ScheduleDueRepositories.Kind, 1, null), CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
        }

        Assert.Equal($"{orgId}", await host.ScalarAsync<string>("SELECT org_id::text FROM platform.job WHERE kind = 'assets.index-repository' AND state = 'pending'"));
        Assert.Contains(linkId.ToString(), await host.ScalarAsync<string>("SELECT payload::text FROM platform.job WHERE kind = 'assets.index-repository' AND state = 'pending'"), StringComparison.Ordinal);
    }

    private sealed record ComponentRow(Guid Id, string Name, string? Version, string Describe);

    private static async Task<List<ComponentRow>> ComponentsAsync(ApiTestHost host, Guid projectId)
    {
        var text = await host.ScalarAsync<string?>($"""
            SELECT string_agg(id || '|' || coalesce(product_key, custom_name) || '|' || coalesce(version, '') || '|' || dependency_scope || '|' || dependency_relation, E'\n' ORDER BY custom_name)
            FROM assets.component WHERE project_id = '{projectId}' AND purl IS NOT NULL
            """);
        return text is null ? [] : [.. text.Split('\n').Select(l => l.Split('|')).Select(p => new ComponentRow(Guid.Parse(p[0]), p[1], p[2], $"{p[1]} {p[2]} {p[3]} {p[4]}"))];
    }

    private static async Task RunAsync(ApiTestHost host, Guid orgId, Guid linkId, bool applyShrink = false)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetOrganization(orgId);
        await scope.ServiceProvider.GetRequiredService<IndexRepositoryHandler>()
            .HandleAsync(new IndexRepository(linkId, applyShrink), new JobContext(Guid.NewGuid(), IndexRepository.Kind, 1, orgId), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
    }

    private Task<ApiTestHost> StartAsync(ScriptedRepository github) =>
        ApiTestHost.StartAsync(server, services: services => services.AddScoped<IRepositoryHost>(_ => github));

    private static async Task<(ApiClient, Guid, Guid, Guid)> LinkAsync(ApiTestHost host)
    {
        var client = host.Client();
        await client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = "owner@example.org", displayName = "Owner", password = ApiTestHost.Password });
        var orgId = (await Json(await client.PostAsync("/api/v1/orgs", new { name = "Acme" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var projectId = (await Json(await client.PostAsync($"/api/v1/orgs/{orgId}/projects", new { name = "Portal" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var connectionId = (await Json(await client.PostAsync($"/api/v1/orgs/{orgId}/repository-connections", new { name = "Public" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var linkId = (await Json(await client.PostAsync($"/api/v1/orgs/{orgId}/projects/{projectId}/repositories", new { connectionId, owner = "acme", name = "portal" }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        return (client, orgId, projectId, linkId);
    }

    /// <summary>A package-lock.json v3 with the given packages; direct ones are declared by the root.</summary>
    private static (string Path, string Content) Lockfile(params (string Name, string Version, bool Dev, bool Direct)[] packages)
    {
        var root = new Dictionary<string, object>
        {
            ["name"] = "portal",
            ["dependencies"] = packages.Where(p => p.Direct && !p.Dev).ToDictionary(p => p.Name, p => "^" + p.Version),
            ["devDependencies"] = packages.Where(p => p.Direct && p.Dev).ToDictionary(p => p.Name, p => "^" + p.Version),
        };
        var entries = new Dictionary<string, object> { [""] = root };
        foreach (var (name, version, dev, _) in packages)
        {
            entries["node_modules/" + name] = dev ? new { version, dev = true } : new { version };
        }

        return ("package-lock.json", JsonSerializer.Serialize(new { name = "portal", lockfileVersion = 3, packages = entries }));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.Equal(status, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>A repository whose head moves to the last scripted commit; no SBOM (graph not enabled).</summary>
    private sealed class ScriptedRepository : IRepositoryHost
    {
        private readonly Dictionary<string, Dictionary<string, string>> _commits = [];
        private string? _head;

        public RepositoryFailure? Failure { get; set; }

        public int FileReads { get; private set; }

        public void Commit(string sha, params (string Path, string Content)[] files) =>
            (_commits[sha], _head) = (files.ToDictionary(f => f.Path, f => f.Content), sha);

        public Task<RepositoryResult<BranchHead>> GetBranchHeadAsync(RepositoryRef repository, string? etag, CancellationToken cancellationToken) =>
            Task.FromResult(Failure is { } failure ? RepositoryResult<BranchHead>.Fail(failure, $"GitHub says {failure}.")
                : _head is null ? RepositoryResult<BranchHead>.Fail(RepositoryFailure.EmptyRepository, "Empty.")
                : etag == $"\"{_head}\"" ? RepositoryResult<BranchHead>.Success(new BranchHead(true, null, etag))
                : RepositoryResult<BranchHead>.Success(new BranchHead(false, _head, $"\"{_head}\"")));

        public Task<RepositoryResult<T>> GetSbomAsync<T>(RepositoryRef repository, Func<Stream, CancellationToken, Task<T>> read, CancellationToken cancellationToken) =>
            Task.FromResult(RepositoryResult<T>.Fail(RepositoryFailure.DependencyGraphDisabled, "The dependency graph is not enabled."));

        public Task<RepositoryResult<RepositoryTree>> GetTreeAsync(RepositoryRef repository, string commitSha, CancellationToken cancellationToken) =>
            Task.FromResult(RepositoryResult<RepositoryTree>.Success(new RepositoryTree(commitSha,
                [.. _commits[commitSha].Select(f => new RepositoryFile(f.Key, Encoding.UTF8.GetByteCount(f.Value)))], false)));

        public Task<RepositoryResult<byte[]>> GetFileAsync(RepositoryRef repository, string commitSha, string path, int maxBytes, CancellationToken cancellationToken)
        {
            FileReads++;
            return Task.FromResult(RepositoryResult<byte[]>.Success(Encoding.UTF8.GetBytes(_commits[commitSha][path])));
        }
    }
}
