using System.Text;
using Ringmaster.Core.Jobs;

namespace Ringmaster.Codex;

public sealed class CodexPromptBuilder
{
    public AgentPromptDefinition BuildPlannerPrompt(StageExecutionContext context)
    {
        string worktreePath = context.Job.Status.Git?.WorktreePath
            ?? throw new InvalidOperationException("Planner prompt requires a prepared worktree.");
        string prompt = $"""
        # Planner Role

        You are the Ringmaster planner for one software job.

        Workspace paths:
        - Worktree root: `{worktreePath}`
        - Job directory: `{context.Job.JobDirectoryPath}`
        - Writable directory: `{context.RunDirectoryPath}`
        - Forbidden: `.ringmaster/jobs/**/STATUS.json`, git commits, PR creation

        Read this material in order:
        1. `JOB.md`
        2. `STATUS.json`
        3. `PLAN.md`

        Behavior rules:
        - Do not edit code in the worktree.
        - Do not edit `STATUS.json`.
        - Do not commit or open PRs.
        - If you need human input, return a blocked result with questions.

        Completion checklist:
        - Produce a concise actionable plan for the implementer.
        - Highlight any blockers or missing context.
        - Return only data that matches the provided schema.
        """;

        return new AgentPromptDefinition
        {
            PromptText = prompt + Environment.NewLine + context.Job.JobMarkdown,
            OutputSchemaJson = BuildPlannerSchema(),
        };
    }

    public AgentPromptDefinition BuildImplementerPrompt(StageExecutionContext context)
    {
        return BuildImplementerLikePrompt(
            context,
            "# Implementer Role",
            "You are the Ringmaster implementer for one software job.",
            "Read this material in order:",
            [
                "1. `JOB.md`",
                "2. `STATUS.json`",
                "3. `PLAN.md`",
                "4. `NOTES.md`",
            ],
            [
                "- Make the smallest viable change that satisfies the job.",
                "- Do not edit `STATUS.json`.",
                "- Do not commit or open PRs.",
                "- If blocked, return a blocked result with a clear reason and questions.",
            ],
            [
                "- Apply the code changes in the worktree.",
                "- Summarize what changed and which files were modified.",
                "- Return only data that matches the provided schema.",
            ]);
    }

    public AgentPromptDefinition BuildRepairPrompt(StageExecutionContext context)
    {
        return BuildImplementerLikePrompt(
            context,
            "# Repair Implementer Role",
            "You are the Ringmaster implementer rerunning in repair mode after verification or review feedback.",
            "Read this material in order:",
            [
                "1. `JOB.md`",
                "2. `STATUS.json`",
                "3. `PLAN.md`",
                "4. `NOTES.md`",
                "5. `artifacts/repair-summary.json`",
                "6. `REVIEW.md`",
            ],
            [
                "- Focus only on the current failure or reviewer findings.",
                "- Preserve good existing work unless the failure summary proves it is wrong.",
                "- Do not edit `STATUS.json`.",
                "- Do not commit or open PRs.",
                "- If blocked, return a blocked result with a clear reason and questions.",
            ],
            [
                "- Apply the smallest viable repair.",
                "- Summarize the fix and list modified files.",
                "- Return only data that matches the provided schema.",
            ]);
    }

    public AgentPromptDefinition BuildReviewerPrompt(StageExecutionContext context)
    {
        string worktreePath = context.Job.Status.Git?.WorktreePath
            ?? throw new InvalidOperationException("Reviewer prompt requires a prepared worktree.");

        StringBuilder prompt = new();
        prompt.AppendLine("# Reviewer Role");
        prompt.AppendLine();
        prompt.AppendLine("You are the Ringmaster reviewer for one software job.");
        prompt.AppendLine();
        prompt.AppendLine("Workspace paths:");
        prompt.AppendLine($"- Worktree root: `{worktreePath}`");
        prompt.AppendLine($"- Job directory: `{context.Job.JobDirectoryPath}`");
        prompt.AppendLine("- Sandbox: read-only");
        prompt.AppendLine("- Forbidden: code edits, commits, PR creation, STATUS.json changes");
        prompt.AppendLine();
        prompt.AppendLine("Read this material in order:");
        prompt.AppendLine("1. `JOB.md`");
        prompt.AppendLine("2. `PLAN.md`");
        prompt.AppendLine("3. `NOTES.md`");
        prompt.AppendLine("4. `artifacts/verification-summary.json`");
        prompt.AppendLine("5. `artifacts/diff.patch`");
        prompt.AppendLine("6. `artifacts/repair-summary.json`");
        prompt.AppendLine("7. `REVIEW.md`");
        prompt.AppendLine();
        prompt.AppendLine("Behavior rules:");
        prompt.AppendLine("- Review the existing change set and verification evidence only.");
        prompt.AppendLine("- Do not edit code or lifecycle state directly.");
        prompt.AppendLine("- Return approve, request_repair, or human_review_required.");
        prompt.AppendLine("- Use human_review_required for ambiguity, unacceptable risk, or policy violations.");
        prompt.AppendLine();
        prompt.AppendLine("Completion checklist:");
        prompt.AppendLine("- Provide a concise review summary.");
        prompt.AppendLine("- List findings and any required repairs.");
        prompt.AppendLine("- Return only data that matches the provided schema.");
        prompt.AppendLine();
        prompt.AppendLine(context.Job.JobMarkdown);

        return new AgentPromptDefinition
        {
            PromptText = prompt.ToString(),
            OutputSchemaJson = BuildReviewerSchema(),
        };
    }

