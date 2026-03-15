using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Ringmaster.Git;
using Ringmaster.Infrastructure.Persistence;

namespace Ringmaster.Codex;

public sealed class PlanningStageRunner(
    RepositoryPreparationService repositoryPreparationService,
    IAgentRunner agentRunner,
    CodexPromptBuilder promptBuilder,
    AtomicFileWriter atomicFileWriter) : IStageRunner
{
    public JobStage Stage => JobStage.PREPARING;
    public StageRole Role => StageRole.Planner;

    public StageRunDescriptor DescribeRun(StoredJob job)
    {
        return new StageRunDescriptor
        {
            Tool = "codex",
            Command = ["codex", "exec", "planner"],
        };
    }

    public async Task<StageExecutionResult> RunAsync(StageExecutionContext context, CancellationToken cancellationToken)
    {
        RepositoryPreparationResult preparation = await repositoryPreparationService.PrepareAsync(context.Job, cancellationToken);

        if (preparation.Blocker is not null)
        {
            return StageExecutionResult.Blocked(preparation.Blocker, preparation.Summary);
        }

        if (preparation.FailureCategory is not null)
        {
            return StageExecutionResult.Failed(preparation.FailureCategory.Value, preparation.Summary);
        }

        AgentPromptDefinition prompt = promptBuilder.BuildPlannerPrompt(
            context with
            {
                Job = context.Job with
                {
                    Status = context.Job.Status with
                    {
                        Git = preparation.GitSnapshot,
                    },
                },
            });
        AgentExecutionResult agentResult = await agentRunner.RunAsync(
            new AgentExecutionRequest
            {
                Kind = AgentRunKind.Planner,
                WorkingDirectory = preparation.GitSnapshot?.WorktreePath ?? throw new InvalidOperationException("Prepared git snapshot did not include a worktree path."),
                AdditionalWritableDirectories = [context.Job.JobDirectoryPath],
                RunDirectoryPath = context.RunDirectoryPath,
                PromptText = prompt.PromptText,
                OutputSchemaJson = prompt.OutputSchemaJson,
                SandboxMode = AgentSandboxMode.ReadOnly,
            },
            cancellationToken);

        if (agentResult.ExitCode != 0)
        {
            return StageExecutionResult.Failed(
                FailureCategory.ToolFailure,
                $"Planner Codex run failed with exit code {agentResult.ExitCode}.",
                artifacts: agentResult.Artifacts,
                sessionId: agentResult.SessionId,
                exitCode: agentResult.ExitCode);
        }

        PlannerAgentOutput output = DeserializeOutput<PlannerAgentOutput>(agentResult.FinalOutputText);
        await atomicFileWriter.WriteTextAsync(
            Path.Combine(context.Job.JobDirectoryPath, "PLAN.md"),
            string.IsNullOrWhiteSpace(output.PlanMarkdown) ? "# Plan" + Environment.NewLine : output.PlanMarkdown,
            cancellationToken);

        if (IsBlocked(output))
        {
            return StageExecutionResult.Blocked(
                CreateBlocker(output, JobState.PREPARING),
                output.BlockerSummary ?? output.Summary,
                artifacts: agentResult.Artifacts,
                sessionId: agentResult.SessionId,
                exitCode: agentResult.ExitCode);
        }

        return StageExecutionResult.Succeeded(
            JobState.IMPLEMENTING,
            output.Summary,
            artifacts: agentResult.Artifacts,
            sessionId: agentResult.SessionId,
            exitCode: agentResult.ExitCode);
    }

    private static T DeserializeOutput<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Codex did not produce a final structured output.");
        }

        return RingmasterJsonSerializer.Deserialize<T>(json);
    }

    private static bool IsBlocked(PlannerAgentOutput output)
    {
        return output.NeedsHuman || string.Equals(output.Result, "blocked", StringComparison.OrdinalIgnoreCase);
    }

    private static BlockerInfo CreateBlocker(PlannerAgentOutput output, JobState resumeState)
    {
        return new BlockerInfo
        {
            ReasonCode = ParseReasonCode(output.BlockerReasonCode),
            Summary = output.BlockerSummary ?? output.Summary,
            Questions = output.Questions,
            ResumeState = resumeState,
        };
    }

    private static BlockerReasonCode ParseReasonCode(string? value)
    {
        return Enum.TryParse<BlockerReasonCode>(value, ignoreCase: true, out BlockerReasonCode reasonCode)
            ? reasonCode
            : BlockerReasonCode.HumanReviewRequired;
    }
}
