using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Ringmaster.Infrastructure.Fakes;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.IntegrationTests.Testing;

namespace Ringmaster.IntegrationTests;

public sealed class JobEngineIntegrationTests
{
    [Fact]
    public async Task RunAsyncTransitionsQueuedJobToReadyForPrAndPersistsRunHistory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DateTimeOffset createdAt = new(2026, 3, 15, 16, 45, 0, TimeSpan.Zero);
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path, createdAt);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest(), CancellationToken.None);
        JobEngine engine = CreateEngine(
            repository,
            new StaticTimeProvider(createdAt),
            CreateDefaultStageRunners());

        JobStatusSnapshot status = await engine.RunAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.Equal(JobState.READY_FOR_PR, status.State);
        Assert.Equal(ExecutionStatus.Idle, status.Execution.Status);
        Assert.Equal(1, status.Attempts.Preparing);
        Assert.Equal(1, status.Attempts.Implementing);
        Assert.Equal(1, status.Attempts.Verifying);
        Assert.Equal(0, status.Attempts.Repairing);
        Assert.Equal(1, status.Attempts.Reviewing);

        string runsRoot = Path.Combine(storedJob.JobDirectoryPath, "runs");
        string[] runDirectories = Directory.GetDirectories(runsRoot)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray()!;

        Assert.Equal(
            ["0001-preparing-planner", "0002-implementing-implementer", "0003-verifying-system", "0004-reviewing-reviewer"],
            runDirectories);

        foreach (string runDirectory in runDirectories)
        {
            string runJson = await File.ReadAllTextAsync(Path.Combine(runsRoot, runDirectory, "run.json"));
            JobRunRecord run = RingmasterJsonSerializer.Deserialize<JobRunRecord>(runJson);

            Assert.NotNull(run.CompletedAtUtc);
            Assert.Equal(RunResult.Completed, run.Result);
        }

        StoredJob? reloaded = await repository.GetAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(JobState.READY_FOR_PR, reloaded.Status.State);
        Assert.Equal(14, reloaded.Events.Count);
        Assert.Contains(reloaded.Events, jobEvent => jobEvent.Type == JobEventType.RunStarted && jobEvent.Stage == JobStage.VERIFYING);
    }

    [Fact]
    public async Task RunAsyncTransitionsToBlockedWhenAStageRequestsHumanInput()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DateTimeOffset createdAt = new(2026, 3, 15, 16, 45, 0, TimeSpan.Zero);
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path, createdAt);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest(), CancellationToken.None);
        JobEngine engine = CreateEngine(
            repository,
            new StaticTimeProvider(createdAt),
            [
                new FakeStageRunner(JobStage.PREPARING, StageRole.Planner, JobState.IMPLEMENTING, "Planner completed."),
                new ScriptedStageRunner(
                    JobStage.IMPLEMENTING,
                    StageRole.Implementer,
                    (_, cancellationToken) =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return Task.FromResult(
                            StageExecutionResult.Blocked(
                                new BlockerInfo
                                {
                                    ReasonCode = BlockerReasonCode.ArchitectureDecision,
                                    Summary = "Need a decision on the persistence boundary.",
                                    Questions = ["Should retries stay in-process or move to the scheduler?"],
                                    ResumeState = JobState.IMPLEMENTING,
                                },
                                "Implementation is blocked pending a design decision."));
                    }),
                new FakeStageRunner(JobStage.VERIFYING, StageRole.SystemVerifier, JobState.REVIEWING, "Verifier completed."),
                new FakeStageRunner(JobStage.REPAIRING, StageRole.Implementer, JobState.VERIFYING, "Repair completed."),
                new FakeStageRunner(JobStage.REVIEWING, StageRole.Reviewer, JobState.READY_FOR_PR, "Reviewer approved."),
            ]);

        JobStatusSnapshot status = await engine.RunAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.Equal(JobState.BLOCKED, status.State);
        Assert.Equal(JobState.IMPLEMENTING, status.ResumeState);
        Assert.Equal(ExecutionStatus.Idle, status.Execution.Status);
        Assert.NotNull(status.Blocker);
        Assert.Equal(BlockerReasonCode.ArchitectureDecision, status.Blocker.ReasonCode);
        Assert.Equal(1, status.Attempts.Preparing);
        Assert.Equal(1, status.Attempts.Implementing);
        Assert.Equal(0, status.Attempts.Verifying);

        string runJson = await File.ReadAllTextAsync(Path.Combine(storedJob.JobDirectoryPath, "runs", "0002-implementing-implementer", "run.json"));
        JobRunRecord run = RingmasterJsonSerializer.Deserialize<JobRunRecord>(runJson);

        Assert.Equal(RunResult.Blocked, run.Result);

        StoredJob? reloaded = await repository.GetAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Contains(reloaded.Events, jobEvent => jobEvent.Type == JobEventType.JobBlocked);
    }

    [Fact]
    public async Task RunAsyncRejectsInvalidStageTransitions()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DateTimeOffset createdAt = new(2026, 3, 15, 16, 45, 0, TimeSpan.Zero);
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path, createdAt);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest(), CancellationToken.None);
        JobEngine engine = CreateEngine(
            repository,
            new StaticTimeProvider(createdAt),
            [
                new ScriptedStageRunner(
                    JobStage.PREPARING,
                    StageRole.Planner,
                    (_, cancellationToken) =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return Task.FromResult(StageExecutionResult.Succeeded(JobState.READY_FOR_PR, "Skipped the lifecycle."));
                    }),
            ]);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.RunAsync(storedJob.Definition.JobId, CancellationToken.None));

        Assert.Equal("Invalid state transition from PREPARING to READY_FOR_PR.", exception.Message);

        StoredJob? reloaded = await repository.GetAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(JobState.PREPARING, reloaded.Status.State);
        Assert.Equal(ExecutionStatus.Idle, reloaded.Status.Execution.Status);
        Assert.Single(Directory.GetDirectories(Path.Combine(storedJob.JobDirectoryPath, "runs")));
    }

    private static LocalFilesystemJobRepository CreateRepository(string repositoryRoot, DateTimeOffset createdAt)
    {
        return new LocalFilesystemJobRepository(
            repositoryRoot,
            new StaticTimeProvider(createdAt),
            new FixedJobIdGenerator("job-20260315-7f3c9b2a"),
            new AtomicFileWriter(),
            new JobEventLogStore(),
            new JobSnapshotRebuilder());
    }

    private static JobEngine CreateEngine(IJobRepository repository, TimeProvider timeProvider, IEnumerable<IStageRunner> stageRunners)
    {
        return new JobEngine(repository, new RingmasterStateMachine(), stageRunners, timeProvider);
    }

    private static JobCreateRequest CreateRequest()
    {
        return new JobCreateRequest
        {
            Title = "Add retry handling",
            Description = "Implement bounded retries for retryable failures.",
            CreatedBy = "tester",
        };
    }

    private static IStageRunner[] CreateDefaultStageRunners()
    {
        return
        [
            new FakeStageRunner(JobStage.PREPARING, StageRole.Planner, JobState.IMPLEMENTING, "Planner completed."),
            new FakeStageRunner(JobStage.IMPLEMENTING, StageRole.Implementer, JobState.VERIFYING, "Implementer completed."),
            new FakeStageRunner(JobStage.VERIFYING, StageRole.SystemVerifier, JobState.REVIEWING, "Verifier completed."),
            new FakeStageRunner(JobStage.REPAIRING, StageRole.Implementer, JobState.VERIFYING, "Repair completed."),
            new FakeStageRunner(JobStage.REVIEWING, StageRole.Reviewer, JobState.READY_FOR_PR, "Reviewer approved."),
        ];
    }
}
