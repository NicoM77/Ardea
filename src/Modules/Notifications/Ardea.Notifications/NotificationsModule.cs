using Ardea.Assessment.Contracts;
using Ardea.Iam.Contracts;
using Ardea.Intel.Contracts;
using Ardea.Notifications.Delivering;
using Ardea.Notifications.Evaluation;
using Ardea.Notifications.Features;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Http;
using Ardea.Platform.Jobs;
using Ardea.Platform.Modules;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Notifications;

public sealed class NotificationsModule : IModule
{
    public string Name => NotifyDbContext.Schema;

    public static IReadOnlyList<string> ConfigurationErrors(IConfiguration configuration) => PushConfiguration.Errors(configuration);

    public IModuleDatabase? Database { get; } =
        new ModuleDatabase<NotifyDbContext>(
            NotifyDbContext.Schema,
            options => new NotifyDbContext(options, new TenantContext()),
            seed: (db, _, cancellationToken) => DefaultRules.NameNoticesAsync(db, cancellationToken));

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<NotifyDbContext>(NotifyDbContext.Schema);
        services.AddScoped<IActionEventSubscriber, ActionEventInbox>();
        services.AddScoped<IEntryChangeSubscriber, WarningSubscriber>();
        services.AddScoped<IOrganizationCreatedSubscriber, DefaultRules>();
        services.AddScoped<IOrganizationTimeZoneSubscriber, DigestRescheduler>();
        services.AddScoped<Evaluator>();
        services.AddSingleton<IDemoSeedStep, Features.DemoSeed>();
        services.AddScoped<Platform.Status.IInstanceStatusContributor, NotifyStatusContributor>();
        services.AddScoped<DeliveryReader>();
        services.AddScoped<EmailChannel>();
        services.AddScoped<WebhookChannel>();
        services.AddScoped<INotificationChannel>(sp => sp.GetRequiredService<EmailChannel>());
        services.AddScoped<INotificationChannel>(sp => sp.GetRequiredService<WebhookChannel>());
        services.AddSingleton<VapidKeyStore>();
        services.AddSingleton<PushOptions>();
        services.AddScoped<WebPushChannel>();
        services.AddScoped<INotificationChannel>(sp => sp.GetRequiredService<WebPushChannel>());
        services.AddHttpClient(WebPushChannel.ClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(OutboundAddresses.CreateHandler)
            .RemoveAllLoggers();
        services.AddHttpClient(WebhookChannel.ClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(OutboundAddresses.CreateHandler)
            .RemoveAllLoggers();

        services.AddJob<EvaluateEvents, EvaluateEventsHandler>(new JobKindOptions { MaxConcurrency = 4, MaxAttempts = 8, Timeout = TimeSpan.FromMinutes(2), Priority = JobPriority.Urgent });
        services.AddJob<Deliver, DeliverHandler>(Deliver.KindOptions);
        services.AddJob<SendOverflow, SendOverflowHandler>(new JobKindOptions { MaxConcurrency = 2, MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(1) });
        services.AddJob<DistributeWarning, DistributeWarningHandler>(new JobKindOptions { MaxConcurrency = 1, MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(1) });
        services.AddJob<ReceiveWarning, ReceiveWarningHandler>(new JobKindOptions { MaxConcurrency = 2, MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(1) });
        services.AddJob<FindDueDigests, FindDueDigestsHandler>(new JobKindOptions { MaxConcurrency = 1, MaxAttempts = 1, Timeout = TimeSpan.FromMinutes(1), Priority = JobPriority.Urgent });
        services.AddJob<SendDigest, SendDigestHandler>(new JobKindOptions { MaxConcurrency = 2, MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(2) });
        services.AddJob<SendReminders, SendRemindersHandler>(new JobKindOptions { MaxConcurrency = 2, MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(2) });
        services.AddJob<SendAddressConfirmation, SendAddressConfirmationHandler>(new JobKindOptions { MaxAttempts = 5, Timeout = TimeSpan.FromMinutes(1) });
        services.AddSchedule("notify.find-due-digests", "* * * * *", new FindDueDigests());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RuleEndpoints.Map(endpoints);
        EndpointEndpoints.Map(endpoints);
        AddressEndpoints.Map(endpoints);
        DeliveryEndpoints.Map(endpoints);
        InboxEndpoints.Map(endpoints);
        PushEndpoints.Map(endpoints);
        ReminderEndpoints.Map(endpoints);
    }
}
