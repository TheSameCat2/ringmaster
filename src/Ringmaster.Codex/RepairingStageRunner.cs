using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Ringmaster.Git;
using Ringmaster.Infrastructure.Persistence;

namespace Ringmaster.Codex;

public sealed class RepairingStageRunner(
    IAgentRunner agentRunner,
    CodexPromptBuilder promptBuilder,
    AtomicFileWriter atomicFileWriter,
    RepairLoopPolicy repairLoopPolicy,
    GitCli gitCli) : IStageRunner
{
    public JobStage Stage => JobStage.REPAIRING;
    public StageRole Role => StageRole.Implementer;

    public StageRunDescriptor DescribeRun(StoredJob job)
    {
        return new StageRunDescriptor
        {
            Tool = "codex",
            Command = ["codex", "exec", "repairer"],
        };
    }

    public async Task<StageExecutionResult> RunAsync(StageExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Job.Status.Attempts.Repairing > repairLoopPolicy.MaxRepairAttempts)
        {
            return StageExecutionResult.Blocked(
                new BlockerInfo
                {
                    ReasonCode = BlockerReasonCode.RepeatedFailureSignature,
                    Summary = $"Repair budget exhausted after {repairLoopPolicy.MaxRepairAttempts} attempts.",
                    Questions =
                    [
                        "Inspect the latest failure summary and decide whether to continue the repair loop manually.",
                    ],
                    ResumeState = JobState.REPAIRING,
                },
                $"Repair budget exhausted after {repairLoopPolicy.MaxRepairAttempts} attempts.",
                failureCategory: FailureCategory.MaxAttemptsExceeded,
                failureSignature: "repair:max-attempts");
        }

        string worktreePath = context.Job.Status.Git?.WorktreePath
            ?? throw new InvalidOperationException("Repair run requires a prepared worktree.");
        AgentPromptDefinition prompt = promptBuilder.BuildRepairPrompt(context);

        AgentExecutionResult agentResult = await ImplementingStageRunner.RunWithRetryAsync(
            agentRunner,
            new AgentExecutionRequest
            {
                Kind = AgentRunKind.Repairer,
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
                $"Repair Codex run failed after retries (exit code {agentResult.ExitCode}, timed out: {agentResult.TimedOut}).",
                artifacts: agentResult.Artifacts,
                sessionId: agentResult.SessionId,
                exitCode: agentResult.ExitCode);
        }

        if (string.IsNullOrWhiteSpace(agentResult.FinalOutputText))
        {
            return StageExecutionResult.Failed(
                FailureCategory.AgentProtocolFailure,
                "Repair Codex run produced no structured output after retries.",
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
                    $"Repairer reported blocked but made changes to {statusInfo.ChangedFiles.Count} file(s). Proceeding to verification. Original summary: {output.Summary}",
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

        GitStatusInfo repairStatusInfo = await gitCli.CaptureStatusAsync(worktreePath, cancellationToken);
        if (!repairStatusInfo.HasUncommittedChanges)
        {
            return StageExecutionResult.Blocked(
                new BlockerInfo
                {
                    ReasonCode = BlockerReasonCode.RepeatedFailureSignature,
                    Summary = "Repairer completed but produced no filesystem changes. No progress was made.",
                    Questions =
                    [
                        "Review the failure summary and decide whether the repair plan needs to change.",
                    ],
                    ResumeState = JobState.REPAIRING,
                },
                "Repairer completed but produced no filesystem changes.",
                artifacts: agentResult.Artifacts,
                failureCategory: FailureCategory.AgentProtocolFailure,
                failureSignature: "repair:no-progress",
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
            ResumeState = JobState.REPAIRING,
        };
    }

    private static BlockerReasonCode ParseReasonCode(string? value)
    {
        return Enum.TryParse<BlockerReasonCode>(value, ignoreCase: true, out BlockerReasonCode reasonCode)
            ? reasonCode
            : BlockerReasonCode.HumanReviewRequired;
    }
}
