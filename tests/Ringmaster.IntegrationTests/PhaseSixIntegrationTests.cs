using System.Diagnostics;
using Ringmaster.Core.Jobs;
using Ringmaster.Infrastructure.Fakes;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.IntegrationTests.Testing;

namespace Ringmaster.IntegrationTests;

public sealed class PhaseSixIntegrationTests
{
    [Fact]
    public async Task QueueSelectorOrdersRunnableJobsByPriorityAndEligibility()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DateTimeOffset now = new(2026, 3, 15, 21, 0, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(now);
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path, timeProvider);
        FileLeaseManager leaseManager = new(temporaryDirectory.Path, new AtomicFileWriter(), timeProvider);
        LocalFilesystemQueueSelector selector = new(repository, leaseManager);

        StoredJob lowPriority = await repository.CreateAsync(CreateRequest("Low priority", priority: 10), CancellationToken.None);
        StoredJob highPriority = await repository.CreateAsync(CreateRequest("High priority", priority: 100), CancellationToken.None);
        StoredJob futureJob = await repository.CreateAsync(CreateRequest("Future job", priority: 90), CancellationToken.None);

        await WriteStatusAsync(temporaryDirectory.Path, futureJob.Status with
        {
            NextEligibleAtUtc = now.AddHours(1),
        });

        IReadOnlyList<QueueJobCandidate> runnable = await selector.SelectRunnableJobsAsync(now, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal([highPriority.Definition.JobId, lowPriority.Definition.JobId], runnable.Select(candidate => candidate.Job.Definition.JobId));
        Assert.All(runnable, candidate => Assert.False(candidate.ResumeExistingState));
    }

    [Fact]
    public async Task QueueOnceRunsQueuedJobsAndWritesNotifications()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DateTimeOffset now = new(2026, 3, 15, 21, 5, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(now);
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path, timeProvider);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Queued job"), CancellationToken.None);
        QueueProcessor queueProcessor = CreateQueueProcessor(temporaryDirectory.Path, repository, timeProvider);

        QueuePassResult result = await queueProcessor.RunOnceAsync(
            new QueueRunOptions
            {
                MaxParallelJobs = 1,
                OwnerId = "tester",
            },
            CancellationToken.None);
        StoredJob reloaded = await repository.GetAsync(storedJob.Definition.JobId, CancellationToken.None)
            ?? throw new InvalidOperationException("The queued job was not found after queue processing.");

        Assert.Single(result.Jobs);
        Assert.Equal(QueueJobDisposition.Started, result.Jobs[0].Disposition);
        Assert.Equal(JobState.READY_FOR_PR, reloaded.Status.State);

        string notifications = await File.ReadAllTextAsync(Path.Combine(temporaryDirectory.Path, ".ringmaster", "runtime", "notifications.jsonl"));
        Assert.Contains("job.started", notifications, StringComparison.Ordinal);
        Assert.Contains("job.completed", notifications, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueueRunProcessesWorkBeforeCancellation()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DateTimeOffset now = new(2026, 3, 15, 21, 10, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(now);
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path, timeProvider);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Queue run job"), CancellationToken.None);
        QueueProcessor queueProcessor = CreateQueueProcessor(temporaryDirectory.Path, repository, timeProvider);
        TimeSpan readyForPrTimeout = TimeSpan.FromSeconds(10);
        // Give the queue runner more time than the assertion window so slower Windows CI
        // machines do not cancel the run before the job reaches READY_FOR_PR.
        using CancellationTokenSource cancellationTokenSource = new(readyForPrTimeout + TimeSpan.FromSeconds(5));
        Exception? runTaskError = null;

        Task runTask = queueProcessor.RunAsync(
            new QueueRunOptions
            {
                MaxParallelJobs = 1,
                PollInterval = TimeSpan.FromMilliseconds(25),
                OwnerId = "tester",
            },
            cancellationTokenSource.Token);

        try
        {
            await WaitForJobStateAsync(repository, storedJob.Definition.JobId, JobState.READY_FOR_PR, readyForPrTimeout);
        }
        finally
        {
            cancellationTokenSource.Cancel();

            try
            {
                await runTask;
            }
            catch (Exception exception)
            {
                runTaskError = exception;
            }
        }

        if (runTaskError is not null)
        {
            throw runTaskError;
        }

        StoredJob reloaded = await repository.GetAsync(storedJob.Definition.JobId, CancellationToken.None)
            ?? throw new InvalidOperationException("The queued job was not found after queue run.");
        Assert.Equal(JobState.READY_FOR_PR, reloaded.Status.State);
    }

    private static QueueProcessor CreateQueueProcessor(string repositoryRoot, IJobRepository repository, TimeProvider timeProvider)
    {
        FileLeaseManager leaseManager = new(repositoryRoot, new AtomicFileWriter(), timeProvider);
        return new QueueProcessor(
            new LocalFilesystemQueueSelector(repository, leaseManager),
            leaseManager,
            new CompositeNotificationSink(
            [
                new JsonlNotificationSink(repositoryRoot),
            ]),
            CreateJobEngine(repository, timeProvider),
            timeProvider);
    }

    private static JobEngine CreateJobEngine(IJobRepository repository, TimeProvider timeProvider)
    {
        return new JobEngine(
            repository,
            new RingmasterStateMachine(),
            [
                new FakeStageRunner(JobStage.PREPARING, StageRole.Planner, JobState.IMPLEMENTING, "Planner completed."),
                new FakeStageRunner(JobStage.IMPLEMENTING, StageRole.Implementer, JobState.VERIFYING, "Implementer completed."),
                new FakeStageRunner(JobStage.VERIFYING, StageRole.SystemVerifier, JobState.REVIEWING, "Verifier completed."),
                new FakeStageRunner(JobStage.REPAIRING, StageRole.Implementer, JobState.VERIFYING, "Repair completed."),
                new FakeStageRunner(JobStage.REVIEWING, StageRole.Reviewer, JobState.READY_FOR_PR, "Reviewer approved."),
            ],
            timeProvider);
    }

    private static LocalFilesystemJobRepository CreateRepository(string repositoryRoot, TimeProvider timeProvider)
    {
        return new LocalFilesystemJobRepository(
            repositoryRoot,
            timeProvider,
            new DefaultJobIdGenerator(),
            new AtomicFileWriter(),
            new JobEventLogStore(),
            new JobSnapshotRebuilder());
    }

    private static JobCreateRequest CreateRequest(string title, int priority = 50)
    {
        return new JobCreateRequest
        {
            Title = title,
            Description = "Exercise queue behavior.",
            CreatedBy = "tester",
            Priority = priority,
        };
    }

    private static async Task WriteStatusAsync(string repositoryRoot, JobStatusSnapshot status)
    {
        AtomicFileWriter writer = new();
        await writer.WriteJsonAsync(
            Path.Combine(repositoryRoot, ".ringmaster", "jobs", status.JobId, "STATUS.json"),
            status,
            CancellationToken.None);
    }

    private static async Task WaitForJobStateAsync(
        IJobRepository repository,
        string jobId,
        JobState expectedState,
        TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        StoredJob? job = null;

        while (stopwatch.Elapsed < timeout)
        {
            job = await repository.GetAsync(jobId, CancellationToken.None);
            if (job?.Status.State is JobState state && state == expectedState)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        throw new Xunit.Sdk.XunitException(
            $"Expected job '{jobId}' to reach state {expectedState} within {timeout}, but found {job?.Status.State.ToString() ?? "missing"}.");
    }
}
