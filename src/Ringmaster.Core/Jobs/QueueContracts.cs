namespace Ringmaster.Core.Jobs;

public interface IQueueSelector
{
    Task<IReadOnlyList<QueueJobCandidate>> SelectRunnableJobsAsync(DateTimeOffset nowUtc, TimeSpan staleLeaseThreshold, CancellationToken cancellationToken);
}

public interface ILeaseManager
{
    Task<ILeaseHandle?> TryAcquireJobLeaseAsync(StoredJob job, string ownerId, CancellationToken cancellationToken);
    Task<ILeaseHandle?> TryAcquireRepoMutationLeaseAsync(string ownerId, CancellationToken cancellationToken);
    Task<ILeaseHandle?> TryAcquireSchedulerLeaseAsync(string ownerId, CancellationToken cancellationToken);
    Task<LeaseRecord?> ReadJobLeaseAsync(StoredJob job, CancellationToken cancellationToken);
}

public interface ILeaseHandle : IAsyncDisposable
{
    string OwnerId { get; }
    Task RenewAsync(string? runId, CancellationToken cancellationToken);
}

public interface INotificationSink
{
    Task NotifyAsync(NotificationRecord notification, CancellationToken cancellationToken);
}

public sealed record class QueueJobCandidate
{
    public required StoredJob Job { get; init; }
    public bool ResumeExistingState { get; init; }
}

public sealed record class QueueRunOptions
{
    public int MaxParallelJobs { get; init; } = 1;
    public int MaxConcurrentCodexRuns { get; init; } = 1;
    public int MaxConcurrentVerificationRuns { get; init; } = 1;
    public int MaxConcurrentPrOperations { get; init; } = 1;
    public TimeSpan LeaseHeartbeatInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan StaleLeaseThreshold { get; init; } = TimeSpan.FromSeconds(10);
    public required string OwnerId { get; init; }
}

public sealed record class QueuePassResult
{
    public DateTimeOffset StartedAtUtc { get; init; }
    public IReadOnlyList<QueueJobResult> Jobs { get; init; } = [];
}

public sealed record class QueueJobResult
{
    public required string JobId { get; init; }
    public required QueueJobDisposition Disposition { get; init; }
    public JobState? FinalState { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public sealed record class LeaseRecord
{
    public required string OwnerId { get; init; }
    public required DateTimeOffset AcquiredAtUtc { get; init; }
    public required DateTimeOffset HeartbeatAtUtc { get; init; }
    public int ProcessId { get; init; }
    public string? MachineName { get; init; }
    public string? RunId { get; init; }
}

public sealed record class NotificationRecord
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string EventType { get; init; }
    public string? JobId { get; init; }
    public JobState? State { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public enum QueueJobDisposition
{
    Started,
    SkippedLeaseHeld,
    SkippedRepoLocked,
    FailedToStart,
}
