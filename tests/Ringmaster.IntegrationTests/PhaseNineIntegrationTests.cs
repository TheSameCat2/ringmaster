using System.Text;
using Ringmaster.App;
using Ringmaster.App.CommandLine;
using Ringmaster.Core.Configuration;
using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Ringmaster.Git;
using Ringmaster.Infrastructure.Configuration;
using Ringmaster.Infrastructure.Fakes;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.Infrastructure.Processes;
using Ringmaster.IntegrationTests.Testing;
using Spectre.Console.Testing;

namespace Ringmaster.IntegrationTests;

public sealed class PhaseNineIntegrationTests
{
    [Fact]
    public async Task InitCommandScaffoldsRuntimeConfigAndGitIgnore()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        await File.WriteAllTextAsync(Path.Combine(temporaryDirectory.Path, "SampleRepo.sln"), string.Empty);
        RingmasterCli cli = CreateCli(console, temporaryDirectory.Path);

        int exitCode = cli.CreateRootCommand().Parse(["init", "--base-branch", "main", "--json"]).Invoke();

        string configJson = await File.ReadAllTextAsync(Path.Combine(temporaryDirectory.Path, "ringmaster.json"));
        RingmasterRepoConfig config = RingmasterJsonSerializer.Deserialize<RingmasterRepoConfig>(configJson);
        string gitIgnore = await File.ReadAllTextAsync(Path.Combine(temporaryDirectory.Path, ".gitignore"));

