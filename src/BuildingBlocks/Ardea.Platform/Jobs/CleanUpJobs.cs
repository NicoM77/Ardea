using Ardea.Platform.Persistence;

namespace Ardea.Platform.Jobs;

/// <summary>Removes finished jobs after their retention (master plan 3.4).</summary>
public sealed record CleanUpJobs : IJob
{
    public static string Kind => "platform.clean-up-jobs";
}

internal sealed class CleanUpJobsHandler(IUnitOfWork unitOfWork) : IJobHandler<CleanUpJobs>
{
    public async Task HandleAsync(CleanUpJobs job, JobContext context, CancellationToken cancellationToken) =>
        await JobStore.CleanUpAsync(await unitOfWork.GetConnectionAsync(cancellationToken), cancellationToken);
}
