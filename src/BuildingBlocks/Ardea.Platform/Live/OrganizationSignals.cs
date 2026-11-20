using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ardea.Platform.Live;

public interface IOrganizationSignals
{
    OrganizationSubscription Subscribe(Guid organizationId);
}

public sealed class OrganizationSubscription : IDisposable
{
    private readonly Action<OrganizationSubscription> _unsubscribe;
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    internal OrganizationSubscription(Guid organizationId, Action<OrganizationSubscription> unsubscribe)
    {
        OrganizationId = organizationId;
        _unsubscribe = unsubscribe;
    }

    public Guid OrganizationId { get; }

    public ChannelReader<string> Reader => _channel.Reader;

    internal void Signal(string kind) => _channel.Writer.TryWrite(kind);

    public void Dispose()
    {
        _unsubscribe(this);
        _channel.Writer.TryComplete();
    }
}

internal sealed partial class OrganizationSignals(Persistence.DatabaseOptions database, TimeProvider time, ILogger<OrganizationSignals> logger)
    : BackgroundService, IOrganizationSignals
{
    public const string Channel = "ardea_org_changed";

    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<OrganizationSubscription, byte>> _subscribers = new();

    public OrganizationSubscription Subscribe(Guid organizationId)
    {
        var subscription = new OrganizationSubscription(organizationId, Unsubscribe);
        _subscribers.GetOrAdd(organizationId, _ => new()).TryAdd(subscription, 0);
        return subscription;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reconnect = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(database.AppConnectionString("ardea-live"))
                {
                    Pooling = false,
                    KeepAlive = 30,
                }.ConnectionString);
                await connection.OpenAsync(stoppingToken);
                connection.Notification += (_, e) => Publish(e.Payload, "changed");
                await using (var listen = new NpgsqlCommand($"LISTEN {Channel}", connection))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }

                if (reconnect)
                {
                    foreach (var organization in _subscribers.Keys)
                    {
                        Publish(organization.ToString(), "resync");
                    }
                }

                LogListening(Channel);
                while (true)
                {
                    await connection.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogLost(ex);
                reconnect = true;
                await Task.Delay(RetryDelay, time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    internal void Publish(string payload, string kind)
    {
        if (Guid.TryParse(payload, out var organization) && _subscribers.TryGetValue(organization, out var subscriptions))
        {
            foreach (var subscription in subscriptions.Keys)
            {
                subscription.Signal(kind);
            }
        }
    }

    private void Unsubscribe(OrganizationSubscription subscription)
    {
        if (_subscribers.TryGetValue(subscription.OrganizationId, out var subscriptions))
        {
            subscriptions.TryRemove(subscription, out _);
        }
    }

    [LoggerMessage(EventId = 1301, Level = LogLevel.Information, Message = "Listening for live signals on {Channel}")]
    private partial void LogListening(string channel);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Warning, Message = "Live signals interrupted; reconnecting")]
    private partial void LogLost(Exception exception);
}

public static class OrganizationSignalServices
{
    public static IServiceCollection AddOrganizationSignals(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(OrganizationSignals)))
        {
            return services;
        }

        services.AddSingleton<OrganizationSignals>();
        services.AddSingleton<IOrganizationSignals>(sp => sp.GetRequiredService<OrganizationSignals>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<OrganizationSignals>());
        return services;
    }
}
