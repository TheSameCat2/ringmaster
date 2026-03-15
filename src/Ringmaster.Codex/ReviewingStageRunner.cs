using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Ringmaster.Infrastructure.Persistence;

namespace Ringmaster.Codex;

public sealed class ReviewingStageRunner(
    IAgentRunner agentRunner,
    CodexPromptBuilder promptBuilder,
    AtomicFileWriter atomicFileWriter,
    PullRequestDraftBuilder pullRequestDraftBuilder) : IStageRunner
{
    public JobStage Stage => JobStage.REVIEWING;
    public StageRole Role => StageRole.Reviewer;

    public StageRunDescriptor DescribeRun(StoredJob job)
    {
        return new StageRunDescriptor
        {
            Tool = "codex",
            Command = ["codex", "exec", "reviewer"],
        };
    }

    public async Task<StageExecutionResult> RunAsync(StageExecutionContext context, CancellationToken cancellationToken)
    {
        string worktreePath = context.Job.Status.Git?.WorktreePath
            ?? throw new InvalidOperationException("Reviewer run requires a prepared worktree.");
        AgentPromptDefinition prompt = promptBuilder.BuildReviewerPrompt(context);
        AgentExecutionResult agentResult = await agentRunner.RunAsync(
            new AgentExecutionRequest
            {
                Kind = AgentRunKind.Reviewer,
                WorkingDirectory = worktreePath,
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
                $"Reviewer Codex run failed with exit code {agentResult.ExitCode}.",
                artifacts: agentResult.Artifacts,
                sessionId: agentResult.SessionId,
                exitCode: agentResult.ExitCode);
        }

        ReviewerAgentOutput output = DeserializeOutput<ReviewerAgentOutput>(agentResult.FinalOutputText);
        ReviewRisk? risk = ParseRisk(output.Risk);
        ReviewVerdict verdict = ParseVerdict(output.Verdict, output.NeedsHuman);
        await WriteReviewAsync(context.Job.JobDirectoryPath, output, risk, cancellationToken);

        if (verdict == ReviewVerdict.HumanReviewRequired)
        {
            return StageExecutionResult.Blocked(
                new BlockerInfo
                {
                    ReasonCode = BlockerReasonCode.HumanReviewRequired,
                    Summary = output.BlockerSummary ?? output.Summary,
                    Questions = output.Questions.Count > 0
                        ? output.Questions
                        : ["Inspect REVIEW.md and decide whether the job should be repaired, approved, or manually completed."],
                    ResumeState = JobState.REVIEWING,
                },
                output.BlockerSummary ?? output.Summary,
                artifacts: agentResult.Artifacts,
                reviewVerdict: verdict,
                reviewRisk: risk,
                sessionId: agentResult.SessionId,
                exitCode: agentResult.ExitCode);
        }

        if (verdict == ReviewVerdict.RequestRepair)
        {
            return StageExecutionResult.Succeeded(
                JobState.REPAIRING,
                output.Summary,
                artifacts: agentResult.Artifacts,
                reviewVerdict: verdict,
                reviewRisk: risk,
                sessionId: agentResult.SessionId,
                exitCode: agentResult.ExitCode);
        }

        await pullRequestDraftBuilder.WriteAsync(context.Job, output, risk, cancellationToken);

        return StageExecutionResult.Succeeded(
            JobState.READY_FOR_PR,
            output.Summary,
            artifacts: agentResult.Artifacts,
            reviewVerdict: verdict,
            reviewRisk: risk,
            sessionId: agentResult.SessionId,
            exitCode: agentResult.ExitCode);
    }

    private async Task WriteReviewAsync(
        string jobDirectoryPath,
        ReviewerAgentOutput output,
        ReviewRisk? risk,
        CancellationToken cancellationToken)
    {
        List<string> lines =
        [
            "# Review",
            string.Empty,
            output.Summary,
            string.Empty,
            $"Verdict: {output.Verdict}",
        ];

        if (risk is not null)
        {
            lines.Add($"Risk: {risk}");
        }

        if (output.Findings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Findings");
            lines.AddRange(output.Findings.Select(finding => $"- {finding.Severity}: {finding.Message}"));
        }

        if (output.RequiredRepairs.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Required Repairs");
            lines.AddRange(output.RequiredRepairs.Select(repair => $"- {repair}"));
        }

        await atomicFileWriter.WriteTextAsync(
            Path.Combine(jobDirectoryPath, "REVIEW.md"),
            string.Join(Environment.NewLine, lines) + Environment.NewLine,
            cancellationToken);
        await atomicFileWriter.WriteJsonAsync(
            Path.Combine(jobDirectoryPath, "artifacts", "review-summary.json"),
            output,
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

    private static ReviewVerdict ParseVerdict(string? value, bool needsHuman)
    {
        if (needsHuman)
        {
            return ReviewVerdict.HumanReviewRequired;
        }

        return value?.Trim().ToLowerInvariant() switch
        {
            "approve" or "approved" => ReviewVerdict.Approved,
            "request_repair" => ReviewVerdict.RequestRepair,
            "human_review_required" => ReviewVerdict.HumanReviewRequired,
            _ => ReviewVerdict.HumanReviewRequired,
        };
    }

    private static ReviewRisk? ParseRisk(string? value)
    {
        return Enum.TryParse<ReviewRisk>(value, ignoreCase: true, out ReviewRisk risk) ? risk : null;
    }
}
