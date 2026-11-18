using System.Text.Json;

namespace Ardea.Platform.Persistence.Entities;

/// <summary>States of a job (master plan 3.4).</summary>
internal enum JobState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Dead,
}

internal static class JobStates
{
    public static string ToDatabase(JobState state) => state switch
    {
        JobState.Pending => "pending",
        JobState.Running => "running",
        JobState.Succeeded => "succeeded",
        JobState.Failed => "failed",
        JobState.Dead => "dead",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static JobState FromDatabase(string value) => value switch
    {
        "pending" => JobState.Pending,
        "running" => JobState.Running,
        "succeeded" => JobState.Succeeded,
        "failed" => JobState.Failed,
        "dead" => JobState.Dead,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown job state."),
    };
}

/// <summary>A row of <c>platform.job</c>: queue entry and outbox at once (master plan 3.4).</summary>
internal sealed class JobRow
{
    public Guid Id { get; set; }

    public required string Kind { get; set; }

    public JsonDocument Payload { get; set; } = JsonDocument.Parse("{}");

    public string? CoalesceKey { get; set; }

    public DateTimeOffset RunAt { get; set; }

    /// <summary>0 urgent, 1 normal, 2 background.</summary>
    public short Priority { get; set; } = 1;

    public JobState State { get; set; }

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public string? LastError { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseUntil { get; set; }

    public int TimeoutSeconds { get; set; } = 300;

    public Guid? OrgId { get; set; }

    public string? TraceParent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>A recurring task of the instance with cron expression and time zone (master plan 3.4).</summary>
internal sealed class ScheduleRow
{
    public required string Key { get; set; }

    public required string JobKind { get; set; }

    public required string Cron { get; set; }

    public string TimeZone { get; set; } = "UTC";

    public bool Enabled { get; set; } = true;

    public DateTimeOffset? LastRunAt { get; set; }

    public DateTimeOffset NextRunAt { get; set; }
}

/// <summary>An instance setting; secrets live encrypted in <see cref="SecretValue"/>.</summary>
internal sealed class SettingRow
{
    public required string Key { get; set; }

    public JsonDocument? Value { get; set; }

    public string? SecretValue { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>An audit event; immutable for the application (master plan 9.4).</summary>
internal sealed class AuditEventRow
{
    public Guid Id { get; set; }

    /// <summary>Empty for events of the instance.</summary>
    public Guid? OrganizationId { get; set; }

    public Guid? ActorUserId { get; set; }

    public required string Action { get; set; }

    public string? SubjectType { get; set; }

    public string? SubjectId { get; set; }

    public JsonDocument? Data { get; set; }

    /// <summary>Where the change came from: <c>ui</c>, <c>api</c>, <c>worker</c>, <c>cli</c>, a commit, ...</summary>
    public required string Origin { get; set; }

    public string? IpAddress { get; set; }

    public DateTimeOffset At { get; set; }
}

/// <summary>A key of ASP.NET Core Data Protection, stored encrypted with the master key.</summary>
internal sealed class DataProtectionKeyRow
{
    public int Id { get; set; }

    public string? FriendlyName { get; set; }

    public string? Xml { get; set; }
}

/// <summary>Liveness of a worker process; the worker health check reads it (master plan 12.3).</summary>
internal sealed class WorkerHeartbeatRow
{
    public required string WorkerId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset BeatAt { get; set; }

    public string? Version { get; set; }
}
