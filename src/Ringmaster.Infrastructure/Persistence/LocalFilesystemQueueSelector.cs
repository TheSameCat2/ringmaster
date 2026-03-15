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
        List<QueueJobCandidate> runnable = [];

        foreach (JobStatusListItem item in jobs)
        {
            StoredJob? storedJob = await jobRepository.GetAsync(item.JobId, cancellationToken);
            if (storedJob is null || storedJob.Status.NextEligibleAtUtc > nowUtc)
            {
                continue;
            }

            if (storedJob.Status.State is JobState.QUEUED)
            {
                runnable.Add(new QueueJobCandidate
                {
                    Job = storedJob,
                    ResumeExistingState = false,
                });
                continue;
            }

            if (!IsActiveState(storedJob.Status.State))
            {
                continue;
            }

            if (storedJob.Status.Execution.Status is ExecutionStatus.Running)
            {
                LeaseRecord? lease = await leaseManager.ReadJobLeaseAsync(storedJob, cancellationToken);
                if (lease is not null && lease.HeartbeatAtUtc >= nowUtc - staleLeaseThreshold)
                {
                    continue;
                }
            }

            runnable.Add(new QueueJobCandidate
            {
                Job = storedJob,
                ResumeExistingState = true,
            });
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
