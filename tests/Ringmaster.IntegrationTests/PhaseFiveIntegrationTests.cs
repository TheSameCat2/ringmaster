using Ringmaster.Codex;
using Ringmaster.Core.Configuration;
using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Ringmaster.Git;
using Ringmaster.Infrastructure.Configuration;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.Infrastructure.Processes;
using Ringmaster.IntegrationTests.Testing;

namespace Ringmaster.IntegrationTests;

[Collection(nameof(UnsafeVerificationCommandCollection))]
public sealed class PhaseFiveIntegrationTests
{

    [Fact]
    public async Task CompileFailureTransitionsThroughRepairAndReachesReadyForPr()
    {
        using UnsafeVerificationCommandOverrideScope _ = new();
        using TemporaryGitRepository repositoryRoot = new();
        await repositoryRoot.InitializeAsync();
        await PrepareScriptedVerificationRepoAsync(repositoryRoot);

        DateTimeOffset timestamp = new(2026, 3, 15, 20, 0, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(timestamp);
        LocalFilesystemJobRepository jobRepository = CreateJobRepository(repositoryRoot.Path, timeProvider);
        StoredJob storedJob = await jobRepository.CreateAsync(CreateRequest(), CancellationToken.None);

        int implementerCalls = 0;
        int reviewerCalls = 0;
        JobEngine engine = CreatePhaseFiveEngine(
            repositoryRoot.Path,
            jobRepository,
            timeProvider,
            async (request, cancellationToken) =>
            {
                string runId = Path.GetFileName(Path.GetDirectoryName(request.OutputLastMessagePath) ?? string.Empty);

                return request.Kind switch
                {
                    AgentRunKind.Planner => await WriteCodexResultAsync(
                        request,
                        new PlannerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Planner completed the implementation plan.",
                            PlanMarkdown = "# Plan" + Environment.NewLine + Environment.NewLine + "- Implement the requested change." + Environment.NewLine,
                            NeedsHuman = false,
                            Questions = [],
                        },
                        "planner-session",
                        cancellationToken),
                    AgentRunKind.Reviewer => await WriteCodexResultAsync(
                        request,
                        new ReviewerAgentOutput
                        {
                            Verdict = ++reviewerCalls == 1 ? "approve" : "approve",
                            Risk = "low",
                            Summary = "The repaired change is ready for PR.",
                            Findings = [],
                            RequiredRepairs = [],
                            RecommendedPrMode = "ready",
                            NeedsHuman = false,
                        },
                        "reviewer-session",
                        cancellationToken),
                    _ when runId.Contains("0002-implementing", StringComparison.Ordinal) => await WriteImplementerResultAsync(
                        request,
                        async () =>
                        {
                            implementerCalls++;
                            await File.WriteAllTextAsync(
                                Path.Combine(request.WorkingDirectory, "src.txt"),
                                "BROKEN_COMPILE" + Environment.NewLine,
                                cancellationToken);
                        },
                        new ImplementerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Initial implementation introduced a compile failure.",
                            FilesModified = ["src.txt"],
                            RecommendedNextChecks = ["Run the default verification profile."],
                            NeedsHuman = false,
                            Questions = [],
                        },
                        "implementer-session",
                        cancellationToken),
                    _ => await WriteImplementerResultAsync(
                        request,
                        async () =>
                        {
                            await File.WriteAllTextAsync(
                                Path.Combine(request.WorkingDirectory, "src.txt"),
                                "FIXED_COMPILE" + Environment.NewLine,
                                cancellationToken);
                        },
                        new ImplementerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Repair fixed the compile failure.",
                            FilesModified = ["src.txt"],
                            RecommendedNextChecks = ["Run the default verification profile again."],
                            NeedsHuman = false,
                            Questions = [],
                        },
                        "repair-session",
                        cancellationToken),
                };
            });

        JobStatusSnapshot status = await engine.RunAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.Equal(JobState.READY_FOR_PR, status.State);
        Assert.Equal(1, status.Attempts.Implementing);
        Assert.Equal(1, status.Attempts.Repairing);
        Assert.Equal(2, status.Attempts.Verifying);
        Assert.Equal(1, status.Attempts.Reviewing);
        Assert.Equal(ReviewVerdict.Approved, status.Review.Verdict);
        Assert.Equal("verify:compile:CS0103:Program.cs", status.LastFailure?.Signature);
        Assert.True(File.Exists(Path.Combine(storedJob.JobDirectoryPath, "artifacts", "repair-summary.json")));
        Assert.True(File.Exists(Path.Combine(storedJob.JobDirectoryPath, "REVIEW.md")));
        string pullRequestDraftPath = Path.Combine(storedJob.JobDirectoryPath, "PR.md");
        Assert.True(File.Exists(pullRequestDraftPath));
        string pullRequestDraft = await File.ReadAllTextAsync(pullRequestDraftPath);
        Assert.DoesNotContain("verify-compile", pullRequestDraft, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verify-tests", pullRequestDraft, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, implementerCalls);
    }

    [Fact]
    public async Task TestFailureTransitionsThroughRepairAndReachesReadyForPr()
    {
        using UnsafeVerificationCommandOverrideScope _ = new();
        using TemporaryGitRepository repositoryRoot = new();
        await repositoryRoot.InitializeAsync();
        await PrepareScriptedVerificationRepoAsync(repositoryRoot);

        DateTimeOffset timestamp = new(2026, 3, 15, 20, 5, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(timestamp);
        LocalFilesystemJobRepository jobRepository = CreateJobRepository(repositoryRoot.Path, timeProvider);
        StoredJob storedJob = await jobRepository.CreateAsync(CreateRequest(), CancellationToken.None);

        JobEngine engine = CreatePhaseFiveEngine(
            repositoryRoot.Path,
            jobRepository,
            timeProvider,
            async (request, cancellationToken) =>
            {
                string runId = Path.GetFileName(Path.GetDirectoryName(request.OutputLastMessagePath) ?? string.Empty);

                return request.Kind switch
                {
                    AgentRunKind.Planner => await WriteCodexResultAsync(
                        request,
                        new PlannerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Planner completed the implementation plan.",
                            PlanMarkdown = "# Plan" + Environment.NewLine + Environment.NewLine + "- Update retry behavior." + Environment.NewLine,
                            NeedsHuman = false,
                            Questions = [],
                        },
                        "planner-session",
                        cancellationToken),
                    AgentRunKind.Reviewer => await WriteCodexResultAsync(
                        request,
                        new ReviewerAgentOutput
                        {
                            Verdict = "approve",
                            Risk = "low",
                            Summary = "The repaired test behavior is ready for PR.",
                            Findings = [],
                            RequiredRepairs = [],
                            RecommendedPrMode = "ready",
                            NeedsHuman = false,
                        },
                        "reviewer-session",
                        cancellationToken),
                    _ when runId.Contains("0002-implementing", StringComparison.Ordinal) => await WriteImplementerResultAsync(
                        request,
                        async () =>
                        {
                            await File.WriteAllTextAsync(
                                Path.Combine(request.WorkingDirectory, "tests.txt"),
                                "FAIL_TEST" + Environment.NewLine,
                                cancellationToken);
                        },
                        new ImplementerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Initial implementation introduced a test failure.",
                            FilesModified = ["tests.txt"],
                            RecommendedNextChecks = ["Run the default verification profile."],
                            NeedsHuman = false,
                            Questions = [],
                        },
                        "implementer-session",
                        cancellationToken),
                    _ => await WriteImplementerResultAsync(
                        request,
                        async () =>
                        {
                            await File.WriteAllTextAsync(
                                Path.Combine(request.WorkingDirectory, "tests.txt"),
                                "FIXED_TEST" + Environment.NewLine,
                                cancellationToken);
                        },
                        new ImplementerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Repair fixed the failing test.",
                            FilesModified = ["tests.txt"],
                            RecommendedNextChecks = ["Run the default verification profile again."],
                            NeedsHuman = false,
                            Questions = [],
                        },
                        "repair-session",
                        cancellationToken),
                };
            });

        JobStatusSnapshot status = await engine.RunAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.Equal(JobState.READY_FOR_PR, status.State);
        Assert.Equal("verify:tests:Ringmaster.Tests.RetryTests.Should_retry_on_429", status.LastFailure?.Signature);
        Assert.Equal(1, status.Attempts.Repairing);
        Assert.Equal(2, status.Attempts.Verifying);
        Assert.True(File.Exists(Path.Combine(storedJob.JobDirectoryPath, "artifacts", "repair-summary.json")));
    }

    [Fact]
    public async Task RepeatedSameFailureSignatureBlocksTheJob()
    {
        using UnsafeVerificationCommandOverrideScope _ = new();
        using TemporaryGitRepository repositoryRoot = new();
        await repositoryRoot.InitializeAsync();
        await PrepareScriptedVerificationRepoAsync(repositoryRoot);

        DateTimeOffset timestamp = new(2026, 3, 15, 20, 10, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(timestamp);
        LocalFilesystemJobRepository jobRepository = CreateJobRepository(repositoryRoot.Path, timeProvider);
        StoredJob storedJob = await jobRepository.CreateAsync(CreateRequest(), CancellationToken.None);

        JobEngine engine = CreatePhaseFiveEngine(
            repositoryRoot.Path,
            jobRepository,
            timeProvider,
            async (request, cancellationToken) =>
            {
                string runId = Path.GetFileName(Path.GetDirectoryName(request.OutputLastMessagePath) ?? string.Empty);

                return request.Kind switch
                {
                    AgentRunKind.Planner => await WriteCodexResultAsync(
                        request,
                        new PlannerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Planner completed the implementation plan.",
                            PlanMarkdown = "# Plan" + Environment.NewLine + Environment.NewLine + "- Try the code change." + Environment.NewLine,
                            NeedsHuman = false,
                            Questions = [],
                        },
                        "planner-session",
                        cancellationToken),
                    AgentRunKind.Reviewer => throw new InvalidOperationException("Reviewer should not run after repeated failure blocking."),
                    _ when runId.Contains("0002-implementing", StringComparison.Ordinal) => await WriteImplementerResultAsync(
                        request,
                        async () =>
                        {
                            await File.WriteAllTextAsync(
                                Path.Combine(request.WorkingDirectory, "src.txt"),
                                "BROKEN_COMPILE" + Environment.NewLine,
                                cancellationToken);
                        },
                        new ImplementerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Initial implementation introduced a compile failure.",
                            FilesModified = ["src.txt"],
                            RecommendedNextChecks = ["Run the default verification profile."],
                            NeedsHuman = false,
                            Questions = [],
                        },
                        "implementer-session",
                        cancellationToken),
                    _ => await WriteImplementerResultAsync(
                        request,
                        async () =>
                        {
                            await File.WriteAllTextAsync(
                                Path.Combine(request.WorkingDirectory, "src.txt"),
                                "BROKEN_COMPILE" + Environment.NewLine,
                                cancellationToken);
                        },
                        new ImplementerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Repair attempt did not change the compile failure.",
                            FilesModified = ["src.txt"],
                            RecommendedNextChecks = ["Run the default verification profile again."],
                            NeedsHuman = false,
                            Questions = [],
                        },
                        "repair-session",
                        cancellationToken),
                };
            });

        JobStatusSnapshot status = await engine.RunAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.Equal(JobState.BLOCKED, status.State);
        Assert.Equal(JobState.REPAIRING, status.ResumeState);
        Assert.Equal(BlockerReasonCode.RepeatedFailureSignature, status.Blocker?.ReasonCode);
        Assert.Equal("verify:compile:CS0103:Program.cs", status.LastFailure?.Signature);
        Assert.Equal(2, status.LastFailure?.RepetitionCount);
        Assert.Equal(1, status.Attempts.Repairing);
        Assert.Equal(2, status.Attempts.Verifying);
    }

    [Fact]
    public async Task ReviewerCanRequestRepairBeforeApproving()
    {
        using UnsafeVerificationCommandOverrideScope _ = new();
        using TemporaryGitRepository repositoryRoot = new();
        await repositoryRoot.InitializeAsync();
        await PrepareScriptedVerificationRepoAsync(repositoryRoot);

        DateTimeOffset timestamp = new(2026, 3, 15, 20, 15, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(timestamp);
        LocalFilesystemJobRepository jobRepository = CreateJobRepository(repositoryRoot.Path, timeProvider);
        StoredJob storedJob = await jobRepository.CreateAsync(CreateRequest(), CancellationToken.None);

        int reviewerCalls = 0;
        JobEngine engine = CreatePhaseFiveEngine(
            repositoryRoot.Path,
            jobRepository,
            timeProvider,
            async (request, cancellationToken) =>
            {
                string runId = Path.GetFileName(Path.GetDirectoryName(request.OutputLastMessagePath) ?? string.Empty);

                return request.Kind switch
                {
                    AgentRunKind.Planner => await WriteCodexResultAsync(
                        request,
                        new PlannerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Planner completed the implementation plan.",
                            PlanMarkdown = "# Plan" + Environment.NewLine + Environment.NewLine + "- Update the tracked files." + Environment.NewLine,
                            NeedsHuman = false,
                            Questions = [],
                        },
                        "planner-session",
                        cancellationToken),
                    AgentRunKind.Reviewer when reviewerCalls++ == 0 => await WriteCodexResultAsync(
                        request,
                        new ReviewerAgentOutput
                        {
                            Verdict = "request_repair",
                            Risk = "medium",
                            Summary = "Implementation is close, but one follow-up repair is required.",
                            Findings =
                            [
                                new ReviewerFinding
                                {
                                    Severity = "medium",
                                    Message = "Add the final tracked marker before approval.",
                                },
                            ],
                            RequiredRepairs = ["Add REVIEW_FIXED to src.txt."],
                            RecommendedPrMode = "draft",
                            NeedsHuman = false,
                        },
                        "reviewer-session-1",
                        cancellationToken),
                    AgentRunKind.Reviewer => await WriteCodexResultAsync(
                        request,
                        new ReviewerAgentOutput
                        {
                            Verdict = "approve",
                            Risk = "low",
                            Summary = "The repaired change is now ready for PR.",
                            Findings = [],
                            RequiredRepairs = [],
                            RecommendedPrMode = "ready",
                            NeedsHuman = false,
                        },
                        "reviewer-session-2",
                        cancellationToken),
                    _ when runId.Contains("0002-implementing", StringComparison.Ordinal) => await WriteImplementerResultAsync(
                        request,
                        async () =>
                        {
                            await File.WriteAllTextAsync(
                                Path.Combine(request.WorkingDirectory, "src.txt"),
                                "INITIAL_CHANGE" + Environment.NewLine,
                                cancellationToken);
                        },
                        new ImplementerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Initial implementation completed successfully.",
                            FilesModified = ["src.txt"],
                            RecommendedNextChecks = ["Run the default verification profile."],
                            NeedsHuman = false,
                            Questions = [],
                        },
                        "implementer-session",
                        cancellationToken),
                    _ => await WriteImplementerResultAsync(
                        request,
                        async () =>
                        {
                            await File.WriteAllTextAsync(
                                Path.Combine(request.WorkingDirectory, "src.txt"),
                                "REVIEW_FIXED" + Environment.NewLine,
                                cancellationToken);
                        },
                        new ImplementerAgentOutput
                        {
                            Result = "completed",
                            Summary = "Repair addressed the reviewer request.",
                            FilesModified = ["src.txt"],
                            RecommendedNextChecks = ["Run the default verification profile again."],
                            NeedsHuman = false,
                            Questions = [],
                        },
                        "repair-session",
                        cancellationToken),
                };
            });

        JobStatusSnapshot status = await engine.RunAsync(storedJob.Definition.JobId, CancellationToken.None);
        StoredJob reloaded = await jobRepository.GetAsync(storedJob.Definition.JobId, CancellationToken.None)
            ?? throw new InvalidOperationException("The job was not found after execution.");

        Assert.Equal(JobState.READY_FOR_PR, status.State);
        Assert.Equal(2, status.Attempts.Reviewing);
        Assert.Equal(1, status.Attempts.Repairing);
        Assert.Equal(ReviewVerdict.Approved, status.Review.Verdict);
        Assert.Contains(reloaded.Events, jobEvent => jobEvent.Type == JobEventType.ReviewRecorded && jobEvent.ReviewVerdict == ReviewVerdict.RequestRepair);
        Assert.Contains(reloaded.Events, jobEvent => jobEvent.Type == JobEventType.ReviewRecorded && jobEvent.ReviewVerdict == ReviewVerdict.Approved);
        Assert.True(File.Exists(Path.Combine(storedJob.JobDirectoryPath, "PR.md")));
    }

    private static JobEngine CreatePhaseFiveEngine(
        string repositoryRoot,
        IJobRepository jobRepository,
        TimeProvider timeProvider,
        Func<CodexExecRequest, CancellationToken, Task<CodexExecResult>> codexHandler)
    {
        AtomicFileWriter atomicFileWriter = new();
        RingmasterRepoConfigLoader repoConfigLoader = new();
        ExternalProcessRunner processRunner = new(timeProvider);
        GitCli gitCli = new(processRunner);
        GitWorktreeManager worktreeManager = new(gitCli);
        IAgentRunner agentRunner = new CodexAgentRunner(new FakeCodexRunner(codexHandler), atomicFileWriter);
        CodexPromptBuilder promptBuilder = new();
        RepairLoopPolicy repairLoopPolicy = new();

        return new JobEngine(
            jobRepository,
            new RingmasterStateMachine(),
            [
                new PlanningStageRunner(
                    new RepositoryPreparationService(repositoryRoot, repoConfigLoader, worktreeManager, jobRepository, timeProvider),
                    agentRunner,
                    promptBuilder,
                    atomicFileWriter),
                new ImplementingStageRunner(agentRunner, promptBuilder, atomicFileWriter),
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
                    new RepairLoopPolicyEvaluator(repairLoopPolicy)),
                new RepairingStageRunner(agentRunner, promptBuilder, atomicFileWriter, repairLoopPolicy),
                new ReviewingStageRunner(agentRunner, promptBuilder, atomicFileWriter, new PullRequestDraftBuilder(atomicFileWriter)),
            ],
            timeProvider);
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

    private static async Task PrepareScriptedVerificationRepoAsync(TemporaryGitRepository repositoryRoot)
    {
        (VerificationCommandDefinition[] commands, string compilePath, string compileScript, string testsPath, string testsScript) = CreateVerificationScripts(repositoryRoot.Path);

        await File.WriteAllTextAsync(
            Path.Combine(repositoryRoot.Path, "ringmaster.json"),
            TemporaryGitRepository.CreateDefaultRepoConfigJson(commands));
        await File.WriteAllTextAsync(Path.Combine(repositoryRoot.Path, "src.txt"), "INITIAL" + Environment.NewLine);
        await File.WriteAllTextAsync(Path.Combine(repositoryRoot.Path, "tests.txt"), "INITIAL" + Environment.NewLine);
        await File.WriteAllTextAsync(compilePath, NormalizeWindowsScript(compileScript));
        await File.WriteAllTextAsync(testsPath, NormalizeWindowsScript(testsScript));

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                compilePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.SetUnixFileMode(
                testsPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        await repositoryRoot.CaptureGitAsync(["add", "."]);
        await repositoryRoot.CaptureGitAsync(["commit", "-m", "Add scripted verification"]);
    }

    private static (VerificationCommandDefinition[] Commands, string CompilePath, string CompileScript, string TestsPath, string TestsScript) CreateVerificationScripts(string repositoryRoot)
    {
        if (OperatingSystem.IsWindows())
        {
            string compilePath = Path.Combine(repositoryRoot, "verify-compile.cmd");
            string testsPath = Path.Combine(repositoryRoot, "verify-tests.cmd");

            return
            (
                [
                    new()
                    {
                        Name = "compile",
                        FileName = "cmd.exe",
                        Arguments = ["/c", "verify-compile.cmd"],
                        TimeoutSeconds = 60,
                    },
                    new()
                    {
                        Name = "tests",
                        FileName = "cmd.exe",
                        Arguments = ["/c", "verify-tests.cmd"],
                        TimeoutSeconds = 60,
                    },
                ],
                compilePath,
                """
                @echo off
                findstr /C:"BROKEN_COMPILE" src.txt >nul
                if errorlevel 1 exit /b 0
                echo src/Program.cs^(12,5^): error CS0103: The name 'missingSymbol' does not exist in the current context
                exit /b 1
                exit /b 0
                """,
                testsPath,
                """
                @echo off
                findstr /C:"FAIL_TEST" tests.txt >nul
                if errorlevel 1 exit /b 0
                echo Failed Ringmaster.Tests.RetryTests.Should_retry_on_429 [1 ms]
                echo   Expected: 2
                echo   Actual:   1
                exit /b 1
                exit /b 0
                """);
        }

        string unixCompilePath = Path.Combine(repositoryRoot, "verify-compile.sh");
        string unixTestsPath = Path.Combine(repositoryRoot, "verify-tests.sh");
        return
        (
            [
                new()
                {
                    Name = "compile",
                    FileName = "sh",
                    Arguments = ["./verify-compile.sh"],
                    TimeoutSeconds = 60,
                },
                new()
                {
                    Name = "tests",
                    FileName = "sh",
                    Arguments = ["./verify-tests.sh"],
                    TimeoutSeconds = 60,
                },
            ],
            unixCompilePath,
            """
            #!/usr/bin/env sh
            set -eu
            if grep -q "BROKEN_COMPILE" src.txt; then
              echo "src/Program.cs(12,5): error CS0103: The name 'missingSymbol' does not exist in the current context"
              exit 1
            fi
            exit 0
            """,
            unixTestsPath,
            """
            #!/usr/bin/env sh
            set -eu
            if grep -q "FAIL_TEST" tests.txt; then
              echo "Failed Ringmaster.Tests.RetryTests.Should_retry_on_429 [1 ms]"
              echo "  Expected: 2"
              echo "  Actual:   1"
              exit 1
            fi
            exit 0
            """);
    }

    private static Task<CodexExecResult> WriteImplementerResultAsync(
        CodexExecRequest request,
        Func<Task> mutation,
        ImplementerAgentOutput output,
        string sessionId,
        CancellationToken cancellationToken)
    {
        return WriteCodexResultAsync(request, output, sessionId, cancellationToken, mutation);
    }

    private static async Task<CodexExecResult> WriteCodexResultAsync(
        CodexExecRequest request,
        object output,
        string sessionId,
        CancellationToken cancellationToken,
        Func<Task>? beforeWrite = null)
    {
        if (beforeWrite is not null)
        {
            await beforeWrite();
        }

        await File.WriteAllTextAsync(
            request.EventLogPath,
            "{\"type\":\"thread.started\",\"thread_id\":\"" + sessionId + "\"}" + Environment.NewLine,
            cancellationToken);
        await File.WriteAllTextAsync(request.StderrPath, string.Empty, cancellationToken);

        string json = output is string text
            ? text
            : RingmasterJsonSerializer.SerializeCompact(output);
        await File.WriteAllTextAsync(request.OutputLastMessagePath, json, cancellationToken);

        return new CodexExecResult
        {
            ExitCode = 0,
            SessionId = sessionId,
            FinalOutputText = json,
        };
    }

    private static string NormalizeWindowsScript(string script)
    {
        if (!OperatingSystem.IsWindows())
        {
            return script;
        }

        return script.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);
    }
}

[CollectionDefinition(nameof(UnsafeVerificationCommandCollection), DisableParallelization = true)]
public sealed class UnsafeVerificationCommandCollection;

internal sealed class UnsafeVerificationCommandOverrideScope : IDisposable
{
    private readonly string? _previousValue = Environment.GetEnvironmentVariable(VerificationCommandSafetyPolicy.UnsafeOverrideEnvironmentVariableName);

    public UnsafeVerificationCommandOverrideScope()
    {
        Environment.SetEnvironmentVariable(VerificationCommandSafetyPolicy.UnsafeOverrideEnvironmentVariableName, "1");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(VerificationCommandSafetyPolicy.UnsafeOverrideEnvironmentVariableName, _previousValue);
    }
}
