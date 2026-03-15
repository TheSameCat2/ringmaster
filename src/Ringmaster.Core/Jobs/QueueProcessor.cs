namespace Ringmaster.Core.Jobs;

public sealed class QueueProcessor(
    IQueueSelector queueSelector,
    ILeaseManager leaseManager,
    INotificationSink notificationSink,
    JobEngine jobEngine,
    TimeProvider timeProvider)
{
    public async Task<QueuePassResult> RunOnceAsync(QueueRunOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        DateTimeOffset startedAtUtc = timeProvider.GetUtcNow();
        IReadOnlyList<QueueJobCandidate> candidates = await queueSelector.SelectRunnableJobsAsync(startedAtUtc, options.StaleLeaseThreshold, cancellationToken);
        QueueJobCandidate[] selected = candidates
            .Take(Math.Max(1, options.MaxParallelJobs))
            .ToArray();

        Task<QueueJobResult>[] tasks = selected
            .Select(candidate => ProcessCandidateAsync(candidate, options, cancellationToken))
            .ToArray();
        QueueJobResult[] results = await Task.WhenAll(tasks);

        return new QueuePassResult
        {
            StartedAtUtc = startedAtUtc,
            Jobs = results,
        };
    }

    public async Task RunAsync(QueueRunOptions options, CancellationToken cancellationToken)
    {
        await using ILeaseHandle? schedulerLease = await leaseManager.TryAcquireSchedulerLeaseAsync(options.OwnerId, cancellationToken);
        if (schedulerLease is null)
        {
            throw new InvalidOperationException("Another queue runner already owns the scheduler lock.");
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                QueuePassResult result = await RunOnceAsync(options, cancellationToken);
                if (result.Jobs.Count == 0)
                {
                    await Task.Delay(options.PollInterval, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown path for queue run.
        }
    }

    private async Task<QueueJobResult> ProcessCandidateAsync(
        QueueJobCandidate candidate,
        QueueRunOptions options,
        CancellationToken cancellationToken)
    {
        await using ILeaseHandle? jobLease = await leaseManager.TryAcquireJobLeaseAsync(candidate.Job, options.OwnerId, cancellationToken);
        if (jobLease is null)
        {
            return new QueueJobResult
            {
                JobId = candidate.Job.Definition.JobId,
                Disposition = QueueJobDisposition.SkippedLeaseHeld,
                Summary = "Another worker already owns the job lease.",
            };
        }

        await using ILeaseHandle? repoLease = await leaseManager.TryAcquireRepoMutationLeaseAsync(options.OwnerId, cancellationToken);
        if (repoLease is null)
        {
            return new QueueJobResult
            {
                JobId = candidate.Job.Definition.JobId,
                Disposition = QueueJobDisposition.SkippedRepoLocked,
                Summary = "The repository mutation lock is currently held by another worker.",
            };
        }

        using CancellationTokenSource heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task heartbeatTask = MaintainHeartbeatsAsync(candidate.Job.Definition.JobId, jobLease, repoLease, options, heartbeatCancellation.Token);

        try
        {
            await notificationSink.NotifyAsync(
                new NotificationRecord
                {
                    TimestampUtc = timeProvider.GetUtcNow(),
                    EventType = "job.started",
                    JobId = candidate.Job.Definition.JobId,
                    State = candidate.Job.Status.State,
                    Summary = candidate.ResumeExistingState
                        ? $"Resuming job from state {candidate.Job.Status.State}."
                        : "Starting queued job.",
                },
                cancellationToken);

            JobStatusSnapshot status = candidate.ResumeExistingState
                ? await jobEngine.ResumeAsync(candidate.Job.Definition.JobId, cancellationToken)
                : await jobEngine.RunAsync(candidate.Job.Definition.JobId, cancellationToken);

            await notificationSink.NotifyAsync(
                new NotificationRecord
                {
                    TimestampUtc = timeProvider.GetUtcNow(),
                    EventType = "job.completed",
                    JobId = candidate.Job.Definition.JobId,
                    State = status.State,
                    Summary = $"Job reached state {status.State}.",
                },
                cancellationToken);

            return new QueueJobResult
            {
                JobId = candidate.Job.Definition.JobId,
                Disposition = QueueJobDisposition.Started,
                FinalState = status.State,
                Summary = $"Job reached state {status.State}.",
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            await notificationSink.NotifyAsync(
                new NotificationRecord
                {
                    TimestampUtc = timeProvider.GetUtcNow(),
                    EventType = "job.error",
                    JobId = candidate.Job.Definition.JobId,
                    Summary = exception.Message,
                },
                CancellationToken.None);

            return new QueueJobResult
            {
                JobId = candidate.Job.Definition.JobId,
                Disposition = QueueJobDisposition.FailedToStart,
                Summary = exception.Message,
            };
        }
        finally
        {
            await heartbeatCancellation.CancelAsync();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path for the heartbeat loop.
            }
        }
    }

    private async Task MaintainHeartbeatsAsync(
        string jobId,
        ILeaseHandle jobLease,
        ILeaseHandle repoLease,
        QueueRunOptions options,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(options.LeaseHeartbeatInterval, cancellationToken);
            await jobLease.RenewAsync(runId: null, cancellationToken);
            await repoLease.RenewAsync(runId: jobId, cancellationToken);
        }
    }
}
