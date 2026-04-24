using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Ringmaster.Git;
using Ringmaster.Infrastructure.Persistence;

namespace Ringmaster.Codex;

public sealed class ImplementingStageRunner(
    IAgentRunner agentRunner,
    CodexPromptBuilder promptBuilder,
    AtomicFileWriter atomicFileWriter,
    GitCli gitCli) : IStageRunner
{
    public JobStage Stage => JobStage.IMPLEMENTING;
    public StageRole Role => StageRole.Implementer;

    public StageRunDescriptor DescribeRun(StoredJob job)
    {
        return new StageRunDescriptor
        {
            Tool = "codex",
            Command = ["codex", "exec", "implementer"],
        };
    }

    public async Task<StageExecutionResult> RunAsync(StageExecutionContext context, CancellationToken cancellationToken)
    {
        string worktreePath = context.Job.Status.Git?.WorktreePath
            ?? throw new InvalidOperationException("Implementer run requires a prepared worktree.");
        AgentPromptDefinition prompt = promptBuilder.BuildImplementerPrompt(context);

        AgentExecutionResult agentResult = await RunWithRetryAsync(
            agentRunner,
            new AgentExecutionRequest
            {
                Kind = AgentRunKind.Implementer,
                WorkingDirectory = worktreePath,
                AdditionalWritableDirectories = [context.RunDirectoryPath],
                RunDirectoryPath = context.RunDirectoryPath,
                PromptText = prompt.PromptText,
                OutputSchemaJson = prompt.OutputSchemaJson,
                SandboxMode = AgentSandboxMode.WorkspaceWrite,
            },
            context.RunDirectoryPath,
            atomicFileWriter,
            cancellationToken);

        if (agentResult.ExitCode != 0 || agentResult.TimedOut)
        {
            return StageExecutionResult.Failed(
                FailureCategory.ToolFailure,
                $"Implementer Codex run failed after retries (exit code {agentResult.ExitCode}, timed out: {agentResult.TimedOut}).",
                artifacts: agentResult.Artifacts,
                sessionId: agentResult.SessionId,
                exitCode: agentResult.ExitCode);
        }

        if (string.IsNullOrWhiteSpace(agentResult.FinalOutputText))
        {
            return StageExecutionResult.Failed(
                FailureCategory.AgentProtocolFailure,
                "Implementer Codex run produced no structured output after retries.",
                artifacts: agentResult.Artifacts,
                sessionId: agentResult.SessionId,
                exitCode: agentResult.ExitCode);
        }

        ImplementerAgentOutput output = DeserializeOutput<ImplementerAgentOutput>(agentResult.FinalOutputText);
        await WriteNotesAsync(context.Job.JobDirectoryPath, output, cancellationToken);

        if (IsBlocked(output))
        {
            GitStatusInfo statusInfo = await gitCli.CaptureStatusAsync(worktreePath, cancellationToken);
            if (statusInfo.HasUncommittedChanges)
            {
                return StageExecutionResult.Succeeded(
                    JobState.VERIFYING,
                    $"Implementer reported blocked but made changes to {statusInfo.ChangedFiles.Count} file(s). Proceeding to verification. Original summary: {output.Summary}",
                    artifacts: agentResult.Artifacts,
                    sessionId: agentResult.SessionId,
                    exitCode: agentResult.ExitCode);
            }

            return StageExecutionResult.Blocked(
                CreateBlocker(output),
                output.BlockerSummary ?? output.Summary,
                artifacts: agentResult.Artifacts,
                sessionId: agentResult.SessionId,
                exitCode: agentResult.ExitCode);
        }

        return StageExecutionResult.Succeeded(
            JobState.VERIFYING,
            output.Summary,
            artifacts: agentResult.Artifacts,
            sessionId: agentResult.SessionId,
            exitCode: agentResult.ExitCode);
    }

    private async Task WriteNotesAsync(string jobDirectoryPath, ImplementerAgentOutput output, CancellationToken cancellationToken)
    {
        List<string> lines =
        [
            "# Notes",
            string.Empty,
            output.Summary,
        ];

        if (output.FilesModified.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Files Modified");
            lines.AddRange(output.FilesModified.Select(file => $"- {file}"));
        }

        if (output.RecommendedNextChecks.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Recommended Next Checks");
            lines.AddRange(output.RecommendedNextChecks.Select(check => $"- {check}"));
        }

        await atomicFileWriter.WriteTextAsync(
            Path.Combine(jobDirectoryPath, "NOTES.md"),
            string.Join(Environment.NewLine, lines) + Environment.NewLine,
            cancellationToken);
    }

    private static T DeserializeOutput<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Codex did not produce a final structured output.");
        }

        return RingmasterJsonSerializer.Deserialize<T>(json);
    }

    private static bool IsBlocked(ImplementerAgentOutput output)
    {
        return output.NeedsHuman || string.Equals(output.Result, "blocked", StringComparison.OrdinalIgnoreCase);
    }

    private static BlockerInfo CreateBlocker(ImplementerAgentOutput output)
    {
        return new BlockerInfo
        {
            ReasonCode = ParseReasonCode(output.BlockerReasonCode),
            Summary = output.BlockerSummary ?? output.Summary,
            Questions = output.Questions,
            ResumeState = JobState.IMPLEMENTING,
        };
    }

    private static BlockerReasonCode ParseReasonCode(string? value)
    {
        return Enum.TryParse<BlockerReasonCode>(value, ignoreCase: true, out BlockerReasonCode reasonCode)
            ? reasonCode
            : BlockerReasonCode.HumanReviewRequired;
    }

    internal static async Task<AgentExecutionResult> RunWithRetryAsync(
        IAgentRunner agentRunner,
        AgentExecutionRequest request,
        string runDirectoryPath,
        AtomicFileWriter atomicFileWriter,
        CancellationToken cancellationToken)
    {
        const int MaxRetries = 2;
        AgentExecutionResult? lastResult = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            AgentExecutionResult agentResult = await agentRunner.RunAsync(request, cancellationToken);
            lastResult = agentResult;

            bool isRecoverable = agentResult.ExitCode != 0
                || agentResult.TimedOut
                || string.IsNullOrWhiteSpace(agentResult.FinalOutputText);

            if (!isRecoverable)
            {
                return agentResult;
            }

            if (attempt < MaxRetries)
            {
                await atomicFileWriter.WriteTextAsync(
                    Path.Combine(runDirectoryPath, $"implementer-retry-{attempt + 1}.log"),
                    $"Attempt {attempt + 1} failed: exitCode={agentResult.ExitCode}, timedOut={agentResult.TimedOut}, hasOutput={!string.IsNullOrWhiteSpace(agentResult.FinalOutputText)}",
                    cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
        }

        return lastResult!;
    }
}
