using Ringmaster.Core.Jobs;

namespace Ringmaster.Codex;

public interface ICodexRunner
{
    Task<CodexExecResult> ExecuteAsync(CodexExecRequest request, CancellationToken cancellationToken);
}

public interface IAgentRunner
{
    Task<AgentExecutionResult> RunAsync(AgentExecutionRequest request, CancellationToken cancellationToken);
}

public enum AgentRunKind
{
    Planner,
    Implementer,
}

public enum AgentSandboxMode
{
    ReadOnly,
    WorkspaceWrite,
}

public sealed record class AgentExecutionRequest
{
    public required AgentRunKind Kind { get; init; }
    public required string WorkingDirectory { get; init; }
    public IReadOnlyList<string> AdditionalWritableDirectories { get; init; } = [];
    public required string RunDirectoryPath { get; init; }
    public required string PromptText { get; init; }
    public required string OutputSchemaJson { get; init; }
    public required AgentSandboxMode SandboxMode { get; init; }
    public string? Model { get; init; }
    public bool SkipGitRepoCheck { get; init; }
}

public sealed record class AgentExecutionResult
{
    public int ExitCode { get; init; }
    public string? SessionId { get; init; }
    public string? FinalOutputText { get; init; }
    public RunArtifacts Artifacts { get; init; } = new();
}

public sealed record class CodexExecRequest
{
    public required AgentRunKind Kind { get; init; }
    public required string WorkingDirectory { get; init; }
    public IReadOnlyList<string> AdditionalWritableDirectories { get; init; } = [];
    public required string PromptText { get; init; }
    public required string OutputSchemaPath { get; init; }
    public required string OutputLastMessagePath { get; init; }
    public required string EventLogPath { get; init; }
    public required string StderrPath { get; init; }
    public required AgentSandboxMode SandboxMode { get; init; }
    public string? Model { get; init; }
    public bool SkipGitRepoCheck { get; init; }
}

public sealed record class CodexExecResult
{
    public int ExitCode { get; init; }
    public string? SessionId { get; init; }
    public string? FinalOutputText { get; init; }
}

public sealed record class PlannerAgentOutput
{
    public required string Result { get; init; }
    public required string Summary { get; init; }
    public required string PlanMarkdown { get; init; }
    public bool NeedsHuman { get; init; }
    public string? BlockerReasonCode { get; init; }
    public string? BlockerSummary { get; init; }
    public IReadOnlyList<string> Questions { get; init; } = [];
}

public sealed record class ImplementerAgentOutput
{
    public required string Result { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> FilesModified { get; init; } = [];
    public IReadOnlyList<string> RecommendedNextChecks { get; init; } = [];
    public bool NeedsHuman { get; init; }
    public string? BlockerReasonCode { get; init; }
    public string? BlockerSummary { get; init; }
    public IReadOnlyList<string> Questions { get; init; } = [];
}

public sealed record class AgentPromptDefinition
{
    public required string PromptText { get; init; }
    public required string OutputSchemaJson { get; init; }
}
