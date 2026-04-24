using Ringmaster.Core.Jobs;
using Ringmaster.Infrastructure.Fakes;
using Ringmaster.Infrastructure.Persistence;

namespace Ringmaster.FaultInjectionTests;

public sealed class QueueRecoveryFaultInjectionTests
{
    [Fact]
    public async Task QueueOnceRecoversAnAbandonedImplementingRun()
    {
        using FaultTestTemporaryDirectory temporaryDirectory = new();
        DateTimeOffset now = new(2026, 3, 15, 22, 0, 0, TimeSpan.Zero);
        FaultTestStaticTimeProvider timeProvider = new(now);
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path, timeProvider);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Recover abandoned run"), CancellationToken.None);
        DateTimeOffset eventTime = now.AddMinutes(-3);

        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreateStateChanged(storedJob.Definition.JobId, JobState.QUEUED, JobState.PREPARING, eventTime),
            CancellationToken.None);
        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreateStateChanged(storedJob.Definition.JobId, JobState.PREPARING, JobState.IMPLEMENTING, eventTime.AddMinutes(1)),
            CancellationToken.None);

        JobRunRecord abandonedRun = new()
        {
            RunId = "0001-implementing-implementer",
            JobId = storedJob.Definition.JobId,
            Stage = JobStage.IMPLEMENTING,
            Role = StageRole.Implementer,
            Attempt = 1,
            StartedAtUtc = eventTime.AddMinutes(2),
            Tool = "fake",
            Command = ["fake-runner", "IMPLEMENTING"],
            SessionId = "abandoned-session",
        };
        await repository.SaveRunAsync(storedJob.Definition.JobId, abandonedRun, CancellationToken.None);
        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreateRunStarted(abandonedRun),
            CancellationToken.None);

        QueueProcessor queueProcessor = CreateQueueProcessor(temporaryDirectory.Path, repository, timeProvider);
        QueuePassResult result = await queueProcessor.RunOnceAsync(
            new QueueRunOptions
            {
                OwnerId = "tester",
            },
            CancellationToken.None);

        StoredJob reloaded = await repository.GetAsync(storedJob.Definition.JobId, CancellationToken.None)
            ?? throw new InvalidOperationException("The recovered job was not found.");
        string abandonedRunJson = await File.ReadAllTextAsync(Path.Combine(storedJob.JobDirectoryPath, "runs", abandonedRun.RunId, "run.json"));

        Assert.Single(result.Jobs);
        Assert.Equal(JobState.READY_FOR_PR, reloaded.Status.State);
        Assert.Contains("\"result\": \"Canceled\"", abandonedRunJson, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(storedJob.JobDirectoryPath, "runs", "0002-implementing-implementer")));
    }

    [Fact]
    public async Task QueueOnceIgnoresStaleLeaseFiles()
    {
        using FaultTestTemporaryDirectory temporaryDirectory = new();
        DateTimeOffset now = new(2026, 3, 15, 22, 5, 0, TimeSpan.Zero);
        FaultTestStaticTimeProvider timeProvider = new(now);
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path, timeProvider);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Stale lease"), CancellationToken.None);

        AtomicFileWriter writer = new();
        await writer.WriteJsonAsync(
            Path.Combine(storedJob.JobDirectoryPath, "locks", "lease.json"),
            new LeaseRecord
            {
                OwnerId = "stale-worker",
                AcquiredAtUtc = now.AddMinutes(-20),
                HeartbeatAtUtc = now.AddMinutes(-15),
                ProcessId = 9999,
                MachineName = "stale-host",
            },
            CancellationToken.None);

        QueueProcessor queueProcessor = CreateQueueProcessor(temporaryDirectory.Path, repository, timeProvider);
        await queueProcessor.RunOnceAsync(
            new QueueRunOptions
            {
                OwnerId = "tester",
                StaleLeaseThreshold = TimeSpan.FromSeconds(10),
            },
            CancellationToken.None);

        StoredJob reloaded = await repository.GetAsync(storedJob.Definition.JobId, CancellationToken.None)
            ?? throw new InvalidOperationException("The queued job was not found after stale lease recovery.");
        Assert.Equal(JobState.READY_FOR_PR, reloaded.Status.State);
        Assert.False(File.Exists(Path.Combine(storedJob.JobDirectoryPath, "locks", "lease.json")));
    }

    [Fact]
    public async Task QueueOnceSkipsJobsWhenRepoMutationLockIsHeld()
    {
        using FaultTestTemporaryDirectory temporaryDirectory = new();
        DateTimeOffset now = new(2026, 3, 15, 22, 10, 0, TimeSpan.Zero);
        FaultTestStaticTimeProvider timeProvider = new(now);
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path, timeProvider);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Repo lock"), CancellationToken.None);
        FileLeaseManager leaseManager = new(temporaryDirectory.Path, new AtomicFileWriter(), timeProvider);
        await using ILeaseHandle? repoLease = await leaseManager.TryAcquireRepoMutationLeaseAsync("other-worker", CancellationToken.None);
        QueueProcessor queueProcessor = new(
            new LocalFilesystemQueueSelector(repository, leaseManager),
            leaseManager,
            new CompositeNotificationSink([]),
            CreateJobEngine(repository, timeProvider),
            timeProvider);

        QueuePassResult result = await queueProcessor.RunOnceAsync(
            new QueueRunOptions
            {
                OwnerId = "tester",
            },
            CancellationToken.None);
        StoredJob reloaded = await repository.GetAsync(storedJob.Definition.JobId, CancellationToken.None)
            ?? throw new InvalidOperationException("The queued job was not found after repo-lock contention.");

        Assert.Single(result.Jobs);
        Assert.Equal(QueueJobDisposition.SkippedRepoLocked, result.Jobs[0].Disposition);
        Assert.Equal(JobState.QUEUED, reloaded.Status.State);
    }

    private static QueueProcessor CreateQueueProcessor(string repositoryRoot, IJobRepository repository, TimeProvider timeProvider)
    {
        FileLeaseManager leaseManager = new(repositoryRoot, new AtomicFileWriter(), timeProvider);
        return new QueueProcessor(
            new LocalFilesystemQueueSelector(repository, leaseManager),
            leaseManager,
            new CompositeNotificationSink([]),
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

    private static JobCreateRequest CreateRequest(string title)
    {
        return new JobCreateRequest
        {
            Title = title,
            Description = "Exercise queue recovery.",
            CreatedBy = "tester",
        };
    }
}

internal sealed class FaultTestTemporaryDirectory : IDisposable
{
    public FaultTestTemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ringmaster-fault-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed class FaultTestStaticTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }
}
