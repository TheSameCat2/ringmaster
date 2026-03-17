using System.Text.Json;
using Ringmaster.Codex;
using Ringmaster.Core.Jobs;

namespace Ringmaster.IntegrationTests;

public sealed class CodexPromptBuilderSchemaTests
{
    public static IEnumerable<object[]> SchemaCases()
    {
        CodexPromptBuilder builder = new();
        StageExecutionContext context = CreateContext();

        yield return [nameof(CodexPromptBuilder.BuildPlannerPrompt), builder.BuildPlannerPrompt(context).OutputSchemaJson];
        yield return [nameof(CodexPromptBuilder.BuildImplementerPrompt), builder.BuildImplementerPrompt(context).OutputSchemaJson];
        yield return [nameof(CodexPromptBuilder.BuildRepairPrompt), builder.BuildRepairPrompt(context).OutputSchemaJson];
        yield return [nameof(CodexPromptBuilder.BuildReviewerPrompt), builder.BuildReviewerPrompt(context).OutputSchemaJson];
    }

    [Theory]
    [MemberData(nameof(SchemaCases))]
    public void PromptSchemasRequireExactlyTheDefinedTopLevelPropertiesForCodexStructuredOutputs(
        string schemaName,
        string schemaJson)
    {
        _ = schemaName;
        using JsonDocument document = JsonDocument.Parse(schemaJson);
        JsonElement root = document.RootElement;
        string[] required = root.GetProperty("required")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .OfType<string>()
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();

        string[] properties = root.GetProperty("properties")
            .EnumerateObject()
            .Select(static property => property.Name)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(properties, required);
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
