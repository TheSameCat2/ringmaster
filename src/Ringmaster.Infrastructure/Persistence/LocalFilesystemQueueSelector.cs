using Ringmaster.Core.Jobs;

namespace Ringmaster.Infrastructure.Persistence;

public sealed class LocalFilesystemQueueSelector(
    IJobRepository jobRepository,
    ILeaseManager leaseManager) : IQueueSelector
{
    public async Task<IReadOnlyList<QueueJobCandidate>> SelectRunnableJobsAsync(
        DateTimeOffset nowUtc,
        TimeSpan staleLeaseThreshold,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<JobStatusListItem> jobs = await jobRepository.ListAsync(cancellationToken);
        List<(JobStatusListItem Item, JobStatusSnapshot Status)> candidates = [];

        foreach (JobStatusListItem item in jobs)
        {
            JobStatusSnapshot? status = await jobRepository.GetStatusAsync(item.JobId, cancellationToken);
            if (status is null || status.NextEligibleAtUtc > nowUtc)
            {
                continue;
            }

            candidates.Add((item, status));
        }

        List<QueueJobCandidate> runnable = [];

        foreach ((JobStatusListItem item, JobStatusSnapshot status) in candidates)
        {
            if (status.State is JobState.QUEUED)
            {
                StoredJob? storedJob = await jobRepository.GetAsync(item.JobId, cancellationToken);
                if (storedJob is not null)
                {
                    runnable.Add(new QueueJobCandidate
                    {
                        Job = storedJob,
                        ResumeExistingState = false,
                    });
                }
                continue;
            }

            if (!IsActiveState(status.State))
            {
                continue;
            }

            if (status.Execution.Status is ExecutionStatus.Running)
            {
                StoredJob? storedJob = await jobRepository.GetAsync(item.JobId, cancellationToken);
                if (storedJob is null)
                {
                    continue;
                }

                LeaseRecord? lease = await leaseManager.ReadJobLeaseAsync(storedJob, cancellationToken);
                if (lease is not null && lease.HeartbeatAtUtc >= nowUtc - staleLeaseThreshold)
                {
                    continue;
                }

                runnable.Add(new QueueJobCandidate
                {
                    Job = storedJob,
                    ResumeExistingState = true,
                });
            }
            else
            {
                StoredJob? storedJob = await jobRepository.GetAsync(item.JobId, cancellationToken);
                if (storedJob is not null)
                {
                    runnable.Add(new QueueJobCandidate
                    {
                        Job = storedJob,
                        ResumeExistingState = true,
                    });
                }
            }
        }

        return runnable
            .OrderByDescending(candidate => candidate.Job.Status.Priority)
            .ThenBy(candidate => candidate.Job.Status.NextEligibleAtUtc)
            .ThenBy(candidate => candidate.Job.Definition.CreatedAtUtc)
            .ToArray();
    }

    private static bool IsActiveState(JobState state)
    {
        return state is JobState.PREPARING
            or JobState.IMPLEMENTING
            or JobState.VERIFYING
            or JobState.REPAIRING
            or JobState.REVIEWING;
    }
}
