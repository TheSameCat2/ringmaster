using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Ringmaster.Git;
using Ringmaster.Infrastructure.Configuration;
using Ringmaster.Infrastructure.Fakes;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.Infrastructure.Processes;
using Ringmaster.IntegrationTests.Testing;

namespace Ringmaster.IntegrationTests;

public sealed class PhaseThreeIntegrationTests
{
    [Fact]
    public async Task RunAsyncCreatesAWorktreeRunsVerificationAndWritesDiffArtifacts()
    {
        using TemporaryGitRepository repositoryRoot = new();
        await repositoryRoot.InitializeAsync();
        DateTimeOffset timestamp = new(2026, 3, 15, 18, 30, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(timestamp);
        LocalFilesystemJobRepository jobRepository = CreateJobRepository(repositoryRoot.Path, timeProvider);
        StoredJob storedJob = await jobRepository.CreateAsync(CreateRequest(), CancellationToken.None);
        JobEngine engine = CreatePhaseThreeEngine(
            repositoryRoot.Path,
            jobRepository,
            timeProvider,
            new ScriptedStageRunner(
                JobStage.IMPLEMENTING,
                StageRole.Implementer,
                async (context, cancellationToken) =>
                {
                    string worktreePath = context.Job.Status.Git?.WorktreePath
                        ?? throw new InvalidOperationException("The prepared git snapshot did not include a worktree path.");
                    await File.AppendAllTextAsync(
                        System.IO.Path.Combine(worktreePath, "README.md"),
                        "Implemented retry handling." + Environment.NewLine,
                        cancellationToken);
                    return StageExecutionResult.Succeeded(JobState.VERIFYING, "Implementer updated the tracked file.");
                }));

        JobStatusSnapshot status = await engine.RunAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.Equal(JobState.READY_FOR_PR, status.State);
        Assert.NotNull(status.Git);
        Assert.StartsWith("ringmaster/j-7f3c9b2a-", status.Git.JobBranch, StringComparison.Ordinal);
        Assert.Contains(".ringmaster-worktrees", status.Git.WorktreePath, StringComparison.Ordinal);
        Assert.Contains("README.md", status.Git.ChangedFiles, StringComparer.Ordinal);
        Assert.True(status.Git.HasUncommittedChanges);

        string jobArtifacts = System.IO.Path.Combine(storedJob.JobDirectoryPath, "artifacts");
        string diffPatch = await File.ReadAllTextAsync(System.IO.Path.Combine(jobArtifacts, "diff.patch"));
        string diffStat = await File.ReadAllTextAsync(System.IO.Path.Combine(jobArtifacts, "diffstat.txt"));
        string changedFilesJson = await File.ReadAllTextAsync(System.IO.Path.Combine(jobArtifacts, "changed-files.json"));
        string verificationSummaryJson = await File.ReadAllTextAsync(System.IO.Path.Combine(jobArtifacts, "verification-summary.json"));

        Assert.Contains("Implemented retry handling.", diffPatch, StringComparison.Ordinal);
        Assert.Contains("README.md", diffStat, StringComparison.Ordinal);
        Assert.Contains("README.md", changedFilesJson, StringComparison.Ordinal);
        Assert.Contains("\"succeeded\": true", verificationSummaryJson, StringComparison.Ordinal);

        string commandsLog = await File.ReadAllTextAsync(
            System.IO.Path.Combine(storedJob.JobDirectoryPath, "runs", "0003-verifying-system", "commands.jsonl"));
        Assert.Contains("\"fileName\":\"dotnet\"", commandsLog, StringComparison.Ordinal);
        Assert.True(File.Exists(System.IO.Path.Combine(storedJob.JobDirectoryPath, "runs", "0003-verifying-system", "01-verify.log")));
    }

    [Fact]
    public async Task PrepareAsyncReusesTheExistingLinkedWorktree()
    {
        using TemporaryGitRepository repositoryRoot = new();
        await repositoryRoot.InitializeAsync();
        TimeProvider timeProvider = new StaticTimeProvider(new DateTimeOffset(2026, 3, 15, 18, 30, 0, TimeSpan.Zero));
        GitCli gitCli = new(new ExternalProcessRunner(timeProvider));
        GitWorktreeManager worktreeManager = new(gitCli);

        PreparedWorktree first = await worktreeManager.PrepareAsync(repositoryRoot.Path, "job-20260315-7f3c9b2a", "Add retry handling", "master", CancellationToken.None);
        PreparedWorktree second = await worktreeManager.PrepareAsync(repositoryRoot.Path, "job-20260315-7f3c9b2a", "Add retry handling", "master", CancellationToken.None);
        IReadOnlyList<GitWorktreeInfo> worktrees = await gitCli.ListWorktreesAsync(repositoryRoot.Path, CancellationToken.None);

        Assert.Equal(first.WorktreePath, second.WorktreePath);
        Assert.Equal(first.JobBranch, second.JobBranch);
        Assert.Equal(2, worktrees.Count);
    }

    [Fact]
    public async Task TemporaryGitRepositoryDisposeRemovesLinkedWorktrees()
    {
        TemporaryGitRepository repositoryRoot = new();
        bool disposed = false;

        try
        {
            await repositoryRoot.InitializeAsync();
            TimeProvider timeProvider = new StaticTimeProvider(new DateTimeOffset(2026, 3, 15, 18, 30, 0, TimeSpan.Zero));
            GitCli gitCli = new(new ExternalProcessRunner(timeProvider));
            GitWorktreeManager worktreeManager = new(gitCli);
            PreparedWorktree prepared = await worktreeManager.PrepareAsync(
                repositoryRoot.Path,
                "job-20260315-7f3c9b2a",
                "Add retry handling",
                "master",
                CancellationToken.None);
            string repoPath = repositoryRoot.Path;
            string worktreeRoot = worktreeManager.GetWorktreeRoot(repoPath);

            Assert.True(Directory.Exists(prepared.WorktreePath));

            repositoryRoot.Dispose();
            disposed = true;

            Assert.False(Directory.Exists(repoPath));
            Assert.False(Directory.Exists(prepared.WorktreePath));
            Assert.False(Directory.Exists(worktreeRoot));
        }
        finally
        {
            if (!disposed)
            {
                repositoryRoot.Dispose();
            }
        }
    }

    [Fact]
    public async Task RunAsyncBlocksWhenRepositoryConfigIsMissing()
    {
        using TemporaryGitRepository repositoryRoot = new();
        await repositoryRoot.InitializeAsync(includeRepoConfig: false);
        DateTimeOffset timestamp = new(2026, 3, 15, 18, 30, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(timestamp);
        LocalFilesystemJobRepository jobRepository = CreateJobRepository(repositoryRoot.Path, timeProvider);
        StoredJob storedJob = await jobRepository.CreateAsync(CreateRequest(), CancellationToken.None);
        JobEngine engine = CreatePhaseThreeEngine(repositoryRoot.Path, jobRepository, timeProvider);

        JobStatusSnapshot status = await engine.RunAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.Equal(JobState.BLOCKED, status.State);
        Assert.Equal(JobState.PREPARING, status.ResumeState);
        Assert.NotNull(status.Blocker);
        Assert.Equal(BlockerReasonCode.MissingConfiguration, status.Blocker.ReasonCode);
    }

    [Fact]
    public async Task RunAsyncFailsWhenTheBaseBranchCannotBeResolved()
    {
        using TemporaryGitRepository repositoryRoot = new();
        await repositoryRoot.InitializeAsync();
        DateTimeOffset timestamp = new(2026, 3, 15, 18, 30, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(timestamp);
        LocalFilesystemJobRepository jobRepository = CreateJobRepository(repositoryRoot.Path, timeProvider);
        StoredJob storedJob = await jobRepository.CreateAsync(
            CreateRequest() with
            {
                BaseBranch = "missing-branch",
            },
            CancellationToken.None);
        JobEngine engine = CreatePhaseThreeEngine(repositoryRoot.Path, jobRepository, timeProvider);

        JobStatusSnapshot status = await engine.RunAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.Equal(JobState.FAILED, status.State);
        Assert.NotNull(status.LastFailure);
        Assert.Equal(FailureCategory.ToolFailure, status.LastFailure.Category);
        Assert.Contains("Git command failed", status.LastFailure.Summary, StringComparison.Ordinal);
    }

    private static LocalFilesystemJobRepository CreateJobRepository(string repositoryRoot, TimeProvider timeProvider)
    {
        return new LocalFilesystemJobRepository(
            repositoryRoot,
            timeProvider,
            new FixedJobIdGenerator("job-20260315-7f3c9b2a"),
            new AtomicFileWriter(),
            new JobEventLogStore(),
            new JobSnapshotRebuilder());
    }

    private static JobEngine CreatePhaseThreeEngine(
        string repositoryRoot,
        IJobRepository jobRepository,
        TimeProvider timeProvider,
        IStageRunner? implementingRunner = null)
    {
        RingmasterRepoConfigLoader repoConfigLoader = new();
        ExternalProcessRunner processRunner = new(timeProvider);
        AtomicFileWriter atomicFileWriter = new();
        GitCli gitCli = new(processRunner);
        GitWorktreeManager worktreeManager = new(gitCli);

        return new JobEngine(
            jobRepository,
            new RingmasterStateMachine(),
            [
                new PreparingStageRunner(repositoryRoot, repoConfigLoader, worktreeManager, jobRepository, timeProvider),
                implementingRunner ?? new FakeStageRunner(JobStage.IMPLEMENTING, StageRole.Implementer, JobState.VERIFYING, "Implementer completed."),
                new VerifyingStageRunner(
                    repositoryRoot,
                    repoConfigLoader,
                    processRunner,
                    gitCli,
                    worktreeManager,
                    atomicFileWriter,
                    jobRepository,
                    timeProvider,
                    new DeterministicFailureClassifier(),
                    new RepairLoopPolicyEvaluator(new RepairLoopPolicy())),
                new FakeStageRunner(JobStage.REPAIRING, StageRole.Implementer, JobState.VERIFYING, "Repair completed."),
                new FakeStageRunner(JobStage.REVIEWING, StageRole.Reviewer, JobState.READY_FOR_PR, "Reviewer approved."),
            ],
            timeProvider);
    }

    private static JobCreateRequest CreateRequest()
    {
        return new JobCreateRequest
        {
            Title = "Add retry handling",
            Description = "Implement bounded retries for retryable failures.",
            CreatedBy = "tester",
            VerificationProfile = "default",
            BaseBranch = "master",
        };
    }
}
