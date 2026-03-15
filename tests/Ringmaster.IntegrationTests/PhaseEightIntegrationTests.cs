using System.Diagnostics;
using Ringmaster.Core.Configuration;
using Ringmaster.Core.Jobs;
using Ringmaster.Git;
using Ringmaster.Infrastructure.Configuration;
using Ringmaster.Infrastructure.Fakes;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.Infrastructure.Processes;
using Ringmaster.IntegrationTests.Testing;

namespace Ringmaster.IntegrationTests;

public sealed class PhaseEightIntegrationTests
{
    [Fact]
    public async Task ExternalProcessRunnerExecutesRelativeToolsWithoutShellWrapping()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string relativeToolPath = await CreateEchoToolAsync(temporaryDirectory.Path);
        ExternalProcessRunner runner = new(new StaticTimeProvider(new DateTimeOffset(2026, 3, 15, 13, 0, 0, TimeSpan.Zero)));

        ExternalProcessResult result = await runner.RunAsync(
            new ExternalProcessSpec
            {
                FileName = relativeToolPath,
                WorkingDirectory = temporaryDirectory.Path,
                Arguments = ["hello"],
                Timeout = TimeSpan.FromSeconds(30),
            },
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello", result.Stdout.Trim());
    }

    [Fact]
    public async Task FileLeaseManagerUsesExclusiveJobLeasesAndReleasesAfterDispose()
    {
        using TemporaryDirectory temporaryDirectory = new();
        StaticTimeProvider timeProvider = new(new DateTimeOffset(2026, 3, 15, 13, 5, 0, TimeSpan.Zero));
        LocalFilesystemJobRepository repository = new(
            temporaryDirectory.Path,
            timeProvider,
            new FixedJobIdGenerator("job-20260315-7f3c9b2a"),
            new AtomicFileWriter(),
            new JobEventLogStore(),
            new JobSnapshotRebuilder());
        StoredJob storedJob = await repository.CreateAsync(
            new JobCreateRequest
            {
                Title = "Lease test",
                Description = "Exercise portable locking.",
                CreatedBy = "tester",
            },
            CancellationToken.None);
        FileLeaseManager leaseManager = new(temporaryDirectory.Path, new AtomicFileWriter(), timeProvider);

        await using ILeaseHandle? firstLease = await leaseManager.TryAcquireJobLeaseAsync(storedJob, "worker-a", CancellationToken.None);
        ILeaseHandle? secondLease = await leaseManager.TryAcquireJobLeaseAsync(storedJob, "worker-b", CancellationToken.None);

        Assert.NotNull(firstLease);
        Assert.Null(secondLease);

        await firstLease.DisposeAsync();

        await using ILeaseHandle? thirdLease = await leaseManager.TryAcquireJobLeaseAsync(storedJob, "worker-c", CancellationToken.None);
        Assert.NotNull(thirdLease);
    }