    private AgentPromptDefinition BuildImplementerLikePrompt(
        StageExecutionContext context,
        string heading,
        string roleDescription,
        string readHeading,
        IReadOnlyList<string> readItems,
        IReadOnlyList<string> behaviorRules,
        IReadOnlyList<string> checklistItems)
    {
        string worktreePath = context.Job.Status.Git?.WorktreePath
            ?? throw new InvalidOperationException("Implementer prompt requires a prepared worktree.");

        StringBuilder prompt = new();
        prompt.AppendLine(heading);
        prompt.AppendLine();
        prompt.AppendLine(roleDescription);
        prompt.AppendLine();
        prompt.AppendLine("Workspace paths:");
        prompt.AppendLine($"- Worktree root: `{worktreePath}`");
        prompt.AppendLine($"- Job directory: `{context.Job.JobDirectoryPath}`");
        prompt.AppendLine($"- Writable directories: `{worktreePath}` and `{context.RunDirectoryPath}`");
        prompt.AppendLine("- Forbidden: `.ringmaster/jobs/**/STATUS.json`, commits, PR creation");
        prompt.AppendLine();
        prompt.AppendLine(readHeading);
        foreach (string item in readItems)
        {
            prompt.AppendLine(item);
        }

        prompt.AppendLine();
        prompt.AppendLine("Behavior rules:");
        foreach (string rule in behaviorRules)
        {
            prompt.AppendLine(rule);
        }

        prompt.AppendLine();
        prompt.AppendLine("Completion checklist:");
        foreach (string item in checklistItems)
        {
            prompt.AppendLine(item);
        }

        prompt.AppendLine();
        prompt.AppendLine(context.Job.JobMarkdown);

        return new AgentPromptDefinition
        {
            PromptText = prompt.ToString(),
            OutputSchemaJson = BuildImplementerSchema(),
        };
    }

    private static string BuildPlannerSchema()
    {
        return """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "required": ["result", "summary", "planMarkdown", "needsHuman", "questions"],
          "properties": {
            "result": { "type": "string", "enum": ["completed", "blocked"] },
            "summary": { "type": "string" },
            "planMarkdown": { "type": "string" },
            "needsHuman": { "type": "boolean" },
            "blockerReasonCode": { "type": ["string", "null"] },
            "blockerSummary": { "type": ["string", "null"] },
            "questions": {
              "type": "array",
              "items": { "type": "string" }
            }
          }
        }
        """;
    }

    private static string BuildImplementerSchema()
    {
        return """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "required": ["result", "summary", "filesModified", "recommendedNextChecks", "needsHuman", "questions"],
          "properties": {
            "result": { "type": "string", "enum": ["completed", "blocked"] },
            "summary": { "type": "string" },
            "filesModified": {
              "type": "array",
              "items": { "type": "string" }
            },
            "recommendedNextChecks": {
              "type": "array",
              "items": { "type": "string" }
            },
            "needsHuman": { "type": "boolean" },
            "blockerReasonCode": { "type": ["string", "null"] },
            "blockerSummary": { "type": ["string", "null"] },
            "questions": {
              "type": "array",
              "items": { "type": "string" }
            }
          }
        }
        """;
    }

    private static string BuildReviewerSchema()
    {
        return """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "required": ["verdict", "summary", "findings", "requiredRepairs", "needsHuman"],
          "properties": {
            "verdict": { "type": "string", "enum": ["approve", "request_repair", "human_review_required"] },
            "risk": { "type": ["string", "null"], "enum": ["low", "medium", "high", null] },
            "summary": { "type": "string" },
            "findings": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["severity", "message"],
                "properties": {
                  "severity": { "type": "string" },
                  "message": { "type": "string" }
                }
              }
            },
            "requiredRepairs": {
              "type": "array",
              "items": { "type": "string" }
            },
            "recommendedPrMode": { "type": ["string", "null"] },
            "needsHuman": { "type": "boolean" },
            "blockerSummary": { "type": ["string", "null"] },
            "questions": {
              "type": "array",
              "items": { "type": "string" }
            }
          }
        }
        """;
    }
}
