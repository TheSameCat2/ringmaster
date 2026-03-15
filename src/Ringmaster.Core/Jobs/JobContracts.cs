namespace Ringmaster.Core.Jobs;

public interface IJobRepository
{
    Task<StoredJob> CreateAsync(JobCreateRequest request, CancellationToken cancellationToken);
    Task<StoredJob?> GetAsync(string jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobStatusListItem>> ListAsync(CancellationToken cancellationToken);
    Task<JobStatusSnapshot> RebuildStatusAsync(string jobId, CancellationToken cancellationToken);
    Task<JobStatusSnapshot> AppendEventAsync(string jobId, JobEventRecord jobEvent, CancellationToken cancellationToken);
    Task<int> GetNextRunNumberAsync(string jobId, CancellationToken cancellationToken);
    Task SaveRunAsync(string jobId, JobRunRecord run, CancellationToken cancellationToken);
}

public interface IJobIdGenerator
{
    string CreateId(DateTimeOffset timestampUtc);
}

public sealed record class JobCreateRequest
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public string? JobMarkdown { get; init; }
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];
    public IReadOnlyList<string> AllowedPaths { get; init; } = [];
    public IReadOnlyList<string> ForbiddenPaths { get; init; } = [];
    public int MaxFilesChangedSoft { get; init; }
    public string BaseBranch { get; init; } = "master";
    public string VerificationProfile { get; init; } = "default";
    public bool AutoOpenPullRequest { get; init; }
    public bool DraftPullRequest { get; init; } = true;
    public IReadOnlyList<string> PullRequestLabels { get; init; } = [];
    public int Priority { get; init; } = 50;
    public required string CreatedBy { get; init; }
}

public sealed record class StoredJob
{
    public required string JobDirectoryPath { get; init; }
    public required JobDefinition Definition { get; init; }
    public required string JobMarkdown { get; init; }
    public required JobStatusSnapshot Status { get; init; }
    public IReadOnlyList<JobEventRecord> Events { get; init; } = [];
}

public sealed record class JobStatusListItem
{
    public required string JobId { get; init; }
    public required string Title { get; init; }
    public required JobState State { get; init; }
    public int Priority { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public interface IStateMachine
{
    bool CanTransition(JobState from, JobState to);
    void EnsureCanTransition(JobState from, JobState to);
    bool IsAutomaticTerminal(JobState state);
    StageDescriptor? GetStageDescriptor(JobState state);
}

public interface IFailureClassifier
{
    FailureClassification Classify(FailureClassificationContext context);
}

public interface IStageRunner
{
    JobStage Stage { get; }
    StageRole Role { get; }
    StageRunDescriptor DescribeRun(StoredJob job);
    Task<StageExecutionResult> RunAsync(StageExecutionContext context, CancellationToken cancellationToken);
}

public sealed record class StageDescriptor(JobStage Stage, StageRole Role);

public sealed record class StageRunDescriptor
{
    public required string Tool { get; init; }
    public IReadOnlyList<string> Command { get; init; } = [];
}

public sealed record class StageExecutionContext
{
    public required StoredJob Job { get; init; }
    public required JobRunRecord Run { get; init; }
    public required string RunDirectoryPath { get; init; }
}

public enum StageExecutionOutcome
{
    Succeeded,
    Blocked,
    Failed,
}

public sealed record class StageExecutionResult
{
    public required StageExecutionOutcome Outcome { get; init; }
    public JobState? NextState { get; init; }
    public string Summary { get; init; } = string.Empty;
    public BlockerInfo? Blocker { get; init; }
    public FailureCategory? FailureCategory { get; init; }
    public string? FailureSignature { get; init; }
    public ReviewVerdict? ReviewVerdict { get; init; }
    public ReviewRisk? ReviewRisk { get; init; }
    public RunArtifacts Artifacts { get; init; } = new();
    public string? SessionId { get; init; }
    public int? ExitCode { get; init; }

    public static StageExecutionResult Succeeded(
        JobState nextState,
        string summary,
        RunArtifacts? artifacts = null,
        FailureCategory? failureCategory = null,
        string? failureSignature = null,
        ReviewVerdict? reviewVerdict = null,
        ReviewRisk? reviewRisk = null,
        string? sessionId = null,
        int? exitCode = null)
    {
        return new StageExecutionResult
        {
            Outcome = StageExecutionOutcome.Succeeded,
            NextState = nextState,
            Summary = summary,
            FailureCategory = failureCategory,
            FailureSignature = failureSignature,
            ReviewVerdict = reviewVerdict,
            ReviewRisk = reviewRisk,
            Artifacts = artifacts ?? new RunArtifacts(),
            SessionId = sessionId,
            ExitCode = exitCode,
        };
    }

    public static StageExecutionResult Blocked(
        BlockerInfo blocker,
        string summary,
        RunArtifacts? artifacts = null,
        FailureCategory? failureCategory = null,
        string? failureSignature = null,
        ReviewVerdict? reviewVerdict = null,
        ReviewRisk? reviewRisk = null,
        string? sessionId = null,
        int? exitCode = null)
    {
        return new StageExecutionResult
        {
            Outcome = StageExecutionOutcome.Blocked,
            Summary = summary,
            Blocker = blocker,
            FailureCategory = failureCategory,
            FailureSignature = failureSignature,
            ReviewVerdict = reviewVerdict,
            ReviewRisk = reviewRisk,
            Artifacts = artifacts ?? new RunArtifacts(),
            SessionId = sessionId,
            ExitCode = exitCode,
        };
    }

    public static StageExecutionResult Failed(
        FailureCategory failureCategory,
        string summary,
        RunArtifacts? artifacts = null,
        string? failureSignature = null,
        string? sessionId = null,
        int? exitCode = null)
    {
        return new StageExecutionResult
        {
            Outcome = StageExecutionOutcome.Failed,
            Summary = summary,
            FailureCategory = failureCategory,
            FailureSignature = failureSignature,
            Artifacts = artifacts ?? new RunArtifacts(),
            SessionId = sessionId,
            ExitCode = exitCode,
        };
    }
}

public sealed record class FailureClassificationContext
{
    public required JobStage Stage { get; init; }
    public required string CommandName { get; init; }
    public required string CommandFileName { get; init; }
    public IReadOnlyList<string> CommandArguments { get; init; } = [];
    public required int ExitCode { get; init; }
    public required bool TimedOut { get; init; }
    public string StdoutText { get; init; } = string.Empty;
    public string StderrText { get; init; } = string.Empty;
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
}

public sealed record class FailureClassification
{
    public required FailureCategory Category { get; init; }
    public required string Signature { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> Highlights { get; init; } = [];
}

public sealed record class RepairLoopPolicy
{
    public int MaxRepairAttempts { get; init; } = 4;
    public int MaxRepeatedFailureSignatures { get; init; } = 2;
}
