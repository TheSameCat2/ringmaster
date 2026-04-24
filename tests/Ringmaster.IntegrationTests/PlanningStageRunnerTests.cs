using Ringmaster.Codex;
using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Ringmaster.Git;
using Ringmaster.Infrastructure.Configuration;
using Ringmaster.Infrastructure.Fakes;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.Infrastructure.Processes;
using Ringmaster.IntegrationTests.Testing;

namespace Ringmaster.IntegrationTests;

public sealed class PlanningStageRunnerTests
{
    [Fact]
    public async Task PlannerReturnsFailedResult_TransitionsJobToFailed()
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

                string output = RingmasterJsonSerializer.SerializeCompact(
                    new PlannerAgentOutput
                    {
                        Result = "failed",
                        Summary = "The repository uses an unsupported build system.",
                        PlanMarkdown = string.Empty,
                        NeedsHuman = false,
                        Questions = [],
                    });
                await File.WriteAllTextAsync(request.OutputLastMessagePath, output, cancellationToken);

                return new CodexExecResult
                {
                    ExitCode = 0,
                    SessionId = "planner-failed-session",
                    FinalOutputText = output,
                };
            }),
            new AtomicFileWriter());

        CodexPromptBuilder promptBuilder = new();
        PlanningStageRunner runner = new(preparationService, agentRunner, promptBuilder, new AtomicFileWriter());

        JobStatusSnapshot status = storedJob.Status;
        storedJob = await jobRepository.GetAsync(storedJob.Definition.JobId, CancellationToken.None)
            ?? throw new InvalidOperationException("Job not found.");

        StageExecutionContext context = new()
        {
            Job = storedJob with { Status = status with { State = JobState.PREPARING } },
            Run = new JobRunRecord
            {
                RunId = "0001-preparing-planner",
                JobId = storedJob.Definition.JobId,
                Stage = JobStage.PREPARING,
                Role = StageRole.Planner,
                Attempt = 1,
                StartedAtUtc = timestamp,
                Tool = "codex",
                Command = ["codex", "exec", "planner"],
            },
            RunDirectoryPath = Path.Combine(storedJob.JobDirectoryPath, "runs", "0001-preparing-planner"),
        };

        StageExecutionResult result = await runner.RunAsync(context, CancellationToken.None);

        Assert.Equal(StageExecutionOutcome.Failed, result.Outcome);
        Assert.Equal(FailureCategory.AgentProtocolFailure, result.FailureCategory);
        Assert.Equal("The repository uses an unsupported build system.", result.Summary);
    }

    [Fact]
    public async Task Engine_HandlesPlannerFailedOutcome_TransitionsToFailed()
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

                string output = RingmasterJsonSerializer.SerializeCompact(
                    new PlannerAgentOutput
                    {
                        Result = "failed",
                        Summary = "Task description is empty and cannot be planned.",
                        PlanMarkdown = string.Empty,
                        NeedsHuman = false,
                        Questions = [],
                    });
                await File.WriteAllTextAsync(request.OutputLastMessagePath, output, cancellationToken);

                return new CodexExecResult
                {
                    ExitCode = 0,
                    SessionId = "planner-failed-session",
                    FinalOutputText = output,
                };
            }),
            new AtomicFileWriter());

        CodexPromptBuilder promptBuilder = new();
        JobEngine engine = new(
            jobRepository,
            new RingmasterStateMachine(),
            [
                new PlanningStageRunner(preparationService, agentRunner, promptBuilder, new AtomicFileWriter()),
            ],
            timeProvider);

        JobStatusSnapshot status = await engine.RunAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.Equal(JobState.FAILED, status.State);
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
            Title = "Test invalid task handling",
            Description = "Verify planner failed result transitions job to FAILED.",
            CreatedBy = "tester",
            VerificationProfile = "default",
            BaseBranch = "master",
        };
    }
}
