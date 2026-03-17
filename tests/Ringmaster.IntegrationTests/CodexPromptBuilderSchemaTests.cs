using System.Text.Json;
using Ringmaster.Codex;
using Ringmaster.Core.Jobs;

namespace Ringmaster.IntegrationTests;

public sealed class CodexPromptBuilderSchemaTests
{
    [Fact]
    public void PromptSchemasRequireEveryTopLevelPropertyForCodexStructuredOutputs()
    {
        CodexPromptBuilder builder = new();
        StageExecutionContext context = CreateContext();

        string[] schemas =
        [
            builder.BuildPlannerPrompt(context).OutputSchemaJson,
            builder.BuildImplementerPrompt(context).OutputSchemaJson,
            builder.BuildRepairPrompt(context).OutputSchemaJson,
            builder.BuildReviewerPrompt(context).OutputSchemaJson,
        ];

        foreach (string schemaJson in schemas)
        {
            using JsonDocument document = JsonDocument.Parse(schemaJson);
            JsonElement root = document.RootElement;
            HashSet<string> required = root.GetProperty("required")
                .EnumerateArray()
                .Select(static item => item.GetString())
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

            JsonElement.ObjectEnumerator properties = root.GetProperty("properties").EnumerateObject();
            foreach (JsonProperty property in properties)
            {
                Assert.Contains(property.Name, required);
            }
        }
    }

    private static StageExecutionContext CreateContext()
    {
        JobDefinition definition = new()
        {
            JobId = "job-20260317-schema",
            Title = "Schema test",
            Description = "Ensure Codex output schemas remain valid.",
            Repo = new JobRepositoryTarget
            {
                BaseBranch = "main",
                VerificationProfile = "default",
            },
            CreatedAtUtc = new DateTimeOffset(2026, 3, 17, 0, 0, 0, TimeSpan.Zero),
            CreatedBy = "tests",
        };

        JobStatusSnapshot status = JobStatusSnapshot.CreateInitial(definition) with
        {
            Git = new JobGitSnapshot
            {
                RepoRoot = "/tmp/repo",
                BaseBranch = "main",
                BaseCommit = "abc123",
                JobBranch = "ringmaster/test",
                WorktreePath = "/tmp/repo-worktree",
                HeadCommit = "abc123",
                HasUncommittedChanges = false,
                ChangedFiles = [],
            },
        };

        StoredJob job = new()
        {
            JobDirectoryPath = "/tmp/job",
            Definition = definition,
            JobMarkdown = "# Schema test",
            Status = status,
            Events = [],
        };

        JobRunRecord run = new()
        {
            RunId = "0001",
            JobId = definition.JobId,
            Stage = JobStage.PREPARING,
            Role = StageRole.Planner,
            Attempt = 1,
            StartedAtUtc = definition.CreatedAtUtc,
            Tool = "codex",
            Command = ["codex", "exec", "planner"],
        };

        return new StageExecutionContext
        {
            Job = job,
            Run = run,
            RunDirectoryPath = "/tmp/job/runs/0001",
        };
    }
}
