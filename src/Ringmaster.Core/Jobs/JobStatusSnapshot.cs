namespace Ringmaster.Core.Jobs;

public sealed record class JobStatusSnapshot
{
    public int SchemaVersion { get; init; } = ProductInfo.SchemaVersion;
    public required string JobId { get; init; }
    public required string Title { get; init; }
    public required JobState State { get; init; }
    public required JobState ResumeState { get; init; }
    public required JobExecutionSnapshot Execution { get; init; }
    public int Priority { get; init; }
    public required DateTimeOffset NextEligibleAtUtc { get; init; }
    public JobAttemptCounters Attempts { get; init; } = new();
    public JobGitSnapshot? Git { get; init; }
    public JobFailureSnapshot? LastFailure { get; init; }
    public JobReviewSnapshot Review { get; init; } = new();
    public JobPullRequestSnapshot Pr { get; init; } = new();
    public BlockerInfo? Blocker { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public static JobStatusSnapshot CreateInitial(JobDefinition definition)
    {
        return new JobStatusSnapshot
        {
            JobId = definition.JobId,
            Title = definition.Title,
            State = JobState.QUEUED,
            ResumeState = JobState.QUEUED,
            Execution = JobExecutionSnapshot.Idle(),
            Priority = definition.Priority,
            NextEligibleAtUtc = definition.CreatedAtUtc,
            Attempts = new JobAttemptCounters(),
            Review = new JobReviewSnapshot(),
            Pr = new JobPullRequestSnapshot
            {
                Status = PullRequestStatus.NotStarted,
                Draft = definition.Pr.DraftByDefault,
            },
            CreatedAtUtc = definition.CreatedAtUtc,
            UpdatedAtUtc = definition.CreatedAtUtc,
        };
    }
}

public sealed record class JobExecutionSnapshot
{
    public ExecutionStatus Status { get; init; }
    public string? RunId { get; init; }
    public JobStage? Stage { get; init; }
    public StageRole? Role { get; init; }
    public int Attempt { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? HeartbeatAtUtc { get; init; }
    public int? ProcessId { get; init; }
    public string? SessionId { get; init; }

    public static JobExecutionSnapshot Idle()
    {
        return new JobExecutionSnapshot
        {
            Status = ExecutionStatus.Idle,
            Attempt = 0,
        };
    }
}

public sealed record class JobAttemptCounters
{
    public int Preparing { get; init; }
    public int Implementing { get; init; }
    public int Verifying { get; init; }
    public int Repairing { get; init; }
    public int Reviewing { get; init; }
}

public sealed record class JobGitSnapshot
{
    public string? RepoRoot { get; init; }
    public string? BaseBranch { get; init; }
    public string? BaseCommit { get; init; }
    public string? JobBranch { get; init; }
    public string? WorktreePath { get; init; }
    public string? HeadCommit { get; init; }
    public bool HasUncommittedChanges { get; init; }
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
}

public sealed record class JobFailureSnapshot
{
    public required FailureCategory Category { get; init; }
    public required string Signature { get; init; }
    public required string Summary { get; init; }
    public required DateTimeOffset FirstSeenAtUtc { get; init; }
    public required DateTimeOffset LastSeenAtUtc { get; init; }
    public int RepetitionCount { get; init; }
}

public sealed record class JobReviewSnapshot
{
    public ReviewVerdict Verdict { get; init; } = ReviewVerdict.Pending;
    public ReviewRisk? Risk { get; init; }
}

public sealed record class JobPullRequestSnapshot
{
    public PullRequestStatus Status { get; init; } = PullRequestStatus.NotStarted;
    public string? Url { get; init; }
    public bool Draft { get; init; }
}

public sealed record class BlockerInfo
{
    public required BlockerReasonCode ReasonCode { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> Questions { get; init; } = [];
    public required JobState ResumeState { get; init; }
}
