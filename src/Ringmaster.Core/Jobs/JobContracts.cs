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

public interface IStageRunner
{
    JobStage Stage { get; }
    StageRole Role { get; }
    Task<StageExecutionResult> RunAsync(StageExecutionContext context, CancellationToken cancellationToken);
}

public sealed record class StageDescriptor(JobStage Stage, StageRole Role);

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

    public static StageExecutionResult Succeeded(JobState nextState, string summary)
    {
        return new StageExecutionResult
        {
            Outcome = StageExecutionOutcome.Succeeded,
            NextState = nextState,
            Summary = summary,
        };
    }

    public static StageExecutionResult Blocked(BlockerInfo blocker, string summary)
    {
        return new StageExecutionResult
        {
            Outcome = StageExecutionOutcome.Blocked,
            Summary = summary,
            Blocker = blocker,
        };
    }

    public static StageExecutionResult Failed(FailureCategory failureCategory, string summary)
    {
        return new StageExecutionResult
        {
            Outcome = StageExecutionOutcome.Failed,
            Summary = summary,
            FailureCategory = failureCategory,
        };
    }
}
