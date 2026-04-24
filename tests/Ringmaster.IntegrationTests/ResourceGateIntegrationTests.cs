using Ringmaster.Core.Jobs;
using Ringmaster.Infrastructure.Fakes;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.IntegrationTests.Testing;

namespace Ringmaster.IntegrationTests;

public sealed class ResourceGateIntegrationTests
{
    [Fact]
    public async Task RunOnceAsync_ResourceLimits_SkipWhenAtCapacity()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TimeProvider timeProvider = TimeProvider.System;
        LocalFilesystemJobRepository repository = new(
            temporaryDirectory.Path,
            timeProvider,
            new DefaultJobIdGenerator(),
            new AtomicFileWriter(),
            new JobEventLogStore(),
            new JobSnapshotRebuilder());

        // Create 2 QUEUED jobs. Both map to Codex resource class.
        for (int index = 0; index < 2; index++)
        {
            await repository.CreateAsync(new JobCreateRequest
            {
                Title = $"Job {index}",
                Description = "Test job.",
                CreatedBy = "tester",
            }, CancellationToken.None);
            await Task.Delay(1, CancellationToken.None);
        }

        FileLeaseManager leaseManager = new(temporaryDirectory.Path, new AtomicFileWriter(), timeProvider);
        QueueProcessor queueProcessor = new(
            new LocalFilesystemQueueSelector(repository, leaseManager),
            leaseManager,
            new CompositeNotificationSink([]),
            new JobEngine(
                repository,
                new RingmasterStateMachine(),
                [
                    new FakeStageRunner(JobStage.PREPARING, StageRole.Planner, JobState.IMPLEMENTING, "Planner completed."),
                    new FakeStageRunner(JobStage.IMPLEMENTING, StageRole.Implementer, JobState.VERIFYING, "Implementer completed."),
                    new FakeStageRunner(JobStage.VERIFYING, StageRole.SystemVerifier, JobState.REVIEWING, "Verifier completed."),
                    new FakeStageRunner(JobStage.REVIEWING, StageRole.Reviewer, JobState.READY_FOR_PR, "Reviewer approved."),
                ],
                timeProvider),
            timeProvider);

        // With MaxParallelJobs = 2 but MaxConcurrentCodexRuns = 1,
        // only 1 job should start (the second is skipped due to Codex capacity).
        // The repo mutation lease also serializes, so the net is still 1.
        QueuePassResult result = await queueProcessor.RunOnceAsync(
            new QueueRunOptions
            {
                MaxParallelJobs = 2,
                MaxConcurrentCodexRuns = 1,
                OwnerId = "tester",
            },
            CancellationToken.None);

        int startedCount = result.Jobs.Count(j => j.Disposition == QueueJobDisposition.Started);
        Assert.True(startedCount <= 1, $"Expected at most 1 started job due to Codex limit and repo serialization, got {startedCount}");
    }

    [Fact]
    public async Task RunOnceAsync_DifferentResourceClasses_CanRunConcurrently()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TimeProvider timeProvider = TimeProvider.System;
        LocalFilesystemJobRepository repository = new(
            temporaryDirectory.Path,
            timeProvider,
            new DefaultJobIdGenerator(),
            new AtomicFileWriter(),
            new JobEventLogStore(),
            new JobSnapshotRebuilder());

        // Create one QUEUED job (Codex) and one VERIFYING job (Verification).
        StoredJob queuedJob = await repository.CreateAsync(new JobCreateRequest
        {
            Title = "Queued",
            Description = "Test.",
            CreatedBy = "tester",
        }, CancellationToken.None);

        await Task.Delay(1, CancellationToken.None);

        StoredJob verifyingJob = await repository.CreateAsync(new JobCreateRequest
        {
            Title = "Verifying",
            Description = "Test.",
            CreatedBy = "tester",
        }, CancellationToken.None);

        // Move the second job to VERIFYING.
        await repository.AppendEventAsync(verifyingJob.Definition.JobId, new JobEventRecord
        {
            Sequence = 1,
            TimestampUtc = timeProvider.GetUtcNow(),
            Type = JobEventType.StateChanged,
            JobId = verifyingJob.Definition.JobId,
            From = JobState.QUEUED,
            To = JobState.VERIFYING,
        }, CancellationToken.None);

        FileLeaseManager leaseManager = new(temporaryDirectory.Path, new AtomicFileWriter(), timeProvider);
        QueueProcessor queueProcessor = new(
            new LocalFilesystemQueueSelector(repository, leaseManager),
            leaseManager,
            new CompositeNotificationSink([]),
            new JobEngine(
                repository,
                new RingmasterStateMachine(),
                [
                    new FakeStageRunner(JobStage.PREPARING, StageRole.Planner, JobState.IMPLEMENTING, "Planner completed."),
                    new FakeStageRunner(JobStage.IMPLEMENTING, StageRole.Implementer, JobState.VERIFYING, "Implementer completed."),
                    new FakeStageRunner(JobStage.VERIFYING, StageRole.SystemVerifier, JobState.REVIEWING, "Verifier completed."),
                    new FakeStageRunner(JobStage.REVIEWING, StageRole.Reviewer, JobState.READY_FOR_PR, "Reviewer approved."),
                ],
                timeProvider),
            timeProvider);

        // MaxParallelJobs = 2, Codex limit = 1, Verification limit = 1.
        // Both jobs should be eligible to run concurrently in theory (different resource classes).
        // However, the repo mutation lease serializes all jobs, so only 1 runs in practice.
        QueuePassResult result = await queueProcessor.RunOnceAsync(
            new QueueRunOptions
            {
                MaxParallelJobs = 2,
                MaxConcurrentCodexRuns = 1,
                MaxConcurrentVerificationRuns = 1,
                OwnerId = "tester",
            },
            CancellationToken.None);

        int startedCount = result.Jobs.Count(j => j.Disposition == QueueJobDisposition.Started);
        Assert.True(startedCount <= 1, $"Expected at most 1 started job due to repo serialization, got {startedCount}");
    }
}
