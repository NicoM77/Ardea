namespace Ardea.Platform.Jobs;

/// <summary>
/// A job's payload. It carries identifiers only, never contents (master plan 4.1). The kind names
/// the job in the queue and in the administration.
/// </summary>
public interface IJob
{
    static abstract string Kind { get; }
}

/// <summary>Handles one kind of job. A handler is idempotent: the job system may run it more than once.</summary>
public interface IJobHandler<in TJob>
    where TJob : IJob
{
    Task HandleAsync(TJob job, JobContext context, CancellationToken cancellationToken);
}

public sealed record JobContext(Guid JobId, string Kind, int Attempt, Guid? OrganizationId);

/// <summary>How a job is enqueued; every value has a default from the job kind's registration.</summary>
public sealed record EnqueueOptions
{
    /// <summary>At most one waiting job per key (master plan 3.4); a running or finished one blocks nothing.</summary>
    public string? CoalesceKey { get; init; }

    public TimeSpan? Delay { get; init; }

    public DateTimeOffset? RunAt { get; init; }

    public JobPriority? Priority { get; init; }

    /// <summary>The organisation whose tenant context the job runs in.</summary>
    public Guid? OrganizationId { get; init; }
}

public enum JobPriority : short
{
    Urgent = 0,
    Normal = 1,
    Background = 2,
}

/// <summary>Enqueues jobs inside the current unit of work: a job exists exactly when its cause is committed.</summary>
public interface IJobQueue
{
    /// <summary>Returns <c>false</c> when a waiting job with the same coalesce key already exists.</summary>
    Task<bool> EnqueueAsync<TJob>(TJob job, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob;
}

/// <summary>Registration of a job kind.</summary>
public sealed record JobKindOptions
{
    public int MaxConcurrency { get; init; } = 4;

    /// <summary>One attempt means no retry: the cause schedules the next run itself (e.g. source fetches).</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Hard limit for one execution; a job over it is cancelled and counts as failed.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    public JobPriority Priority { get; init; } = JobPriority.Normal;
}