    [Fact]
    public async Task QueuePerformanceSmokeProcessesMultipleQueuedJobs()
    {
        using TemporaryDirectory temporaryDirectory = new();
        StaticTimeProvider timeProvider = new(new DateTimeOffset(2026, 3, 15, 13, 10, 0, TimeSpan.Zero));
        LocalFilesystemJobRepository repository = new(
            temporaryDirectory.Path,
            timeProvider,
            new DefaultJobIdGenerator(),
            new AtomicFileWriter(),
            new JobEventLogStore(),
            new JobSnapshotRebuilder());
        FileLeaseManager leaseManager = new(temporaryDirectory.Path, new AtomicFileWriter(), timeProvider);
        QueueProcessor queueProcessor = new(
            new LocalFilesystemQueueSelector(repository, leaseManager),
            leaseManager,
            new WebhookPlaceholderNotificationSink(),
            new JobEngine(
                repository,
                new RingmasterStateMachine(),
                [
                    new FakeStageRunner(JobStage.PREPARING, StageRole.Planner, JobState.IMPLEMENTING, "Planner completed."),
                    new FakeStageRunner(JobStage.IMPLEMENTING, StageRole.Implementer, JobState.VERIFYING, "Implementer completed."),
                    new FakeStageRunner(JobStage.VERIFYING, StageRole.SystemVerifier, JobState.REVIEWING, "Verifier completed."),
                    new FakeStageRunner(JobStage.REPAIRING, StageRole.Implementer, JobState.VERIFYING, "Repair completed."),
                    new FakeStageRunner(JobStage.REVIEWING, StageRole.Reviewer, JobState.READY_FOR_PR, "Reviewer approved."),
                ],
                timeProvider),
            timeProvider);

        const int jobCount = 12;
        for (int index = 0; index < jobCount; index++)
        {
            await repository.CreateAsync(
                new JobCreateRequest
                {
                    Title = $"Queue performance {index:D2}",
                    Description = "Exercise multi-job throughput.",
                    Priority = 50 + index,
                    CreatedBy = "tester",
                },
                CancellationToken.None);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        int passes = 0;
        while (passes < jobCount * 3)
        {
            passes++;
            await queueProcessor.RunOnceAsync(
                new QueueRunOptions
                {
                    MaxParallelJobs = 1,
                    OwnerId = "perf-smoke",
                },
                CancellationToken.None);

            IReadOnlyList<JobStatusListItem> items = await repository.ListAsync(CancellationToken.None);
            bool allReady = items.All(item => item.State is JobState.READY_FOR_PR);

            if (allReady)
            {
                break;
            }
        }

        stopwatch.Stop();
        IReadOnlyList<JobStatusListItem> finalItems = await repository.ListAsync(CancellationToken.None);

        Assert.Equal(jobCount, finalItems.Count);
        Assert.All(finalItems, item => Assert.Equal(JobState.READY_FOR_PR, item.State));
        Assert.InRange(passes, 1, jobCount * 3);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"Expected the queue smoke run to finish quickly, but it took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task RepoConfigLoaderUpgradesLegacyConfigsWithoutSchemaVersion()
    {
        using TemporaryDirectory temporaryDirectory = new();
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory.Path, "ringmaster.json"),
            """
            {
              "baseBranch": "main",
              "verificationProfiles": {
                "default": {
                  "commands": [
                    {
                      "name": "build",
                      "fileName": "dotnet",
                      "arguments": [ "build" ],
                      "timeoutSeconds": 120
                    }
                  ]
                }
              }
            }
            """);
        RingmasterRepoConfigLoader loader = new();

        RingmasterRepoConfig config = await loader.LoadAsync(temporaryDirectory.Path, CancellationToken.None);

        Assert.Equal(1, config.SchemaVersion);
        Assert.Equal("main", config.BaseBranch);
        Assert.True(config.VerificationProfiles.ContainsKey("default"));
    }