        Assert.Equal(OperatorExitCodes.Success, exitCode);
        Assert.True(Directory.Exists(Path.Combine(temporaryDirectory.Path, ".ringmaster", "runtime")));
        Assert.True(Directory.Exists(Path.Combine(temporaryDirectory.Path, ".ringmaster", "jobs")));
        Assert.True(File.Exists(Path.Combine(temporaryDirectory.Path, ".ringmaster", "runtime", "notifications.jsonl")));
        Assert.Contains(".ringmaster/", gitIgnore, StringComparison.Ordinal);
        Assert.Equal("main", config.BaseBranch);
        Assert.True(config.VerificationProfiles.TryGetValue("default", out VerificationProfileDefinition? profile));
        Assert.NotNull(profile);
        Assert.Equal(2, profile.Commands.Count);
        Assert.Equal(["build", "SampleRepo.sln"], profile.Commands[0].Arguments);
        Assert.Contains("\"configCreated\": true", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobResumeCommandResumesBlockedJobsFromResumeState()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Resume blocked job"), CancellationToken.None);
        await AppendBlockedStatusAsync(repository, storedJob.Definition.JobId, JobState.IMPLEMENTING, "Need a persistence decision.");
        RingmasterCli cli = CreateCli(console, temporaryDirectory.Path, repository);

        int exitCode = cli.CreateRootCommand().Parse(["job", "resume", storedJob.Definition.JobId, "--json"]).Invoke();
        StoredJob reloaded = await repository.GetAsync(storedJob.Definition.JobId, CancellationToken.None)
            ?? throw new InvalidOperationException("Expected the job to exist after resuming it.");

        Assert.Equal(OperatorExitCodes.Success, exitCode);
        Assert.Equal(JobState.READY_FOR_PR, reloaded.Status.State);
        Assert.Contains("\"state\": \"READY_FOR_PR\"", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobResumeCommandPublishesReadyForPrJobs()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Publish PR"), CancellationToken.None);
        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreateStateChanged(
                storedJob.Definition.JobId,
                JobState.QUEUED,
                JobState.READY_FOR_PR,
                new DateTimeOffset(2026, 3, 15, 17, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        JobStatusSnapshot doneStatus = storedJob.Status with
        {
            State = JobState.DONE,
            Pr = new JobPullRequestSnapshot
            {
                Status = PullRequestStatus.Open,
                Url = "https://example.test/pr/42",
                Draft = false,
            },
        };
        RingmasterCli cli = CreateCli(
            console,
            temporaryDirectory.Path,
            repository,
            pullRequestService: new DelegatePullRequestService(
                publishAsync: _ => Task.FromResult(
                    new PullRequestOperationResult
                    {
                        JobId = storedJob.Definition.JobId,
                        Status = doneStatus,
                        PullRequestStatus = PullRequestStatus.Open,
                        Url = "https://example.test/pr/42",
                        Attempted = true,
                        Published = true,
                        Created = true,
                        Summary = "Created a pull request.",
                    })));

        int exitCode = cli.CreateRootCommand().Parse(["job", "resume", storedJob.Definition.JobId, "--json"]).Invoke();

        Assert.Equal(OperatorExitCodes.Success, exitCode);
        Assert.Contains("\"pullRequestAttempted\": true", console.Output, StringComparison.Ordinal);
        Assert.Contains("\"url\": \"https://example.test/pr/42\"", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobUnblockCommandStoresHumanInputAndResumes()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Unblock job"), CancellationToken.None);
        await AppendBlockedStatusAsync(repository, storedJob.Definition.JobId, JobState.IMPLEMENTING, "Need retry guidance.");
        RingmasterCli cli = CreateCli(console, temporaryDirectory.Path, repository);

        int exitCode = cli.CreateRootCommand().Parse(
            ["job", "unblock", storedJob.Definition.JobId, "--message", "Use in-memory retry only.", "--json"]).Invoke();
        string notes = await File.ReadAllTextAsync(Path.Combine(storedJob.JobDirectoryPath, "NOTES.md"));
        StoredJob reloaded = await repository.GetAsync(storedJob.Definition.JobId, CancellationToken.None)
            ?? throw new InvalidOperationException("Expected the job to exist after unblocking it.");

        Assert.Equal(OperatorExitCodes.Success, exitCode);
        Assert.Equal(JobState.READY_FOR_PR, reloaded.Status.State);
        Assert.Contains("Human Input", notes, StringComparison.Ordinal);
        Assert.Contains("Use in-memory retry only.", notes, StringComparison.Ordinal);
        Assert.Contains("\"notesUpdated\": true", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobCancelCommandMarksTheJobFailedAndReturnsFailureExitCode()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Cancel job"), CancellationToken.None);
        RingmasterCli cli = CreateCli(console, temporaryDirectory.Path, repository);

        int exitCode = cli.CreateRootCommand().Parse(["job", "cancel", storedJob.Definition.JobId, "--json"]).Invoke();
        StoredJob reloaded = await repository.GetAsync(storedJob.Definition.JobId, CancellationToken.None)
            ?? throw new InvalidOperationException("Expected the job to exist after cancellation.");
        string notes = await File.ReadAllTextAsync(Path.Combine(storedJob.JobDirectoryPath, "NOTES.md"));

        Assert.Equal(OperatorExitCodes.Failed, exitCode);
        Assert.Equal(JobState.FAILED, reloaded.Status.State);
        Assert.Contains("Operator Cancellation", notes, StringComparison.Ordinal);
        Assert.Contains("\"state\": \"FAILED\"", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusCommandReturnsBlockedExitCodeForBlockedJobs()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Blocked status"), CancellationToken.None);
        await AppendBlockedStatusAsync(repository, storedJob.Definition.JobId, JobState.VERIFYING, "Need credentials.");
        RingmasterCli cli = CreateCli(console, temporaryDirectory.Path, repository);

        int exitCode = cli.CreateRootCommand().Parse(["status", "--job-id", storedJob.Definition.JobId, "--json"]).Invoke();

        Assert.Equal(OperatorExitCodes.Blocked, exitCode);
        Assert.Contains("\"state\": \"BLOCKED\"", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogsCommandDefaultsToTheLatestRunLog()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Inspect logs"), CancellationToken.None);
        await WriteRunLogAsync(storedJob, repository, "0001-preparing-planner", "first log");
        await WriteRunLogAsync(storedJob, repository, "0002-implementing-implementer", "second log");
        RingmasterCli cli = CreateCli(console, temporaryDirectory.Path, repository);

        int exitCode = cli.CreateRootCommand().Parse(["logs", storedJob.Definition.JobId]).Invoke();

        Assert.Equal(OperatorExitCodes.Success, exitCode);
        Assert.DoesNotContain("first log", console.Output, StringComparison.Ordinal);
        Assert.Contains("second log", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusDisplayServiceReportsWatchFieldsForActiveJobs()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DateTimeOffset now = new(2026, 3, 15, 17, 10, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(now);
        LocalFilesystemJobRepository repository = new(
            temporaryDirectory.Path,
            timeProvider,
            new FixedJobIdGenerator("job-20260315-7f3c9b2a"),
            new AtomicFileWriter(),
            new JobEventLogStore(),
            new JobSnapshotRebuilder());
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Watch status"), CancellationToken.None);
        JobStatusSnapshot runningStatus = storedJob.Status with
        {
            State = JobState.VERIFYING,
            ResumeState = JobState.VERIFYING,
            Execution = new JobExecutionSnapshot
            {
                Status = ExecutionStatus.Running,
                RunId = "0003-verifying-system",
                Stage = JobStage.VERIFYING,
                Role = StageRole.SystemVerifier,
                Attempt = 2,
                StartedAtUtc = now.AddMinutes(-3),
                HeartbeatAtUtc = now.AddSeconds(-5),
            },
            Attempts = new JobAttemptCounters
            {
                Preparing = 1,
                Implementing = 1,
                Verifying = 2,
                Repairing = 1,
                Reviewing = 0,
            },
            LastFailure = new JobFailureSnapshot
            {
                Category = FailureCategory.RepairableCodeFailure,
                Signature = "verify:test:RetryTests.Should_retry",
                Summary = "One retry test is still failing.",
                FirstSeenAtUtc = now.AddMinutes(-10),
                LastSeenAtUtc = now.AddMinutes(-4),
                RepetitionCount = 1,
            },
            Pr = new JobPullRequestSnapshot
            {
                Status = PullRequestStatus.Open,
                Url = "https://example.test/pr/77",
                Draft = false,
            },
            UpdatedAtUtc = now,
        };
        await WriteStatusAsync(temporaryDirectory.Path, runningStatus);
        StatusDisplayService service = new(repository, timeProvider);

        IReadOnlyList<StatusDisplayItem> snapshot = await service.GetSnapshotAsync(storedJob.Definition.JobId, CancellationToken.None);

        StatusDisplayItem item = Assert.Single(snapshot);
        Assert.Equal(JobState.VERIFYING, item.State);
        Assert.Equal(JobStage.VERIFYING, item.CurrentStage);
        Assert.Equal("0003-verifying-system", item.ActiveRunId);
        Assert.Equal(TimeSpan.FromMinutes(3), item.Elapsed);
        Assert.Equal(2, item.RetryCount);
        Assert.Equal("One retry test is still failing.", item.LastFailureSummary);
        Assert.Equal("https://example.test/pr/77", item.PullRequestUrl);
    }

    [Fact]
    public async Task RunLogServiceFollowsAppendedContent()
    {
        using TemporaryDirectory temporaryDirectory = new();
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Follow logs"), CancellationToken.None);
        string logPath = await WriteRunLogAsync(storedJob, repository, "0001-preparing-planner", "first line" + Environment.NewLine);
        RunLogService logService = new(repository);
        RunLogSelection selection = await logService.SelectAsync(storedJob.Definition.JobId, "0001-preparing-planner", CancellationToken.None);

        StringBuilder buffer = new();
        using CancellationTokenSource cancellation = new();
        Task followTask = logService.FollowAsync(
            selection,
            (chunk, _) =>
            {
                buffer.Append(chunk);
                if (buffer.ToString().Contains("second line", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                }

                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(25),
            cancellation.Token);

        await Task.Delay(100);
        await File.AppendAllTextAsync(logPath, "second line" + Environment.NewLine, CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await followTask);

        Assert.Contains("first line", buffer.ToString(), StringComparison.Ordinal);
        Assert.Contains("second line", buffer.ToString(), StringComparison.Ordinal);
    }

    private static async Task AppendBlockedStatusAsync(
        LocalFilesystemJobRepository repository,
        string jobId,
        JobState resumeState,
        string summary)
    {
        DateTimeOffset transitionTime = new(2026, 3, 15, 16, 50, 0, TimeSpan.Zero);
        await repository.AppendEventAsync(
            jobId,
            JobEventRecord.CreateStateChanged(jobId, JobState.QUEUED, resumeState, transitionTime),
            CancellationToken.None);
        await repository.AppendEventAsync(
            jobId,
            JobEventRecord.CreateBlocked(
                jobId,
                new BlockerInfo
                {
                    ReasonCode = BlockerReasonCode.ArchitectureDecision,
                    Summary = summary,
                    Questions = ["What is the intended behavior?"],
                    ResumeState = resumeState,
                },
                transitionTime.AddMinutes(1)),
            CancellationToken.None);
    }

    private static async Task<string> WriteRunLogAsync(
        StoredJob storedJob,
        LocalFilesystemJobRepository repository,
        string runId,
        string content)
    {
        JobRunRecord run = new()
        {
            RunId = runId,
            JobId = storedJob.Definition.JobId,
            Stage = JobStage.IMPLEMENTING,
            Role = StageRole.Implementer,
            Attempt = 1,
            StartedAtUtc = new DateTimeOffset(2026, 3, 15, 16, 55, 0, TimeSpan.Zero),
            CompletedAtUtc = new DateTimeOffset(2026, 3, 15, 16, 55, 10, TimeSpan.Zero),
            Tool = "codex",
            Command = ["codex", "exec"],
            Result = RunResult.Completed,
            ExitCode = 0,
            Artifacts = new RunArtifacts
            {
                EventLog = "codex.events.jsonl",
            },
        };
        await repository.SaveRunAsync(storedJob.Definition.JobId, run, CancellationToken.None);

        string logPath = Path.Combine(storedJob.JobDirectoryPath, "runs", runId, "codex.events.jsonl");
        await File.WriteAllTextAsync(logPath, content);
        return logPath;
    }

    private static RingmasterCli CreateCli(
        TestConsole console,
        string repositoryRoot,
        LocalFilesystemJobRepository? repository = null,
        IPullRequestService? pullRequestService = null,
        IExternalProcessRunner? processRunner = null)
    {
        DateTimeOffset createdAt = new(2026, 3, 15, 16, 45, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(createdAt);
        LocalFilesystemJobRepository effectiveRepository = repository ?? CreateRepository(repositoryRoot);
        JobEngine jobEngine = new(
            effectiveRepository,
            new RingmasterStateMachine(),
            [
                new FakeStageRunner(JobStage.PREPARING, StageRole.Planner, JobState.IMPLEMENTING, "Planner completed."),
                new FakeStageRunner(JobStage.IMPLEMENTING, StageRole.Implementer, JobState.VERIFYING, "Implementer completed."),
                new FakeStageRunner(JobStage.VERIFYING, StageRole.SystemVerifier, JobState.REVIEWING, "Verifier completed."),
                new FakeStageRunner(JobStage.REPAIRING, StageRole.Implementer, JobState.VERIFYING, "Repair completed."),
                new FakeStageRunner(JobStage.REVIEWING, StageRole.Reviewer, JobState.READY_FOR_PR, "Reviewer approved."),
            ],
            timeProvider);
        FileLeaseManager leaseManager = new(repositoryRoot, new AtomicFileWriter(), timeProvider);
        IExternalProcessRunner effectiveProcessRunner = processRunner ?? CreateSuccessfulProcessRunner();
        GitCli gitCli = new(effectiveProcessRunner);
        QueueProcessor queueProcessor = new(
            new LocalFilesystemQueueSelector(effectiveRepository, leaseManager),
            leaseManager,
            new WebhookPlaceholderNotificationSink(),
            jobEngine,
            timeProvider);
        IPullRequestService effectivePullRequestService = pullRequestService ?? new DelegatePullRequestService(effectiveRepository);
        DoctorService doctorService = new(
            repositoryRoot,
            effectiveProcessRunner,
            new RingmasterRepoConfigLoader(),
            new GitWorktreeManager(gitCli));
        RepositoryInitializationService initializationService = new(
            repositoryRoot,
            new AtomicFileWriter());
        JobOperatorService jobOperatorService = new(
            effectiveRepository,
            jobEngine,
            effectivePullRequestService,
            new RingmasterStateMachine(),
            new AtomicFileWriter(),
            timeProvider,
            new RingmasterApplicationContext(repositoryRoot, "tester"));
        RunLogService runLogService = new(effectiveRepository);
        StatusDisplayService statusDisplayService = new(effectiveRepository, timeProvider);
        CleanupService cleanupService = new(
            repositoryRoot,
            effectiveRepository,
            leaseManager,
            gitCli,
            timeProvider);

        return new RingmasterCli(
            console,
            effectiveRepository,
            jobEngine,
            queueProcessor,
            effectivePullRequestService,
            doctorService,
            initializationService,
            jobOperatorService,
            runLogService,
            statusDisplayService,
            cleanupService,
            new RingmasterApplicationContext(repositoryRoot, "tester"));
    }

    private static LocalFilesystemJobRepository CreateRepository(string repositoryRoot)
    {
        return new LocalFilesystemJobRepository(
            repositoryRoot,
            new StaticTimeProvider(new DateTimeOffset(2026, 3, 15, 16, 45, 0, TimeSpan.Zero)),
            new FixedJobIdGenerator("job-20260315-7f3c9b2a"),
            new AtomicFileWriter(),
            new JobEventLogStore(),
            new JobSnapshotRebuilder());
    }

    private static JobCreateRequest CreateRequest(string title)
    {
        return new JobCreateRequest
        {
            Title = title,
            Description = "Exercise Phase 9 behavior.",
            CreatedBy = "tester",
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

    private static IExternalProcessRunner CreateSuccessfulProcessRunner()
    {
        return new FakeExternalProcessRunner((spec, _) =>
        {
            string stdout = spec.FileName switch
            {
                "git" => "git version test",
                "codex" when spec.Arguments.SequenceEqual(["login", "status"]) => "Logged in",
                "codex" => "codex-cli test",
                "gh" when spec.Arguments.SequenceEqual(["auth", "status"]) => "github.com authenticated",
                "gh" => "gh version test",
                _ => string.Empty,
            };

            return Task.FromResult(
                new ExternalProcessResult
                {
                    FileName = spec.FileName,
                    Arguments = spec.Arguments,
                    WorkingDirectory = spec.WorkingDirectory,
                    EnvironmentVariableNames = [],
                    Timeout = spec.Timeout,
                    StartedAtUtc = new DateTimeOffset(2026, 3, 15, 16, 45, 0, TimeSpan.Zero),
                    CompletedAtUtc = new DateTimeOffset(2026, 3, 15, 16, 45, 1, TimeSpan.Zero),
                    ExitCode = 0,
                    Stdout = stdout,
                    Stderr = string.Empty,
                    StdoutPath = spec.StdoutPath,
                    StderrPath = spec.StderrPath,
                    ProcessId = 1234,
                });
        });
    }

    private sealed class DelegatePullRequestService(
        IJobRepository? jobRepository = null,
        Func<string, Task<PullRequestOperationResult>>? publishAsync = null,
        Func<string, Task<PullRequestOperationResult>>? publishIfConfiguredAsync = null) : IPullRequestService
    {
        public async Task<PullRequestOperationResult> PublishAsync(string jobId, CancellationToken cancellationToken)
        {
            if (publishAsync is not null)
            {
                return await publishAsync(jobId);
            }

            return await CreateNoOpResultAsync(jobId, "Pull request publication was not configured.", cancellationToken);
        }

        public async Task<PullRequestOperationResult> PublishIfConfiguredAsync(string jobId, CancellationToken cancellationToken)
        {
            if (publishIfConfiguredAsync is not null)
            {
                return await publishIfConfiguredAsync(jobId);
            }

            return await CreateNoOpResultAsync(jobId, "Pull request auto-open was not attempted.", cancellationToken);
        }

        private async Task<PullRequestOperationResult> CreateNoOpResultAsync(
            string jobId,
            string summary,
            CancellationToken cancellationToken)
        {
            JobStatusSnapshot status = await LoadStatusAsync(jobId, cancellationToken);
            return new PullRequestOperationResult
            {
                JobId = jobId,
                Status = status,
                PullRequestStatus = status.Pr.Status,
                Url = status.Pr.Url,
                Summary = summary,
            };
        }

        private async Task<JobStatusSnapshot> LoadStatusAsync(string jobId, CancellationToken cancellationToken)
        {
            if (jobRepository is not null)
            {
                StoredJob? job = await jobRepository.GetAsync(jobId, cancellationToken);
                if (job is not null)
                {
                    return job.Status;
                }
            }

            return JobStatusSnapshot.CreateInitial(
                new JobDefinition
                {
                    JobId = jobId,
                    Title = "stub",
                    Description = "stub",
                    Repo = new JobRepositoryTarget
                    {
                        BaseBranch = "master",
                        VerificationProfile = "default",
                    },
                    CreatedAtUtc = new DateTimeOffset(2026, 3, 15, 16, 45, 0, TimeSpan.Zero),
                    CreatedBy = "tester",
                });
        }
    }
}
