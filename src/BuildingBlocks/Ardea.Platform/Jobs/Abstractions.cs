namespace Ardea.Platform.Jobs;

public interface IJob
{
    static abstract string Kind { get; }
}

public interface IJobHandler<in TJob>
    where TJob : IJob
{
    Task HandleAsync(TJob job, JobContext context, CancellationToken cancellationToken);
}

public sealed record JobContext(Guid JobId, string Kind, int Attempt, Guid? OrganizationId);

public sealed record EnqueueOptions
{
    public string? CoalesceKey { get; init; }

    public TimeSpan? Delay { get; init; }

    public DateTimeOffset? RunAt { get; init; }

    public JobPriority? Priority { get; init; }

    public Guid? OrganizationId { get; init; }
}

public enum JobPriority : short
{
    Urgent = 0,
    Normal = 1,
    Background = 2,
}

public interface IJobQueue
{
    Task<bool> EnqueueAsync<TJob>(TJob job, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob;
}

public sealed record JobKindOptions
{
    public int MaxConcurrency { get; init; } = 4;

    public int MaxAttempts { get; init; } = 5;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    public JobPriority Priority { get; init; } = JobPriority.Normal;
}
