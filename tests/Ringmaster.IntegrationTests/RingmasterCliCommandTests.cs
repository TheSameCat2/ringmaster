using Ringmaster.App;
using Ringmaster.App.CommandLine;
using Ringmaster.Core.Jobs;
using Ringmaster.Infrastructure.Configuration;
using Ringmaster.Infrastructure.Fakes;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.Infrastructure.Processes;
using Ringmaster.IntegrationTests.Testing;
using Ringmaster.Git;
using Spectre.Console.Testing;

namespace Ringmaster.IntegrationTests;

public sealed class RingmasterCliCommandTests
{
    [Fact]
    public void JobCreateShowAndStatusCommandsWorkAgainstTheFilesystemRepository()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        RingmasterCli cli = CreateCli(console, temporaryDirectory.Path);

        int createExitCode = cli.CreateRootCommand().Parse(
            ["job", "create", "--title", "Add retry handling", "--description", "Implement bounded retries.", "--json"]).Invoke();
        int showExitCode = cli.CreateRootCommand().Parse(
            ["job", "show", "job-20260315-7f3c9b2a", "--json"]).Invoke();
        int statusExitCode = cli.CreateRootCommand().Parse(
            ["status", "--json"]).Invoke();

        string output = console.Output;

        Assert.Equal(0, createExitCode);
        Assert.Equal(0, showExitCode);
        Assert.Equal(0, statusExitCode);
        Assert.Contains("\"jobId\": \"job-20260315-7f3c9b2a\"", output, StringComparison.Ordinal);
        Assert.Contains("\"state\": \"QUEUED\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void JobRunCommandUsesFakeStageRunnersToReachReadyForPr()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        RingmasterCli cli = CreateCli(console, temporaryDirectory.Path);

        int createExitCode = cli.CreateRootCommand().Parse(
            ["job", "create", "--title", "Add retry handling", "--description", "Implement bounded retries.", "--json"]).Invoke();
        int runExitCode = cli.CreateRootCommand().Parse(
            ["job", "run", "job-20260315-7f3c9b2a", "--json"]).Invoke();

        string output = console.Output;

        Assert.Equal(0, createExitCode);
        Assert.Equal(0, runExitCode);
        Assert.Contains("\"state\": \"READY_FOR_PR\"", output, StringComparison.Ordinal);
        Assert.Contains("\"attempts\": {", output, StringComparison.Ordinal);
    }

    [Fact]
    public void QueueOnceCommandRunsQueuedJobs()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        RingmasterCli cli = CreateCli(console, temporaryDirectory.Path);

        int createExitCode = cli.CreateRootCommand().Parse(
            ["job", "create", "--title", "Add retry handling", "--description", "Implement bounded retries.", "--json"]).Invoke();
        int queueExitCode = cli.CreateRootCommand().Parse(
            ["queue", "once", "--json"]).Invoke();
        int statusExitCode = cli.CreateRootCommand().Parse(
            ["status", "--job-id", "job-20260315-7f3c9b2a", "--json"]).Invoke();

        string output = console.Output;

        Assert.Equal(0, createExitCode);
        Assert.Equal(0, queueExitCode);
        Assert.Equal(0, statusExitCode);
        Assert.Contains("\"disposition\": \"Started\"", output, StringComparison.Ordinal);
        Assert.Contains("\"state\": \"READY_FOR_PR\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrOpenCommandUsesThePullRequestService()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Ready for PR"), CancellationToken.None);
        JobStatusSnapshot publishedStatus = storedJob.Status with
        {
            State = JobState.DONE,
            Pr = new JobPullRequestSnapshot
            {
                Status = PullRequestStatus.Open,
                Url = "https://example.test/pr/1",
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
                        Status = publishedStatus,
                        PullRequestStatus = PullRequestStatus.Open,
                        Url = "https://example.test/pr/1",
                        Attempted = true,
                        Published = true,
                        Created = true,
                        Summary = "Created a pull request.",
                    }),
                publishIfConfiguredAsync: _ => Task.FromResult(
                    new PullRequestOperationResult
                    {
                        JobId = storedJob.Definition.JobId,
                        Status = storedJob.Status,
                        Summary = "Pull request auto-open was not attempted.",
                    })));

        int exitCode = cli.CreateRootCommand().Parse(["pr", "open", storedJob.Definition.JobId, "--json"]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("\"url\": \"https://example.test/pr/1\"", console.Output, StringComparison.Ordinal);
        Assert.Contains("\"published\": true", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorktreeOpenCommandPrintsTheRecordedPath()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Inspect worktree"), CancellationToken.None);
        string worktreePath = Path.Combine(temporaryDirectory.Path, "linked-worktree");
        Directory.CreateDirectory(worktreePath);

        await WriteStatusAsync(
            temporaryDirectory.Path,
            storedJob.Status with
            {
                Git = new JobGitSnapshot
                {
                    RepoRoot = temporaryDirectory.Path,
                    BaseBranch = "master",
                    BaseCommit = "abc123",
                    JobBranch = "ringmaster/j-7f3c9b2a-test",
                    WorktreePath = worktreePath,
                    HeadCommit = "abc123",
                    ChangedFiles = [],
                },
            });

        RingmasterCli cli = CreateCli(console, temporaryDirectory.Path, repository);

        int exitCode = cli.CreateRootCommand().Parse(["worktree", "open", storedJob.Definition.JobId]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains(worktreePath, console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void DoctorCommandReturnsNonZeroWhenRepoConfigIsMissing()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        RingmasterCli cli = CreateCli(
            console,
            temporaryDirectory.Path,
            processRunner: CreateSuccessfulProcessRunner());

        int exitCode = cli.CreateRootCommand().Parse(["doctor", "--json"]).Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("\"name\": \"repo config validity\"", console.Output, StringComparison.Ordinal);
        Assert.Contains("\"succeeded\": false", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleanupCommandEmitsJobResults()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Clean finished job"), CancellationToken.None);

        await WriteStatusAsync(
            temporaryDirectory.Path,
            storedJob.Status with
            {
                State = JobState.FAILED,
                UpdatedAtUtc = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            });

        RingmasterCli cli = CreateCli(console, temporaryDirectory.Path, repository);

        int exitCode = cli.CreateRootCommand().Parse(["cleanup", "--json"]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains(storedJob.Definition.JobId, console.Output, StringComparison.Ordinal);
        Assert.Contains("\"disposition\": \"SkippedNoWorktree\"", console.Output, StringComparison.Ordinal);
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
        DoctorService doctorService = new(
            repositoryRoot,
            effectiveProcessRunner,
            new RingmasterRepoConfigLoader(),
            new GitWorktreeManager(gitCli));
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
            pullRequestService ?? new DelegatePullRequestService(effectiveRepository),
            doctorService,
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
            Description = "Exercise CLI behavior.",
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
