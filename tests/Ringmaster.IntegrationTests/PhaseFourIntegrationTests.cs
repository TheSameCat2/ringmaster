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

public sealed class PhaseFourIntegrationTests
{
    [Fact]
    public async Task RealStageRunnersUseCodexArtifactsAndReachReadyForPrWithFakeParity()
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
        List<CodexExecRequest> codexRequests = [];
        IAgentRunner agentRunner = new CodexAgentRunner(
            new FakeCodexRunner(async (request, cancellationToken) =>
            {
                codexRequests.Add(request);
                await File.WriteAllTextAsync(request.EventLogPath, "{\"type\":\"thread.started\",\"thread_id\":\"" + request.Kind.ToString().ToLowerInvariant() + "-session\"}" + Environment.NewLine, cancellationToken);
                await File.WriteAllTextAsync(request.StderrPath, string.Empty, cancellationToken);

                if (request.Kind == AgentRunKind.Planner)
                {
                    string output = RingmasterJsonSerializer.SerializeCompact(
                        new PlannerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Planner completed the implementation plan.",
                            PlanMarkdown = "# Plan" + Environment.NewLine + Environment.NewLine + "- Update the tracked README file." + Environment.NewLine,
                            NeedsHuman = false,
                            Questions = [],
                        });
                    await File.WriteAllTextAsync(request.OutputLastMessagePath, output, cancellationToken);

                    return new CodexExecResult
                    {
                        ExitCode = 0,
                        SessionId = "planner-session",
                        FinalOutputText = output,
                    };
                }

                await File.AppendAllTextAsync(
                    System.IO.Path.Combine(request.WorkingDirectory, "README.md"),
                    "Implemented retry handling with Codex." + Environment.NewLine,
                    cancellationToken);
                string implementerOutput = RingmasterJsonSerializer.SerializeCompact(
                    new ImplementerAgentOutput
                    {
                        Result = "completed",
                        Summary = "Implementer updated the tracked README file.",
                        FilesModified = ["README.md"],
                        RecommendedNextChecks = ["Run the default verification profile."],
                        NeedsHuman = false,
                        Questions = [],
                    });
                await File.WriteAllTextAsync(request.OutputLastMessagePath, implementerOutput, cancellationToken);

                return new CodexExecResult
                {
                    ExitCode = 0,
                    SessionId = "implementer-session",
                    FinalOutputText = implementerOutput,
                };
            }),
            new AtomicFileWriter());
        CodexPromptBuilder promptBuilder = new();
        JobEngine engine = new(
            jobRepository,
            new RingmasterStateMachine(),
            [
                new PlanningStageRunner(preparationService, agentRunner, promptBuilder, new AtomicFileWriter()),
                new ImplementingStageRunner(agentRunner, promptBuilder, new AtomicFileWriter(), new GitCli(new ExternalProcessRunner(timeProvider))),
                new VerifyingStageRunner(
                    repositoryRoot.Path,
                    new RingmasterRepoConfigLoader(),
                    new ExternalProcessRunner(timeProvider),
                    new GitCli(new ExternalProcessRunner(timeProvider)),
                    new GitWorktreeManager(new GitCli(new ExternalProcessRunner(timeProvider))),
                    new AtomicFileWriter(),
                    jobRepository,
                    timeProvider,
                    new DeterministicFailureClassifier(),
                    new RepairLoopPolicyEvaluator(new RepairLoopPolicy())),
                new FakeStageRunner(JobStage.REPAIRING, StageRole.Implementer, JobState.VERIFYING, "Repair completed."),
                new FakeStageRunner(JobStage.REVIEWING, StageRole.Reviewer, JobState.READY_FOR_PR, "Reviewer approved."),
            ],
            timeProvider);

        JobStatusSnapshot status = await engine.RunAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.Equal(JobState.READY_FOR_PR, status.State);
        Assert.True(File.Exists(System.IO.Path.Combine(storedJob.JobDirectoryPath, "PLAN.md")));
        Assert.True(File.Exists(System.IO.Path.Combine(storedJob.JobDirectoryPath, "NOTES.md")));
        Assert.Contains("README.md", status.Git?.ChangedFiles ?? [], StringComparer.Ordinal);

        string plannerPrompt = await File.ReadAllTextAsync(System.IO.Path.Combine(storedJob.JobDirectoryPath, "runs", "0001-preparing-planner", "prompt.md"));
        string implementerPrompt = await File.ReadAllTextAsync(System.IO.Path.Combine(storedJob.JobDirectoryPath, "runs", "0002-implementing-implementer", "prompt.md"));

        Assert.Contains("Planner Role", plannerPrompt, StringComparison.Ordinal);
        Assert.Contains("Implementer Role", implementerPrompt, StringComparison.Ordinal);
        Assert.True(File.Exists(System.IO.Path.Combine(storedJob.JobDirectoryPath, "runs", "0001-preparing-planner", "output-schema.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(storedJob.JobDirectoryPath, "runs", "0001-preparing-planner", "final-output.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(storedJob.JobDirectoryPath, "runs", "0001-preparing-planner", "codex.events.jsonl")));
        Assert.True(File.Exists(System.IO.Path.Combine(storedJob.JobDirectoryPath, "runs", "0002-implementing-implementer", "output-schema.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(storedJob.JobDirectoryPath, "runs", "0002-implementing-implementer", "final-output.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(storedJob.JobDirectoryPath, "runs", "0002-implementing-implementer", "codex.events.jsonl")));

        JobRunRecord planningRun = RingmasterJsonSerializer.Deserialize<JobRunRecord>(
            await File.ReadAllTextAsync(System.IO.Path.Combine(storedJob.JobDirectoryPath, "runs", "0001-preparing-planner", "run.json")));
        JobRunRecord implementingRun = RingmasterJsonSerializer.Deserialize<JobRunRecord>(
            await File.ReadAllTextAsync(System.IO.Path.Combine(storedJob.JobDirectoryPath, "runs", "0002-implementing-implementer", "run.json")));

        Assert.Equal("planner-session", planningRun.SessionId);
        Assert.Equal("implementer-session", implementingRun.SessionId);
        Assert.Equal("prompt.md", planningRun.Artifacts.Prompt);
        Assert.Equal("final-output.json", implementingRun.Artifacts.FinalOutput);

        CodexExecRequest plannerRequest = Assert.Single(codexRequests, request => request.Kind == AgentRunKind.Planner);
        CodexExecRequest implementerRequest = Assert.Single(codexRequests, request => request.Kind == AgentRunKind.Implementer);
        Assert.Equal([Path.Combine(storedJob.JobDirectoryPath, "runs", "0001-preparing-planner")], plannerRequest.AdditionalWritableDirectories);
        Assert.Equal([Path.Combine(storedJob.JobDirectoryPath, "runs", "0002-implementing-implementer")], implementerRequest.AdditionalWritableDirectories);
    }

    [Fact]
    public async Task RealCodexSmokeWritesStructuredOutputWhenEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RINGMASTER_RUN_REAL_CODEX_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using TemporaryDirectory temporaryDirectory = new();
        string schemaPath = System.IO.Path.Combine(temporaryDirectory.Path, "output-schema.json");
        string outputPath = System.IO.Path.Combine(temporaryDirectory.Path, "final-output.json");
        string eventLogPath = System.IO.Path.Combine(temporaryDirectory.Path, "codex.events.jsonl");
        string stderrPath = System.IO.Path.Combine(temporaryDirectory.Path, "stderr.log");

        await File.WriteAllTextAsync(
            schemaPath,
            """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "additionalProperties": false,
              "required": ["result", "summary"],
              "properties": {
                "result": { "type": "string" },
                "summary": { "type": "string" }
              }
            }
            """);

        CodexExecRunner runner = new(new ExternalProcessRunner(TimeProvider.System));
        CodexExecResult result = await runner.ExecuteAsync(
            new CodexExecRequest
            {
                Kind = AgentRunKind.Planner,
                WorkingDirectory = temporaryDirectory.Path,
                AdditionalWritableDirectories = [temporaryDirectory.Path],
                PromptText = "Return JSON matching the schema with result 'completed' and a short summary.",
                OutputSchemaPath = schemaPath,
                OutputLastMessagePath = outputPath,
                EventLogPath = eventLogPath,
                StderrPath = stderrPath,
                SandboxMode = AgentSandboxMode.ReadOnly,
                SkipGitRepoCheck = true,
            },
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.FinalOutputText));
        Assert.False(string.IsNullOrWhiteSpace(result.SessionId));
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
            Title = "Add retry handling",
            Description = "Implement bounded retries for retryable failures.",
            CreatedBy = "tester",
            VerificationProfile = "default",
            BaseBranch = "master",
        };
    }
}
