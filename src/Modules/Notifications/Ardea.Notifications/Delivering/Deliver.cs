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
/// transaction, write the outcome afterwards. The outcome goes into the job's own unit of work when the job
/// ends here; when it is retried, or ran out of time during the call, into a unit of work of its own, because
/// the job's is rolled back when it throws. Nothing touches the database between the first step and the call.
/// </summary>
internal sealed partial class DeliverHandler(
    IUnitOfWorkFactory units,
    IEnumerable<INotificationChannel> channels,
    NotifyDbContext db,
    IServiceProvider services,
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

        if (start.Message is not { } message)
        {
            var suppressed = await db.Deliveries.SingleAsync(d => d.Id == job.DeliveryId, cancellationToken);
            Settle(suppressed, "suppressed", start.Suppress);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (message.Channel == Channels.InApp)
        {
            // A digest or a run of the reminders by level in the app is a row like any other notification; nothing leaves the instance.
            var delivery = await db.Deliveries.SingleAsync(d => d.Id == job.DeliveryId, cancellationToken);
            var reminders = message.Content.Kind == "reminder";
            var text = reminders ? MessageRenderer.InAppReminders(message.Content) : MessageRenderer.InAppDigest(message.Content);
            db.Notifications.Add(new Notification
            {
                Id = Guid.CreateVersion7(),
                UserId = message.Recipient.Id,
                EventId = delivery.Id,
                Kind = reminders ? NotifyEvents.ReminderDue : "digest",
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

        // The call to the outside: no transaction is open while it waits (master plan 9.2, ADR 041).
        var channel = channels.Single(c => c.Name == message.Channel);
        SendResult result;
        try
        {
            result = await channel.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A channel answers with a result; should one throw all the same, the delivery must not stay in «sending».
            result = new SendResult(SendOutcome.Transient, $"{ex.GetType().Name}: the channel failed.");
        }

        var timedOut = cancellationToken.IsCancellationRequested;
        var retry = result.Outcome == SendOutcome.Transient && context.Attempt < Deliver.MaxAttempts;
        if (timedOut || retry)
        {
            // The job's unit of work ends without a commit; what the call found must stay all the same: which devices
            // took the message, which subscriptions are gone, and that a retry is due.
            await using var scope = units.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<NotifyDbContext>();
            await WriteAsync(store, scope.ServiceProvider, job.DeliveryId, context.Attempt, message, result, CancellationToken.None);
            await store.SaveChangesAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            LogRetry(job.DeliveryId, message.Channel, context.Attempt);
            throw new DeliveryRetryException(result.Error ?? "The channel failed for now.");
        }

        await WriteAsync(db, services, job.DeliveryId, context.Attempt, message, result, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Writes what the call found into the delivery, the endpoint and the push subscriptions.</summary>
    private async Task WriteAsync(
        NotifyDbContext store, IServiceProvider scope, Guid deliveryId, int attempt, Outgoing message, SendResult result, CancellationToken cancellationToken)
    {
        var delivery = await store.Deliveries.SingleAsync(d => d.Id == deliveryId, cancellationToken);
        delivery.Attempts = attempt;
        if (message.Recipient.Kind == RecipientKinds.Endpoint && result.Status is { } status)
        {
            var endpoint = await store.Endpoints.SingleOrDefaultAsync(e => e.Id == message.Recipient.Id, cancellationToken);
            endpoint?.LastStatus = status;
            endpoint?.LastStatusAt = time.GetUtcNow();
        }

        if (result.Devices is { } devices)
        {
            // Also before a retry: a subscription that is gone goes now, a device that took the message counts it.
            await PushChannel.RecordAsync(store, devices, cancellationToken);
        }

        switch (result.Outcome)
        {
            case SendOutcome.Sent:
                Settle(delivery, "sent", null);
                delivery.SentAt = time.GetUtcNow();
                break;
            case SendOutcome.Suppressed:
                Settle(delivery, "suppressed", result.Error);
                break;
            case SendOutcome.Transient when attempt < Deliver.MaxAttempts:
                Settle(delivery, "failed", result.Error ?? "Failed for now.");
                break;
            default:
                Settle(delivery, "dead", result.Error);
                LogGaveUp(delivery.Id, delivery.Channel);
                await WarnOwnersAsync(store, scope.GetRequiredService<IOrganizationMembers>(), delivery, cancellationToken);
                break;
        }
    }

    private static void Settle(Delivery delivery, string state, string? error)
    {
        delivery.State = state;
        delivery.LastError = error is null ? null : Truncate(error);
    }

    /// <summary>
    /// Level 5 that reached a recipient on none of their channels: the owners get an in-app warning (master plan 8.6).
    /// </summary>
    private async Task WarnOwnersAsync(NotifyDbContext store, IOrganizationMembers members, Delivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Kind != "immediate")
        {
            // A digest or a reminder repeats what was announced at once before; its failure is in the log.
            return;
        }

        var critical = await store.DeliveryEvents.AsNoTracking().Where(e => e.DeliveryId == delivery.Id && e.Level == 5).Select(e => e.EventId).ToListAsync(cancellationToken);
        if (critical.Count == 0)
        {
            return;
        }

        var reachedElsewhere = await store.DeliveryEvents.AsNoTracking()
            .Where(e => e.RecipientKey == delivery.RecipientKey && e.Channel != delivery.Channel && critical.Contains(e.EventId))
            .Join(store.Deliveries.AsNoTracking().Where(d => d.State != "dead" && d.State != "suppressed"), e => e.DeliveryId, d => d.Id, (e, d) => d.Id)
            .AnyAsync(cancellationToken);
        if (reachedElsewhere)
        {
            return;
        }

        var texts = MessageTexts.For(null);
        var now = time.GetUtcNow();
        foreach (var owner in (await members.ListAsync(cancellationToken)).Where(m => m.Role == "owner"))
        {
            store.Notifications.Add(new Notification
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

    private WebPushChannel PushChannel => channels.OfType<WebPushChannel>().Single();

    private static string Truncate(string error) => error.Length <= 2000 ? error : error[..2000];

    [LoggerMessage(EventId = 6101, Level = LogLevel.Warning, Message = "Delivery {DeliveryId} on {Channel} failed for now (attempt {Attempt}); the job system retries")]
    private partial void LogRetry(Guid deliveryId, string channel, int attempt);

    [LoggerMessage(EventId = 6102, Level = LogLevel.Warning, Message = "Delivery {DeliveryId} on {Channel} was given up")]
    private partial void LogGaveUp(Guid deliveryId, string channel);
}
