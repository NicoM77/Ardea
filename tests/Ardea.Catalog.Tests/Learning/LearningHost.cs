using Ardea.Catalog.Contracts;
using Ardea.Catalog.Learning;
using Ardea.Catalog.Tests.Products;
using Ardea.Platform.Ai;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Ardea.Platform.Secrets;
using Ardea.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Ardea.Catalog.Tests.Learning;

internal sealed class LearningHost : IAsyncDisposable
{
    private LearningHost(ServiceProvider services, CatalogDatabase database, FakeTimeProvider clock)
    {
        Services = services;
        Database = database;
        Clock = clock;
    }

    public ServiceProvider Services { get; }

    public CatalogDatabase Database { get; }

    public FakeTimeProvider Clock { get; }

    public FakeFeed Feed => Services.GetRequiredService<FakeFeed>();

    public RecordingSubscriber Recomputed => Services.GetRequiredService<RecordingSubscriber>();

    public static async Task<LearningHost> StartAsync(
        PostgresServer server, IAiProviders? providers = null, IReadOnlyDictionary<string, string?>? settings = null, LearningOptions? options = null)
    {
        var database = await CatalogDatabase.CreateAsync(server);
        await using (var owner = await database.OpenOwnerAsync())
        {
            await database.Migrator(new MasterDataOptions(MasterDataOptions.FindDefault())).MigrateAsync(owner, CancellationToken.None);
        }

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(database.Options);
        services.AddSingleton(_ => NpgsqlDataSource.Create(database.Options.AppConnectionString("catalog-tests")));
        services.AddSingleton<ISecretProtector, PlainSecrets>();
        services.AddUnitOfWork();
        services.AddJobQueue();
        if (providers is not null)
        {
            services.AddSingleton(providers);
        }

        new CatalogModule().AddServices(services, configuration);
        if (options is not null)
        {
            services.AddSingleton(options);
        }

        services.AddSingleton<FakeFeed>();
        services.AddSingleton<IUnknownProductFeed>(sp => sp.GetRequiredService<FakeFeed>());
        services.AddSingleton<RecordingSubscriber>();
        services.AddSingleton<ICatalogChangeSubscriber>(sp => sp.GetRequiredService<RecordingSubscriber>());
        return new LearningHost(services.BuildServiceProvider(validateScopes: true), database, clock);
    }

    public async Task GrowAsync(bool collect = true, string? rulesAfter = null)
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<GrowCatalogHandler>()
            .HandleAsync(new GrowCatalog(collect, rulesAfter), new JobContext(Guid.CreateVersion7(), GrowCatalog.Kind, 1, null), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
    }

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        var result = await work(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
        scope.ServiceProvider.GetRequiredService<Ardea.Catalog.Products.CatalogCache>().Invalidate();
        return result;
    }

    public async Task<IReadOnlyList<string>> RowsAsync(string sql)
    {
        await using var connection = await Database.OpenOwnerAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(string.Join('\t', Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? string.Empty : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await Database.OpenOwnerAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public Task<IReadOnlyList<string>> CuratedAsync() => RowsAsync("""
        SELECT key || '|' || name || '|' || coalesce(vendor, '') || '|' || kind || '|' || array_to_string(cpe_pairs, ',') || '|' || array_to_string(purls, ',')
               || '|' || aliases::text || '|' || version_scheme || '|' || coalesce(platform_key, '') || '|' || array_to_string(related_keys, ',') || '|' || state || '|' || updated_at::text
        FROM catalog.product WHERE origin = 'curated' ORDER BY key
        """);

    public ValueTask DisposeAsync() => Services.DisposeAsync();

    private sealed class PlainSecrets : ISecretProtector
    {
        public string Protect(string purpose, string plaintext) => plaintext;

        public string? Unprotect(string purpose, string protectedValue) => protectedValue;
    }
}

internal sealed class FakeFeed : IUnknownProductFeed
{
    public List<UnknownProductStatement> Unknown { get; } = [];

    public Dictionary<string, string[]> Titles { get; } = [];

    public void Add(string? vendor, string product, int records = 3, string[]? cpe = null, string? purl = null, bool kev = false) =>
        Unknown.Add(new UnknownProductStatement(ProductKey.Create(vendor, product).Value, vendor, product, cpe ?? [], purl, records, kev));

    public Task<UnknownProductCensus> CollectAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new UnknownProductCensus(Unknown.Count * 4, [.. Unknown]));

    public Task<IReadOnlyList<string>> TitlesAsync(string rawKey, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([.. Titles.GetValueOrDefault(rawKey, []).Take(limit)]);
}

internal sealed class RecordingSubscriber : ICatalogChangeSubscriber
{
    public List<LearnedEntriesChanged> Changes { get; } = [];

    public IReadOnlyList<string> RawKeys => [.. Changes.SelectMany(c => c.RawKeys).Order(StringComparer.Ordinal)];

    public Task OnLearnedEntriesChangedAsync(LearnedEntriesChanged change, CancellationToken cancellationToken)
    {
        lock (Changes)
        {
            Changes.Add(change);
        }

        return Task.CompletedTask;
    }
}

internal sealed class RecordedModel(Func<string, string> answer) : IChatClient
{
    private readonly List<string> _requests = [];

    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var text = string.Join("\n", messages.Select(m => m.Text));
        lock (_requests)
        {
            _requests.Add(text);
        }

        Assert.Null(options?.Tools);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer(text)))
        {
            Usage = new UsageDetails { InputTokenCount = 900, OutputTokenCount = 120 },
        });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class RecordedProviders(params (string Name, bool Local, RecordedModel Model)[] providers) : IAiProviders
{
    public IReadOnlyList<AiProviderInfo> All { get; } = [.. providers.Select(p => new AiProviderInfo(p.Name, p.Local ? "ollama" : "anthropic", p.Name + "-model", p.Local, null))];

    public IChatClient? Client(string name) => providers.FirstOrDefault(p => p.Name == name).Model;

    public Task<AiConnectionResult?> TestAsync(string name, CancellationToken cancellationToken) => Task.FromResult<AiConnectionResult?>(null);
}
