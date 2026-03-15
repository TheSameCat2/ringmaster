using Ringmaster.Core.Jobs;

namespace Ringmaster.Git;

public sealed record class GitWorktreeInfo
{
    public required string Path { get; init; }
    public string? Branch { get; init; }
    public string? Head { get; init; }
    public bool Locked { get; init; }
    public string? LockReason { get; init; }
}

public sealed record class PreparedWorktree
{
    public required string RepoRoot { get; init; }
    public required string BaseBranch { get; init; }
    public required string BaseCommit { get; init; }
    public required string JobBranch { get; init; }
    public required string WorktreePath { get; init; }
    public required string HeadCommit { get; init; }
}

public sealed record class GitStatusInfo
{
    public required string HeadCommit { get; init; }
    public bool HasUncommittedChanges { get; init; }
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
}

public sealed record class VerificationCommandRecord
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string RunId { get; init; }
    public required string JobId { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public IReadOnlyList<string> EnvironmentVariableNamesUsed { get; init; } = [];
    public int TimeoutSeconds { get; init; }
    public int ExitCode { get; init; }
    public double DurationMs { get; init; }
    public bool TimedOut { get; init; }
    public string? StdoutPath { get; init; }
    public string? StderrPath { get; init; }
}

public sealed record class VerificationSummary
{
    public required string JobId { get; init; }
    public required string RunId { get; init; }
    public required string ProfileName { get; init; }
    public bool Succeeded { get; init; }
    public IReadOnlyList<VerificationCommandRecord> Commands { get; init; } = [];
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
}

public sealed record class VerificationFailureSummary
{
    public required string JobId { get; init; }
    public required string RunId { get; init; }
    public required string ProfileName { get; init; }
    public required string CommandName { get; init; }
    public required FailureCategory Category { get; init; }
    public required string Signature { get; init; }
    public required string Summary { get; init; }
    public int ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public string? StdoutPath { get; init; }
    public string? StderrPath { get; init; }
    public IReadOnlyList<string> Highlights { get; init; } = [];
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
}

public sealed record class RepositoryPreparationResult
{
    public bool Succeeded { get; init; }
    public string Summary { get; init; } = string.Empty;
    public JobGitSnapshot? GitSnapshot { get; init; }
    public BlockerInfo? Blocker { get; init; }
    public FailureCategory? FailureCategory { get; init; }
}