    [Fact]
    public async Task RepoConfigLoaderRejectsFutureSchemaVersions()
    {
        using TemporaryDirectory temporaryDirectory = new();
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory.Path, "ringmaster.json"),
            """
            {
              "schemaVersion": 99,
              "baseBranch": "main",
              "verificationProfiles": {}
            }
            """);
        RingmasterRepoConfigLoader loader = new();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => loader.LoadAsync(temporaryDirectory.Path, CancellationToken.None));

        Assert.Contains("schema version 99", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobRepositoryLoadsLegacyJobAndStatusFilesWithoutSchemaVersion()
    {
        using TemporaryDirectory temporaryDirectory = new();
        StaticTimeProvider timeProvider = new(new DateTimeOffset(2026, 3, 15, 13, 15, 0, TimeSpan.Zero));
        LocalFilesystemJobRepository repository = new(
            temporaryDirectory.Path,
            timeProvider,
            new FixedJobIdGenerator("job-20260315-7f3c9b2a"),
            new AtomicFileWriter(),
            new JobEventLogStore(),
            new JobSnapshotRebuilder());
        StoredJob storedJob = await repository.CreateAsync(
            new JobCreateRequest
            {
                Title = "Legacy status",
                Description = "Exercise schema upgrades.",
                CreatedBy = "tester",
            },
            CancellationToken.None);

        await RemoveSchemaVersionAsync(Path.Combine(storedJob.JobDirectoryPath, "JOB.json"));
        await RemoveSchemaVersionAsync(Path.Combine(storedJob.JobDirectoryPath, "STATUS.json"));

        StoredJob? reloaded = await repository.GetAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(1, reloaded.Definition.SchemaVersion);
        Assert.Equal(1, reloaded.Status.SchemaVersion);
    }

    [Fact]
    public async Task ResumeAsyncAcceptsLegacyRunRecordsWithoutSchemaVersion()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DateTimeOffset startedAt = new(2026, 3, 15, 13, 20, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(startedAt);
        LocalFilesystemJobRepository repository = new(
            temporaryDirectory.Path,
            timeProvider,
            new FixedJobIdGenerator("job-20260315-7f3c9b2a"),
            new AtomicFileWriter(),
            new JobEventLogStore(),
            new JobSnapshotRebuilder());
        StoredJob storedJob = await repository.CreateAsync(
            new JobCreateRequest
            {
                Title = "Legacy run",
                Description = "Exercise run migration.",
                CreatedBy = "tester",
            },
            CancellationToken.None);
        JobRunRecord abandonedRun = new()
        {
            RunId = "0001-preparing-planner",
            JobId = storedJob.Definition.JobId,
            Stage = JobStage.PREPARING,
            Role = StageRole.Planner,
            Attempt = 1,
            StartedAtUtc = startedAt,
            Tool = "legacy-tool",
            Command = ["legacy-tool", "--run"],
            SessionId = "legacy-session",
        };
        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreateStateChanged(storedJob.Definition.JobId, JobState.QUEUED, JobState.PREPARING, startedAt.AddSeconds(-30)),
            CancellationToken.None);
        await repository.SaveRunAsync(storedJob.Definition.JobId, abandonedRun, CancellationToken.None);
        await RemoveSchemaVersionAsync(Path.Combine(storedJob.JobDirectoryPath, "runs", abandonedRun.RunId, "run.json"));
        await WriteStatusAsync(
            temporaryDirectory.Path,
            storedJob.Status with
            {
                State = JobState.PREPARING,
                ResumeState = JobState.PREPARING,
                Execution = new JobExecutionSnapshot
                {
                    Status = ExecutionStatus.Running,
                    RunId = abandonedRun.RunId,
                    Stage = abandonedRun.Stage,
                    Role = abandonedRun.Role,
                    Attempt = abandonedRun.Attempt,
                    StartedAtUtc = abandonedRun.StartedAtUtc,
                    HeartbeatAtUtc = abandonedRun.StartedAtUtc,
                    SessionId = abandonedRun.SessionId,
                },
            });
        JobEngine engine = new(
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

        JobStatusSnapshot status = await engine.ResumeAsync(storedJob.Definition.JobId, CancellationToken.None);
        string abandonedRunJson = await File.ReadAllTextAsync(Path.Combine(storedJob.JobDirectoryPath, "runs", abandonedRun.RunId, "run.json"));

        Assert.Equal(JobState.READY_FOR_PR, status.State);
        Assert.Contains("\"result\": \"Canceled\"", abandonedRunJson, StringComparison.Ordinal);
    }

    private static async Task<string> CreateEchoToolAsync(string repositoryRoot)
    {
        string toolsDirectory = Path.Combine(repositoryRoot, "tools");
        Directory.CreateDirectory(toolsDirectory);

        if (OperatingSystem.IsWindows())
        {
            string toolPath = Path.Combine(toolsDirectory, "echo-arg.cmd");
            await File.WriteAllTextAsync(
                toolPath,
                """
                @echo off
                echo %1
                """);
            return Path.Combine("tools", "echo-arg.cmd");
        }

        string unixToolPath = Path.Combine(toolsDirectory, "echo-arg.sh");
        await File.WriteAllTextAsync(
            unixToolPath,
            """
            #!/usr/bin/env sh
            printf '%s\n' "$1"
            """);
        File.SetUnixFileMode(
            unixToolPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return Path.Combine("tools", "echo-arg.sh");
    }

    private static async Task RemoveSchemaVersionAsync(string path)
    {
        string json = await File.ReadAllTextAsync(path);
        string withoutSchemaVersion = json
            .Replace("\"schemaVersion\": 1,\r\n", string.Empty, StringComparison.Ordinal)
            .Replace("\"schemaVersion\": 1,\n", string.Empty, StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, withoutSchemaVersion);
    }

    private static async Task WriteStatusAsync(string repositoryRoot, JobStatusSnapshot status)
    {
        AtomicFileWriter writer = new();
        await writer.WriteJsonAsync(
            Path.Combine(repositoryRoot, ".ringmaster", "jobs", status.JobId, "STATUS.json"),
            status,
            CancellationToken.None);
    }
}
