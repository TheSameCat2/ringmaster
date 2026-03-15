using Ringmaster.App;
using Ringmaster.Core.Jobs;
using Ringmaster.Git;
using Ringmaster.GitHub;
using Ringmaster.Infrastructure.Configuration;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.Infrastructure.Processes;
using Ringmaster.IntegrationTests.Testing;

namespace Ringmaster.IntegrationTests;

public sealed class PhaseSevenIntegrationTests
{
    [Fact]
    public async Task PublishAsyncFindsExistingPullRequestAndTransitionsJobToDone()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DateTimeOffset createdAt = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        StaticTimeProvider repositoryTimeProvider = new(createdAt);
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path, repositoryTimeProvider);
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Publish ready job"), CancellationToken.None);
        string worktreePath = Path.Combine(temporaryDirectory.Path, "worktree");
        Directory.CreateDirectory(worktreePath);

        JobGitSnapshot gitSnapshot = new()
        {
            RepoRoot = temporaryDirectory.Path,
            BaseBranch = "master",
            BaseCommit = "abc123",
            JobBranch = "ringmaster/j-7f3c9b2a-publish",
            WorktreePath = worktreePath,
            HeadCommit = "abc123",
            ChangedFiles = ["README.md"],
        };

        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreateGitStateCaptured(storedJob.Definition.JobId, gitSnapshot, createdAt.AddMinutes(1)),
            CancellationToken.None);
        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreateStateChanged(storedJob.Definition.JobId, JobState.QUEUED, JobState.READY_FOR_PR, createdAt.AddMinutes(2)),
            CancellationToken.None);

        List<ExternalProcessSpec> calls = [];
        FakeExternalProcessRunner processRunner = new(async (spec, cancellationToken) =>
        {
            calls.Add(spec);

            if (spec.FileName == "git")
            {
                return Success(spec, string.Empty);
            }

            if (spec.FileName == "gh" && spec.Arguments.SequenceEqual(
                    [
                        "pr",
                        "list",
                        "--state",
                        "all",
                        "--head",
                        "ringmaster/j-7f3c9b2a-publish",
                        "--base",
                        "master",
                        "--limit",
                        "1",
                        "--json",
                        "url,isDraft,state",
                    ]))
            {
                return Success(spec, "[{\"url\":\"https://example.test/pr/1\",\"isDraft\":true,\"state\":\"OPEN\"}]");
            }

            await Task.CompletedTask;
            throw new InvalidOperationException($"Unexpected command: {spec.FileName} {string.Join(' ', spec.Arguments)}");
        });

        PullRequestService service = new(
            repository,
            new RingmasterStateMachine(),
            new GitCli(processRunner),
            new GitHubPullRequestProvider(processRunner),
            new StaticTimeProvider(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero)));

        PullRequestOperationResult result = await service.PublishAsync(storedJob.Definition.JobId, CancellationToken.None);
        StoredJob reloaded = await repository.GetAsync(storedJob.Definition.JobId, CancellationToken.None)
            ?? throw new InvalidOperationException("Published job was not reloaded.");

        Assert.True(result.Published);
        Assert.False(result.Created);
        Assert.Equal(JobState.DONE, result.Status.State);
        Assert.Equal(JobState.DONE, reloaded.Status.State);
        Assert.Equal(PullRequestStatus.Draft, reloaded.Status.Pr.Status);
        Assert.Equal("https://example.test/pr/1", reloaded.Status.Pr.Url);
        Assert.Contains(calls, call => call.FileName == "git" && call.Arguments.SequenceEqual(["push", "-u", "origin", "ringmaster/j-7f3c9b2a-publish"]));
        Assert.DoesNotContain(calls, call => call.FileName == "gh" && call.Arguments.Count >= 2 && call.Arguments[0] == "pr" && call.Arguments[1] == "create");
    }

    [Fact]
    public async Task GitHubPullRequestProviderUsesExplicitAutomationFlagsWhenCreatingPullRequests()
    {
        using TemporaryDirectory temporaryDirectory = new();
        List<ExternalProcessSpec> calls = [];
        FakeExternalProcessRunner processRunner = new((spec, _) =>
        {
            calls.Add(spec);

            if (spec.Arguments.Count >= 2 && spec.Arguments[0] == "pr" && spec.Arguments[1] == "list")
            {
                return Task.FromResult(Success(spec, "[]"));
            }

            if (spec.Arguments.Count >= 2 && spec.Arguments[0] == "pr" && spec.Arguments[1] == "create")
            {
                return Task.FromResult(Success(spec, "https://example.test/pr/2"));
            }

            throw new InvalidOperationException($"Unexpected command: {spec.FileName} {string.Join(' ', spec.Arguments)}");
        });
        GitHubPullRequestProvider provider = new(processRunner);

        PullRequestProviderResult result = await provider.OpenOrGetAsync(
            new PullRequestPublicationRequest
            {
                RepositoryRoot = temporaryDirectory.Path,
                WorkingDirectory = temporaryDirectory.Path,
                HeadBranch = "ringmaster/j-7f3c9b2a-feature",
                BaseBranch = "master",
                Title = "Add retry handling",
                BodyPath = Path.Combine(temporaryDirectory.Path, "PR.md"),
                Draft = true,
                Labels = ["automation", "codex"],
            },
            CancellationToken.None);

        ExternalProcessSpec createCall = Assert.Single(calls, call => call.FileName == "gh" && call.Arguments.Count >= 2 && call.Arguments[1] == "create");

        Assert.True(result.Created);
        Assert.Equal(PullRequestStatus.Draft, result.Status);
        Assert.Contains("--head", createCall.Arguments);
        Assert.Contains("ringmaster/j-7f3c9b2a-feature", createCall.Arguments);
        Assert.Contains("--base", createCall.Arguments);
        Assert.Contains("master", createCall.Arguments);
        Assert.Contains("--title", createCall.Arguments);
        Assert.Contains("Add retry handling", createCall.Arguments);
        Assert.Contains("--body-file", createCall.Arguments);
        Assert.Contains("--draft", createCall.Arguments);
        Assert.Equal(2, createCall.Arguments.Count(argument => argument == "--label"));
    }

    [Fact]
    public async Task CleanupServiceRemovesRetainedFinishedWorktreesAndOldRunLogs()
    {
        using TemporaryGitRepository repositoryRoot = new();
        await repositoryRoot.InitializeAsync();

        DateTimeOffset createdAt = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        LocalFilesystemJobRepository repository = CreateRepository(repositoryRoot.Path, new StaticTimeProvider(createdAt));
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Cleanup finished job"), CancellationToken.None);
        GitCli gitCli = new(new ExternalProcessRunner(new StaticTimeProvider(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero))));
        GitWorktreeManager worktreeManager = new(gitCli);
        PreparedWorktree prepared = await worktreeManager.PrepareAsync(
            repositoryRoot.Path,
            storedJob.Definition.JobId,
            storedJob.Definition.Title,
            "master",
            CancellationToken.None);
        JobGitSnapshot gitSnapshot = await worktreeManager.CaptureSnapshotAsync(prepared, CancellationToken.None);

        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreateGitStateCaptured(storedJob.Definition.JobId, gitSnapshot, createdAt.AddMinutes(1)),
            CancellationToken.None);
        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreateStateChanged(storedJob.Definition.JobId, JobState.QUEUED, JobState.READY_FOR_PR, createdAt.AddMinutes(2)),
            CancellationToken.None);
        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreatePullRequestRecorded(
                storedJob.Definition.JobId,
                PullRequestStatus.Open,
                "https://example.test/pr/cleanup",
                draft: false,
                summary: "Created a pull request.",
                createdAt.AddMinutes(3)),
            CancellationToken.None);
        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreateStateChanged(storedJob.Definition.JobId, JobState.READY_FOR_PR, JobState.DONE, createdAt.AddMinutes(4)),
            CancellationToken.None);

        string runDirectory = Path.Combine(storedJob.JobDirectoryPath, "runs", "0001-verifying-system");
        Directory.CreateDirectory(runDirectory);
        string oldLogPath = Path.Combine(runDirectory, "01-verify.log");
        string retainedJsonPath = Path.Combine(runDirectory, "run.json");
        await File.WriteAllTextAsync(oldLogPath, "old log");
        await File.WriteAllTextAsync(retainedJsonPath, "{}");
        File.SetLastWriteTimeUtc(oldLogPath, createdAt.UtcDateTime);
        File.SetLastWriteTimeUtc(retainedJsonPath, createdAt.UtcDateTime);

        CleanupService cleanupService = new(
            repositoryRoot.Path,
            repository,
            new FileLeaseManager(repositoryRoot.Path, new AtomicFileWriter(), new StaticTimeProvider(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero))),
            gitCli,
            new StaticTimeProvider(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero)));

        CleanupResult result = await cleanupService.RunAsync(
            new CleanupOptions
            {
                WorktreeRetention = TimeSpan.FromDays(7),
                ArtifactRetention = TimeSpan.FromDays(30),
            },
            CancellationToken.None);
        IReadOnlyList<GitWorktreeInfo> worktrees = await gitCli.ListWorktreesAsync(repositoryRoot.Path, CancellationToken.None);

        Assert.Equal(1, result.WorktreesRemoved);
        Assert.Equal(1, result.ArtifactFilesRemoved);
        Assert.False(Directory.Exists(prepared.WorktreePath));
        Assert.False(File.Exists(oldLogPath));
        Assert.True(File.Exists(retainedJsonPath));
        Assert.Single(worktrees);
        Assert.Equal(
            TestPathComparer.Normalize(repositoryRoot.Path),
            TestPathComparer.Normalize(worktrees[0].Path));
    }


    [Fact]
    public async Task CleanupServiceSkipsWorktreePathsOutsideManagedRoot()
    {
        using TemporaryGitRepository repositoryRoot = new();
        await repositoryRoot.InitializeAsync();

        DateTimeOffset createdAt = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        LocalFilesystemJobRepository repository = CreateRepository(repositoryRoot.Path, new StaticTimeProvider(createdAt));
        StoredJob storedJob = await repository.CreateAsync(CreateRequest("Cleanup invalid path"), CancellationToken.None);

        string foreignWorktree = Path.Combine(repositoryRoot.Path, "foreign-worktree");
        Directory.CreateDirectory(foreignWorktree);

        JobGitSnapshot tamperedSnapshot = new()
        {
            RepoRoot = repositoryRoot.Path,
            BaseBranch = "master",
            BaseCommit = "HEAD",
            JobBranch = "ringmaster/j-invalid",
            WorktreePath = foreignWorktree,
        };

        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreateGitStateCaptured(storedJob.Definition.JobId, tamperedSnapshot, createdAt.AddMinutes(1)),
            CancellationToken.None);
        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreateStateChanged(storedJob.Definition.JobId, JobState.QUEUED, JobState.READY_FOR_PR, createdAt.AddMinutes(2)),
            CancellationToken.None);
        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreatePullRequestRecorded(
                storedJob.Definition.JobId,
                PullRequestStatus.Open,
                "https://example.test/pr/cleanup",
                draft: false,
                summary: "Created a pull request.",
                createdAt.AddMinutes(3)),
            CancellationToken.None);
        await repository.AppendEventAsync(
            storedJob.Definition.JobId,
            JobEventRecord.CreateStateChanged(storedJob.Definition.JobId, JobState.READY_FOR_PR, JobState.DONE, createdAt.AddMinutes(4)),
            CancellationToken.None);

        GitCli gitCli = new(new ExternalProcessRunner(new StaticTimeProvider(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero))));
        CleanupService cleanupService = new(
            repositoryRoot.Path,
            repository,
            new FileLeaseManager(repositoryRoot.Path, new AtomicFileWriter(), new StaticTimeProvider(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero))),
            gitCli,
            new StaticTimeProvider(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero)));

        CleanupResult result = await cleanupService.RunAsync(
            new CleanupOptions
            {
                WorktreeRetention = TimeSpan.FromDays(7),
                ArtifactRetention = TimeSpan.FromDays(30),
            },
            CancellationToken.None);

        JobCleanupResult jobResult = Assert.Single(result.Jobs);
        Assert.Equal(CleanupDisposition.SkippedInvalidWorktreePath, jobResult.Disposition);
        Assert.True(Directory.Exists(foreignWorktree));
    }

    [Fact]
    public async Task DoctorServiceReportsConfigFailuresWhilePassingToolChecks()
    {
        using TemporaryDirectory temporaryDirectory = new();
        FakeExternalProcessRunner processRunner = new((spec, _) =>
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

            return Task.FromResult(Success(spec, stdout));
        });
        DoctorService service = new(
            temporaryDirectory.Path,
            processRunner,
            new RingmasterRepoConfigLoader(),
            new GitWorktreeManager(new GitCli(processRunner)));

        DoctorReport report = await service.RunAsync(CancellationToken.None);

        Assert.False(report.Succeeded);
        Assert.Contains(report.Checks, check => check.Name == "git availability" && check.Succeeded);
        Assert.Contains(report.Checks, check => check.Name == "codex auth" && check.Succeeded);
        Assert.Contains(report.Checks, check => check.Name == "gh auth" && check.Succeeded);
        Assert.Contains(report.Checks, check => check.Name == "repo config validity" && !check.Succeeded);
    }

    private static LocalFilesystemJobRepository CreateRepository(string repositoryRoot, TimeProvider timeProvider)
    {
        return new LocalFilesystemJobRepository(
            repositoryRoot,
            timeProvider,
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
            Description = "Exercise Phase 7 behavior.",
            CreatedBy = "tester",
        };
    }

    private static ExternalProcessResult Success(ExternalProcessSpec spec, string stdout)
    {
        return new ExternalProcessResult
        {
            FileName = spec.FileName,
            Arguments = spec.Arguments,
            WorkingDirectory = spec.WorkingDirectory,
            EnvironmentVariableNames = [],
            Timeout = spec.Timeout,
            StartedAtUtc = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero),
            CompletedAtUtc = new DateTimeOffset(2026, 3, 15, 12, 0, 1, TimeSpan.Zero),
            ExitCode = 0,
            Stdout = stdout,
            Stderr = string.Empty,
            StdoutPath = spec.StdoutPath,
            StderrPath = spec.StderrPath,
            ProcessId = 4321,
        };
    }
}
