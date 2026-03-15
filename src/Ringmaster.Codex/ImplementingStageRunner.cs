using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Ringmaster.Infrastructure.Persistence;

namespace Ringmaster.Codex;

public sealed class ImplementingStageRunner(
    IAgentRunner agentRunner,
    CodexPromptBuilder promptBuilder,
    AtomicFileWriter atomicFileWriter) : IStageRunner
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
        AgentExecutionResult agentResult = await agentRunner.RunAsync(
            new AgentExecutionRequest
            {
                Kind = AgentRunKind.Implementer,
                WorkingDirectory = worktreePath,
                AdditionalWritableDirectories = [context.Job.JobDirectoryPath],
                RunDirectoryPath = context.RunDirectoryPath,
                PromptText = prompt.PromptText,
                OutputSchemaJson = prompt.OutputSchemaJson,
                SandboxMode = AgentSandboxMode.WorkspaceWrite,
            },
            cancellationToken);

        if (agentResult.ExitCode != 0)
        {
            return StageExecutionResult.Failed(
                FailureCategory.ToolFailure,
                $"Implementer Codex run failed with exit code {agentResult.ExitCode}.",
                agentResult.Artifacts,
                agentResult.SessionId,
                agentResult.ExitCode);
        }

        ImplementerAgentOutput output = DeserializeOutput<ImplementerAgentOutput>(agentResult.FinalOutputText);
        await WriteNotesAsync(context.Job.JobDirectoryPath, output, cancellationToken);

        if (IsBlocked(output))
        {
            return StageExecutionResult.Blocked(
                CreateBlocker(output),
                output.BlockerSummary ?? output.Summary,
                agentResult.Artifacts,
                agentResult.SessionId,
                agentResult.ExitCode);
        }

        return StageExecutionResult.Succeeded(
            JobState.VERIFYING,
            output.Summary,
            agentResult.Artifacts,
            agentResult.SessionId,
            agentResult.ExitCode);
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
}
