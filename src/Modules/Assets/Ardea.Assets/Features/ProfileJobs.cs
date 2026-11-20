using Ardea.Assets.Contracts;
using Ardea.Platform.Jobs;
using Ardea.Platform.Tenancy;

namespace Ardea.Assets.Features;

internal sealed record ProjectProfileChanged(Guid ProjectId) : IJob
{
    public static string Kind => "assets.profile-changed";

    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(30);

    public static EnqueueOptions Options(Guid projectId) =>
        new() { CoalesceKey = $"assets.profile-changed:{projectId}", Delay = Delay };
}

internal sealed class ProjectProfileChangedHandler(IEnumerable<IProfileChangeSubscriber> subscribers, ITenantContext tenant) : IJobHandler<ProjectProfileChanged>
{
    public async Task HandleAsync(ProjectProfileChanged job, JobContext context, CancellationToken cancellationToken)
    {
        var change = new ProfileChanged(tenant.RequiredOrganizationId, job.ProjectId);
        foreach (var subscriber in subscribers)
        {
            await subscriber.OnProfileChangedAsync(change, cancellationToken);
        }
    }
}
