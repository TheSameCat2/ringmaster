using Ringmaster.Codex;
using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Ringmaster.Git;
using Ringmaster.Infrastructure.Configuration;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.Infrastructure.Processes;
using Ringmaster.IntegrationTests.Testing;

namespace Ringmaster.IntegrationTests;

public sealed class ImplementerBlockedWithChangesTests
{
    [Fact]
    public async Task ImplementerBlockedButHasChanges_ProceedsToVerifying()
    {
        using TemporaryGitRepository repositoryRoot = new();
        await repositoryRoot.InitializeAsync();
        DateTimeOffset timestamp = new(2026, 3, 15, 19, 15, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(timestamp);
        LocalFilesystemJobRepository jobRepository = CreateJobRepository(repositoryRoot.Path, timeProvider);
        StoredJob storedJob = await jobRepository.CreateAsync(CreateRequest(), CancellationToken.None);
        RepositoryPreparationService preparationService = new(
            repositoryRoot.Path,
            new RingmasterRepoConfigLoader(),
            new GitWorktreeManager(new GitCli(new ExternalProcessRunner(timeProvider))),
            jobRepository,
            timeProvider);

        IAgentRunner agentRunner = new CodexAgentRunner(
            new FakeCodexRunner(async (request, cancellationToken) =>
            {
                await File.WriteAllTextAsync(request.EventLogPath, "{\"type\":\"thread.started\"}" + Environment.NewLine, cancellationToken);
                await File.WriteAllTextAsync(request.StderrPath, string.Empty, cancellationToken);

                if (request.Kind == AgentRunKind.Planner)
                {
                    string output = RingmasterJsonSerializer.SerializeCompact(
                        new PlannerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Plan ready.",
                            PlanMarkdown = "# Plan\n\n- Update README.\n",
                            NeedsHuman = false,
                            Questions = [],
                        });
                    await File.WriteAllTextAsync(request.OutputLastMessagePath, output, cancellationToken);
                    return new CodexExecResult
                    {
                        ExitCode = 0,
                        TimedOut = false,
                        SessionId = "planner-session",
                        FinalOutputText = output,
                    };
                }

                // Implementer writes a file and then returns blocked
                await File.WriteAllTextAsync(
                    Path.Combine(request.WorkingDirectory, "new-file.txt"),
                    "I was here." + Environment.NewLine,
                    cancellationToken);

                string implementerOutput = RingmasterJsonSerializer.SerializeCompact(
                    new ImplementerAgentOutput
                    {
                        Result = "blocked",
                        Summary = "I got stuck but made some changes first.",
                        FilesModified = ["new-file.txt"],
                        RecommendedNextChecks = [],
                        NeedsHuman = true,
                        Questions = ["What should I do next?"],
                    });
                await File.WriteAllTextAsync(request.OutputLastMessagePath, implementerOutput, cancellationToken);

                return new CodexExecResult
                {
                    ExitCode = 0,
                    TimedOut = false,
                    SessionId = "implementer-session",
                    FinalOutputText = implementerOutput,
                };
            }),
            new AtomicFileWriter());

        CodexPromptBuilder promptBuilder = new();
        GitCli gitCli = new(new ExternalProcessRunner(timeProvider));
        ImplementingStageRunner runner = new(agentRunner, promptBuilder, new AtomicFileWriter(), gitCli);

        // Run planner first
        PlanningStageRunner planner = new(
            preparationService,
            agentRunner,
            promptBuilder,
            new AtomicFileWriter());
        StageExecutionContext plannerContext = CreateContext(storedJob, JobStage.PREPARING, StageRole.Planner);
        await planner.RunAsync(plannerContext, CancellationToken.None);

        storedJob = await jobRepository.GetAsync(storedJob.Definition.JobId, CancellationToken.None)
            ?? throw new InvalidOperationException("Job not found.");

        StageExecutionContext implementerContext = CreateContext(storedJob, JobStage.IMPLEMENTING, StageRole.Implementer);
        StageExecutionResult result = await runner.RunAsync(implementerContext, CancellationToken.None);

        Assert.Equal(StageExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(JobState.VERIFYING, result.NextState);
        Assert.Contains("made changes", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImplementerBlockedWithNoChanges_ReturnsBlocked()
    {
        using TemporaryGitRepository repositoryRoot = new();
        await repositoryRoot.InitializeAsync();
        DateTimeOffset timestamp = new(2026, 3, 15, 19, 15, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(timestamp);
        LocalFilesystemJobRepository jobRepository = CreateJobRepository(repositoryRoot.Path, timeProvider);
        StoredJob storedJob = await jobRepository.CreateAsync(CreateRequest(), CancellationToken.None);
        RepositoryPreparationService preparationService = new(
            repositoryRoot.Path,
            new RingmasterRepoConfigLoader(),
            new GitWorktreeManager(new GitCli(new ExternalProcessRunner(timeProvider))),
            jobRepository,
            timeProvider);

        IAgentRunner agentRunner = new CodexAgentRunner(
            new FakeCodexRunner(async (request, cancellationToken) =>
            {
                await File.WriteAllTextAsync(request.EventLogPath, "{\"type\":\"thread.started\"}" + Environment.NewLine, cancellationToken);
                await File.WriteAllTextAsync(request.StderrPath, string.Empty, cancellationToken);

                if (request.Kind == AgentRunKind.Planner)
                {
                    string output = RingmasterJsonSerializer.SerializeCompact(
                        new PlannerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Plan ready.",
                            PlanMarkdown = "# Plan\n\n- Update README.\n",
                            NeedsHuman = false,
                            Questions = [],
                        });
                    await File.WriteAllTextAsync(request.OutputLastMessagePath, output, cancellationToken);
                    return new CodexExecResult
                    {
                        ExitCode = 0,
                        TimedOut = false,
                        SessionId = "planner-session",
                        FinalOutputText = output,
                    };
                }

                string implementerOutput = RingmasterJsonSerializer.SerializeCompact(
                    new ImplementerAgentOutput
                    {
                        Result = "blocked",
                        Summary = "I cannot proceed without more context.",
                        FilesModified = [],
                        RecommendedNextChecks = [],
                        NeedsHuman = true,
                        Questions = ["What is the expected behavior?"],
                    });
                await File.WriteAllTextAsync(request.OutputLastMessagePath, implementerOutput, cancellationToken);

                return new CodexExecResult
                {
                    ExitCode = 0,
                    TimedOut = false,
                    SessionId = "implementer-session",
                    FinalOutputText = implementerOutput,
                };
            }),
            new AtomicFileWriter());

        CodexPromptBuilder promptBuilder = new();
        GitCli gitCli = new(new ExternalProcessRunner(timeProvider));
        ImplementingStageRunner runner = new(agentRunner, promptBuilder, new AtomicFileWriter(), gitCli);

        // Run planner first
        PlanningStageRunner planner = new(
            preparationService,
            agentRunner,
            promptBuilder,
            new AtomicFileWriter());
        StageExecutionContext plannerContext = CreateContext(storedJob, JobStage.PREPARING, StageRole.Planner);
        await planner.RunAsync(plannerContext, CancellationToken.None);

        storedJob = await jobRepository.GetAsync(storedJob.Definition.JobId, CancellationToken.None)
            ?? throw new InvalidOperationException("Job not found.");

        StageExecutionContext implementerContext = CreateContext(storedJob, JobStage.IMPLEMENTING, StageRole.Implementer);
        StageExecutionResult result = await runner.RunAsync(implementerContext, CancellationToken.None);

        Assert.Equal(StageExecutionOutcome.Blocked, result.Outcome);
        Assert.NotNull(result.Blocker);
        Assert.Equal(BlockerReasonCode.HumanReviewRequired, result.Blocker.ReasonCode);
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

    private static JobCreateRequest CreateRequest()
    {
        return new JobCreateRequest
        {
            Title = "Test blocked vs changes",
            Description = "Verify blocked implementer with/without changes.",
            CreatedBy = "tester",
            VerificationProfile = "default",
            BaseBranch = "master",
        };
    }

    private static StageExecutionContext CreateContext(StoredJob job, JobStage stage, StageRole role)
    {
        string runId = stage == JobStage.PREPARING ? "0001-preparing-planner" : "0002-implementing-implementer";
        return new StageExecutionContext
        {
            Job = job,
            Run = new JobRunRecord
            {
                RunId = runId,
                JobId = job.Definition.JobId,
                Stage = stage,
                Role = role,
                Attempt = 1,
                StartedAtUtc = DateTimeOffset.UtcNow,
                Tool = "test",
                Command = [],
            },
            RunDirectoryPath = Path.Combine(job.JobDirectoryPath, "runs", runId),
        };
    }
}
