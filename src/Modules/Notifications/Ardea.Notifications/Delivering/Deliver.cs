using Ardea.Iam.Contracts;
using Ardea.Notifications.Messages;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Jobs;
using Ardea.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ardea.Notifications.Delivering;

/// <summary>
/// <c>Deliver(deliveryId)</c> of master plan 3.3: sends one message. Retries, waiting times and giving up come
/// from the job system (master plan 8.6); the delivery row is the log.
/// </summary>
internal sealed record Deliver(Guid DeliveryId) : IJob
{
    public static string Kind => "notify.deliver";

    /// <summary>With the job system's back-off (10 s doubling, at most one hour) the last attempt comes about 3.5 hours after the first.</summary>
    public const int MaxAttempts = 12;

    public static readonly JobKindOptions KindOptions = new() { MaxConcurrency = 8, MaxAttempts = MaxAttempts, Timeout = TimeSpan.FromMinutes(1) };

    public static EnqueueOptions Options(Guid deliveryId, DateTimeOffset runAt) => new() { CoalesceKey = $"notify.deliver:{deliveryId}", RunAt = runAt };
}

/// <summary>A transient failure: the job system tries again after its back-off.</summary>
internal sealed class DeliveryRetryException(string message) : Exception(message);

/// <summary>
/// Three steps (master plan 9.2): read and mark in a short unit of work, call the channel without a
/// transaction, write the outcome in the job's own unit of work. A transient failure is written in a unit
/// of work of its own, because the job's is rolled back when it throws for the retry.
/// </summary>
internal sealed partial class DeliverHandler(
    IUnitOfWorkFactory units,
    IEnumerable<INotificationChannel> channels,
    NotifyDbContext db,
    IOrganizationMembers members,
    TimeProvider time,
    ILogger<DeliverHandler> logger) : IJobHandler<Deliver>
{
    public async Task HandleAsync(Deliver job, JobContext context, CancellationToken cancellationToken)
    {
        DeliveryStart? start;
        await using (var scope = units.CreateScope())
        {
            start = await scope.ServiceProvider.GetRequiredService<DeliveryReader>().StartAsync(job.DeliveryId, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        }

        if (start is null)
        {
            return;
        }

        var delivery = await db.Deliveries.SingleAsync(d => d.Id == job.DeliveryId, cancellationToken);
        if (start.Message is not { } message)
        {
            Settle(delivery, "suppressed", start.Suppress);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (message.Channel == Channels.InApp)
        {
            // A digest in the app is a row like any other notification; nothing leaves the instance.
            var text = MessageRenderer.InAppDigest(message.Content);
            db.Notifications.Add(new Notification
            {
                Id = Guid.CreateVersion7(),
                UserId = message.Recipient.Id,
                EventId = delivery.Id,
                Kind = "digest",
                Level = message.Content.Level,
                Title = text.Title,
                Body = text.Body,
                Link = text.Link,
                CreatedAt = time.GetUtcNow(),
            });
            delivery.Attempts = context.Attempt;
            Settle(delivery, "sent", null);
            delivery.SentAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var channel = channels.Single(c => c.Name == message.Channel);
        var result = await channel.SendAsync(message, cancellationToken);
        delivery.Attempts = context.Attempt;
        await RecordEndpointAsync(message, result, cancellationToken);
        switch (result.Outcome)
        {
            case SendOutcome.Sent:
                Settle(delivery, "sent", null);
                delivery.SentAt = time.GetUtcNow();
                break;
            case SendOutcome.Suppressed:
                Settle(delivery, "suppressed", result.Error);
                break;
            case SendOutcome.Permanent:
                await GiveUpAsync(delivery, result.Error, cancellationToken);
                break;
            case SendOutcome.Transient when context.Attempt >= Deliver.MaxAttempts:
                await GiveUpAsync(delivery, result.Error, cancellationToken);
                break;
            case SendOutcome.Transient:
                await RecordRetryAsync(job.DeliveryId, context.Attempt, result, message, cancellationToken);
                LogRetry(job.DeliveryId, message.Channel, context.Attempt);
                throw new DeliveryRetryException(result.Error ?? "The channel failed for now.");
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Settle(Delivery delivery, string state, string? error)
    {
        delivery.State = state;
        delivery.LastError = error is null ? null : Truncate(error);
    }

    private async Task GiveUpAsync(Delivery delivery, string? error, CancellationToken cancellationToken)
    {
        Settle(delivery, "dead", error);
        LogGaveUp(delivery.Id, delivery.Channel);
        await WarnOwnersAsync(delivery, cancellationToken);
    }

    /// <summary>
    /// Level 5 that reached a recipient on none of their channels: the owners get an in-app warning (master plan 8.6).
    /// </summary>
    private async Task WarnOwnersAsync(Delivery delivery, CancellationToken cancellationToken)
    {
        var critical = await db.DeliveryEvents.AsNoTracking().Where(e => e.DeliveryId == delivery.Id && e.Level == 5).Select(e => e.EventId).ToListAsync(cancellationToken);
        if (critical.Count == 0)
        {
            return;
        }

        var reachedElsewhere = await db.DeliveryEvents.AsNoTracking()
            .Where(e => e.RecipientKey == delivery.RecipientKey && e.Channel != delivery.Channel && critical.Contains(e.EventId))
            .Join(db.Deliveries.AsNoTracking().Where(d => d.State != "dead" && d.State != "suppressed"), e => e.DeliveryId, d => d.Id, (e, d) => d.Id)
            .AnyAsync(cancellationToken);
        if (reachedElsewhere)
        {
            return;
        }

        var texts = MessageTexts.For(null);
        var now = time.GetUtcNow();
        foreach (var owner in (await members.ListAsync(cancellationToken)).Where(m => m.Role == "owner"))
        {
            db.Notifications.Add(new Notification
            {
                Id = Guid.CreateVersion7(),
                UserId = owner.UserId,
                EventId = delivery.Id,
                Kind = NotifyEvents.DeliveryFailed,
                Level = 5,
                Title = texts["headline.delivery_failed"],
                Body = $"{delivery.Channel}: {delivery.LastError}",
                CreatedAt = now,
            });
        }
    }

    private async Task RecordRetryAsync(Guid deliveryId, int attempt, SendResult result, Outgoing message, CancellationToken cancellationToken)
    {
        await using var scope = units.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<NotifyDbContext>();
        var delivery = await store.Deliveries.SingleAsync(d => d.Id == deliveryId, cancellationToken);
        delivery.State = "failed";
        delivery.Attempts = attempt;
        delivery.LastError = Truncate(result.Error ?? "Failed for now.");
        if (message.Recipient.Kind == RecipientKinds.Endpoint && result.Status is { } status)
        {
            var endpoint = await store.Endpoints.SingleOrDefaultAsync(e => e.Id == message.Recipient.Id, cancellationToken);
            endpoint?.LastStatus = status;
            endpoint?.LastStatusAt = time.GetUtcNow();
        }

        await store.SaveChangesAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
    }

    private async Task RecordEndpointAsync(Outgoing message, SendResult result, CancellationToken cancellationToken)
    {
        if (message.Recipient.Kind == RecipientKinds.Endpoint && result.Status is { } status && result.Outcome != SendOutcome.Transient)
        {
            var endpoint = await db.Endpoints.SingleOrDefaultAsync(e => e.Id == message.Recipient.Id, cancellationToken);
            endpoint?.LastStatus = status;
            endpoint?.LastStatusAt = time.GetUtcNow();
        }
    }

    private static string Truncate(string error) => error.Length <= 2000 ? error : error[..2000];

    [LoggerMessage(EventId = 6101, Level = LogLevel.Warning, Message = "Delivery {DeliveryId} on {Channel} failed for now (attempt {Attempt}); the job system retries")]
    private partial void LogRetry(Guid deliveryId, string channel, int attempt);

    [LoggerMessage(EventId = 6102, Level = LogLevel.Warning, Message = "Delivery {DeliveryId} on {Channel} was given up")]
    private partial void LogGaveUp(Guid deliveryId, string channel);
}
