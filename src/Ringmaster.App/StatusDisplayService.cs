using Ringmaster.Core.Jobs;

namespace Ringmaster.App;

public sealed class StatusDisplayService(
    IJobRepository jobRepository,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<StatusDisplayItem>> GetSnapshotAsync(
        string? jobId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(jobId))
        {
            StoredJob? job = await jobRepository.GetAsync(jobId.Trim(), cancellationToken);
            if (job is null)
            {
                throw new InvalidOperationException($"Job '{jobId}' was not found.");
            }

            return [ToDisplayItem(job, timeProvider.GetUtcNow())];
        }

        IReadOnlyList<JobStatusListItem> listedJobs = await jobRepository.ListAsync(cancellationToken);
        if (listedJobs.Count == 0)
        {
            return [];
        }

        List<StatusDisplayItem> snapshot = [];
        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (JobStatusListItem listItem in listedJobs)
        {
            StoredJob? job = await jobRepository.GetAsync(listItem.JobId, cancellationToken);
            if (job is not null)
            {
                snapshot.Add(ToDisplayItem(job, now));
            }
        }

        return snapshot
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.JobId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task WatchAsync(
        string? jobId,
        TimeSpan pollInterval,
        Func<IReadOnlyList<StatusDisplayItem>, CancellationToken, Task> onSnapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onSnapshot);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<StatusDisplayItem> snapshot = await GetSnapshotAsync(jobId, cancellationToken);
            await onSnapshot(snapshot, cancellationToken);
            await Task.Delay(pollInterval, cancellationToken);
        }
    }

    private static StatusDisplayItem ToDisplayItem(StoredJob job, DateTimeOffset now)
    {
        int retryCount = Math.Max(0, job.Status.Attempts.Verifying - 1) + job.Status.Attempts.Repairing;

        return new StatusDisplayItem
        {
            JobId = job.Definition.JobId,
            Title = job.Definition.Title,
            State = job.Status.State,
            CurrentStage = job.Status.Execution.Stage,
            ActiveRunId = job.Status.Execution.RunId,
            Elapsed = job.Status.Execution.StartedAtUtc is { } startedAt ? now - startedAt : null,
            LastFailureSummary = job.Status.LastFailure?.Summary,
            RetryCount = retryCount,
            PullRequestUrl = job.Status.Pr.Url,
            Priority = job.Status.Priority,
            UpdatedAtUtc = job.Status.UpdatedAtUtc,
        };
    }
}

public sealed record class StatusDisplayItem
{
    public required string JobId { get; init; }
    public required string Title { get; init; }
    public required JobState State { get; init; }
    public JobStage? CurrentStage { get; init; }
    public string? ActiveRunId { get; init; }
    public TimeSpan? Elapsed { get; init; }
    public string? LastFailureSummary { get; init; }
    public int RetryCount { get; init; }
    public string? PullRequestUrl { get; init; }
    public int Priority { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
