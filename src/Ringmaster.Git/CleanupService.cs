using Ringmaster.Core.Jobs;

namespace Ringmaster.Git;

public sealed class CleanupService(
    string repositoryRoot,
    IJobRepository jobRepository,
    ILeaseManager leaseManager,
    GitCli gitCli,
    TimeProvider timeProvider)
{
    private readonly string _repositoryRoot = Path.GetFullPath(repositoryRoot);

    public async Task<CleanupResult> RunAsync(CleanupOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        DateTimeOffset startedAtUtc = timeProvider.GetUtcNow();
        DateTimeOffset worktreeCutoff = startedAtUtc - options.WorktreeRetention;
        DateTimeOffset artifactCutoff = startedAtUtc - options.ArtifactRetention;
        IReadOnlyList<JobStatusListItem> jobs = await jobRepository.ListAsync(cancellationToken);
        List<JobCleanupResult> results = [];
        bool shouldPruneWorktrees = false;

        foreach (JobStatusListItem listItem in jobs.OrderBy(item => item.JobId, StringComparer.Ordinal))
        {
            StoredJob? storedJob = await jobRepository.GetAsync(listItem.JobId, cancellationToken);
            if (storedJob is null)
            {
                continue;
            }

            JobCleanupResult result = await CleanupJobAsync(
                storedJob,
                worktreeCutoff,
                artifactCutoff,
                options,
                cancellationToken);
            shouldPruneWorktrees |= result.Disposition is CleanupDisposition.Removed;
            results.Add(result);
        }

        if (shouldPruneWorktrees)
        {
            await gitCli.PruneWorktreesAsync(_repositoryRoot, cancellationToken);
        }

        return new CleanupResult
        {
            StartedAtUtc = startedAtUtc,
            WorktreesRemoved = results.Count(result => result.Disposition is CleanupDisposition.Removed),
            ArtifactFilesRemoved = results.Sum(result => result.ArtifactFilesRemoved),
            Jobs = results,
        };
    }

    private async Task<JobCleanupResult> CleanupJobAsync(
        StoredJob storedJob,
        DateTimeOffset worktreeCutoff,
        DateTimeOffset artifactCutoff,
        CleanupOptions options,
        CancellationToken cancellationToken)
    {
        int artifactFilesRemoved = 0;

        if (!IsFinished(storedJob.Status.State))
        {
            return CreateResult(
                storedJob,
                CleanupDisposition.SkippedNotFinished,
                artifactFilesRemoved,
                "Job is not in a finished state.");
        }

        artifactFilesRemoved = PruneRunLogs(storedJob.JobDirectoryPath, artifactCutoff);

        LeaseRecord? lease = await leaseManager.ReadJobLeaseAsync(storedJob, cancellationToken);
        if (lease is not null && lease.HeartbeatAtUtc >= timeProvider.GetUtcNow() - options.StaleLeaseThreshold)
        {
            return CreateResult(
                storedJob,
                CleanupDisposition.SkippedActiveLease,
                artifactFilesRemoved,
                "A recent job lease is still present.");
        }

        if (storedJob.Status.UpdatedAtUtc > worktreeCutoff)
        {
            return CreateResult(
                storedJob,
                CleanupDisposition.SkippedRetention,
                artifactFilesRemoved,
                "Worktree retention period has not elapsed.");
        }

        if (!IsWorktreeRemovalSafe(storedJob.Status))
        {
            return CreateResult(
                storedJob,
                CleanupDisposition.SkippedUnsafeState,
                artifactFilesRemoved,
                "Finished job does not have durable PR state recorded yet.");
        }

        string? worktreePath = storedJob.Status.Git?.WorktreePath;
        if (string.IsNullOrWhiteSpace(worktreePath))
        {
            return CreateResult(
                storedJob,
                CleanupDisposition.SkippedNoWorktree,
                artifactFilesRemoved,
                "No worktree path was recorded for this job.");
        }

        if (!Directory.Exists(worktreePath))
        {
            return CreateResult(
                storedJob,
                CleanupDisposition.AlreadyMissing,
                artifactFilesRemoved,
                "Worktree directory was already absent.");
        }

        try
        {
            await gitCli.RemoveWorktreeAsync(
                _repositoryRoot,
                worktreePath,
                force: true,
                forceIfLocked: true,
                cancellationToken);
            return CreateResult(
                storedJob,
                CleanupDisposition.Removed,
                artifactFilesRemoved,
                $"Removed retained worktree '{worktreePath}'.");
        }
        catch (Exception exception) when (exception is GitCliException or IOException)
        {
            return CreateResult(
                storedJob,
                CleanupDisposition.Error,
                artifactFilesRemoved,
                exception.Message);
        }
    }

    private static bool IsFinished(JobState state)
    {
        return state is JobState.DONE or JobState.FAILED;
    }

    private static bool IsWorktreeRemovalSafe(JobStatusSnapshot status)
    {
        return status.State switch
        {
            JobState.DONE => status.Pr.Status is PullRequestStatus.Draft
                or PullRequestStatus.Open
                or PullRequestStatus.Merged
                or PullRequestStatus.Closed,
            JobState.FAILED => true,
            _ => false,
        };
    }

    private static int PruneRunLogs(string jobDirectoryPath, DateTimeOffset artifactCutoff)
    {
        string runsRoot = Path.Combine(jobDirectoryPath, "runs");
        if (!Directory.Exists(runsRoot))
        {
            return 0;
        }

        int removed = 0;

        foreach (string filePath in Directory.EnumerateFiles(runsRoot, "*.log", SearchOption.AllDirectories))
        {
            DateTimeOffset lastWrite = File.GetLastWriteTimeUtc(filePath);
            if (lastWrite > artifactCutoff)
            {
                continue;
            }

            File.Delete(filePath);
            removed++;
        }

        return removed;
    }

    private static JobCleanupResult CreateResult(
        StoredJob storedJob,
        CleanupDisposition disposition,
        int artifactFilesRemoved,
        string summary)
    {
        return new JobCleanupResult
        {
            JobId = storedJob.Definition.JobId,
            State = storedJob.Status.State,
            WorktreePath = storedJob.Status.Git?.WorktreePath,
            Disposition = disposition,
            ArtifactFilesRemoved = artifactFilesRemoved,
            Summary = summary,
        };
    }
}

public sealed record class CleanupOptions
{
    public TimeSpan WorktreeRetention { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan ArtifactRetention { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan StaleLeaseThreshold { get; init; } = TimeSpan.FromMinutes(10);
}

public sealed record class CleanupResult
{
    public required DateTimeOffset StartedAtUtc { get; init; }
    public int WorktreesRemoved { get; init; }
    public int ArtifactFilesRemoved { get; init; }
    public IReadOnlyList<JobCleanupResult> Jobs { get; init; } = [];
}

public sealed record class JobCleanupResult
{
    public required string JobId { get; init; }
    public required JobState State { get; init; }
    public string? WorktreePath { get; init; }
    public required CleanupDisposition Disposition { get; init; }
    public int ArtifactFilesRemoved { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public enum CleanupDisposition
{
    SkippedNotFinished,
    SkippedActiveLease,
    SkippedRetention,
    SkippedUnsafeState,
    SkippedNoWorktree,
    AlreadyMissing,
    Removed,
    Error,
}
