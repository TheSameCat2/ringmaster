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
        - Writable directory: `{context.Job.JobDirectoryPath}`
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
        string worktreePath = context.Job.Status.Git?.WorktreePath
            ?? throw new InvalidOperationException("Implementer prompt requires a prepared worktree.");

        StringBuilder prompt = new();
        prompt.AppendLine("# Implementer Role");
        prompt.AppendLine();
        prompt.AppendLine("You are the Ringmaster implementer for one software job.");
        prompt.AppendLine();
        prompt.AppendLine("Workspace paths:");
        prompt.AppendLine($"- Worktree root: `{worktreePath}`");
        prompt.AppendLine($"- Job directory: `{context.Job.JobDirectoryPath}`");
        prompt.AppendLine($"- Writable directories: `{worktreePath}` and `{context.Job.JobDirectoryPath}`");
        prompt.AppendLine("- Forbidden: `.ringmaster/jobs/**/STATUS.json`, commits, PR creation");
        prompt.AppendLine();
        prompt.AppendLine("Read this material in order:");
        prompt.AppendLine("1. `JOB.md`");
        prompt.AppendLine("2. `STATUS.json`");
        prompt.AppendLine("3. `PLAN.md`");
        prompt.AppendLine("4. `NOTES.md`");
        prompt.AppendLine();
        prompt.AppendLine("Behavior rules:");
        prompt.AppendLine("- Make the smallest viable change that satisfies the job.");
        prompt.AppendLine("- Do not edit `STATUS.json`.");
        prompt.AppendLine("- Do not commit or open PRs.");
        prompt.AppendLine("- If blocked, return a blocked result with a clear reason and questions.");
        prompt.AppendLine();
        prompt.AppendLine("Completion checklist:");
        prompt.AppendLine("- Apply the code changes in the worktree.");
        prompt.AppendLine("- Summarize what changed and which files were modified.");
        prompt.AppendLine("- Return only data that matches the provided schema.");
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
}
