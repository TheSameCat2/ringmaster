using System.Text.Json.Serialization;

namespace Ringmaster.Core.Jobs;

public sealed record class JobEventRecord
{
    [JsonPropertyName("seq")]
    public required long Sequence { get; init; }

    [JsonPropertyName("ts")]
    public required DateTimeOffset TimestampUtc { get; init; }

    public required JobEventType Type { get; init; }
    public required string JobId { get; init; }
    public string? Title { get; init; }
    public JobState? State { get; init; }
    public JobState? ResumeState { get; init; }
    public JobState? From { get; init; }
    public JobState? To { get; init; }
    public int? Priority { get; init; }
    public bool? PullRequestDraft { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public DateTimeOffset? NextEligibleAtUtc { get; init; }
    public string? RepoRoot { get; init; }
    public string? BaseBranch { get; init; }
    public string? BaseCommit { get; init; }
    public string? JobBranch { get; init; }
    public string? WorktreePath { get; init; }
    public string? HeadCommit { get; init; }
    public bool? HasUncommittedChanges { get; init; }
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
    public string? RunId { get; init; }
    public JobStage? Stage { get; init; }
    public StageRole? Role { get; init; }
    public int? Attempt { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? HeartbeatAtUtc { get; init; }
    public int? ProcessId { get; init; }
    public string? SessionId { get; init; }
    public int? ExitCode { get; init; }
    public FailureCategory? FailureCategory { get; init; }
    public BlockerInfo? Blocker { get; init; }
    public ReviewVerdict? ReviewVerdict { get; init; }
    public ReviewRisk? ReviewRisk { get; init; }
    public string? Summary { get; init; }
    public string? Signature { get; init; }

    public static JobEventRecord CreateJobCreated(long sequence, JobDefinition definition, JobStatusSnapshot status)
    {
        return new JobEventRecord
        {
            Sequence = sequence,
            TimestampUtc = definition.CreatedAtUtc,
            Type = JobEventType.JobCreated,
            JobId = definition.JobId,
            Title = definition.Title,
            State = status.State,
            ResumeState = status.ResumeState,
            Priority = status.Priority,
            PullRequestDraft = status.Pr.Draft,
            CreatedAtUtc = status.CreatedAtUtc,
            UpdatedAtUtc = status.UpdatedAtUtc,
            NextEligibleAtUtc = status.NextEligibleAtUtc,
        };
    }

    public static JobEventRecord CreateStateChanged(string jobId, JobState from, JobState to, DateTimeOffset timestampUtc)
    {
        return new JobEventRecord
        {
            Sequence = 0,
            TimestampUtc = timestampUtc,
            Type = JobEventType.StateChanged,
            JobId = jobId,
            From = from,
            To = to,
            ResumeState = to,
            UpdatedAtUtc = timestampUtc,
            NextEligibleAtUtc = timestampUtc,
        };
    }

    public static JobEventRecord CreateGitStateCaptured(string jobId, JobGitSnapshot gitSnapshot, DateTimeOffset timestampUtc)
    {
        return new JobEventRecord
        {
            Sequence = 0,
            TimestampUtc = timestampUtc,
            Type = JobEventType.GitStateCaptured,
            JobId = jobId,
            RepoRoot = gitSnapshot.RepoRoot,
            BaseBranch = gitSnapshot.BaseBranch,
            BaseCommit = gitSnapshot.BaseCommit,
            JobBranch = gitSnapshot.JobBranch,
            WorktreePath = gitSnapshot.WorktreePath,
            HeadCommit = gitSnapshot.HeadCommit,
            HasUncommittedChanges = gitSnapshot.HasUncommittedChanges,
            ChangedFiles = gitSnapshot.ChangedFiles,
            UpdatedAtUtc = timestampUtc,
        };
    }

    public static JobEventRecord CreateRunStarted(JobRunRecord run)
    {
        return new JobEventRecord
        {
            Sequence = 0,
            TimestampUtc = run.StartedAtUtc,
            Type = JobEventType.RunStarted,
            JobId = run.JobId,
            RunId = run.RunId,
            Stage = run.Stage,
            Role = run.Role,
            Attempt = run.Attempt,
            StartedAtUtc = run.StartedAtUtc,
            HeartbeatAtUtc = run.StartedAtUtc,
            UpdatedAtUtc = run.StartedAtUtc,
        };
    }

    public static JobEventRecord CreateFailureRecorded(
        string jobId,
        FailureCategory failureCategory,
        string signature,
        string summary,
        DateTimeOffset timestampUtc)
    {
        return new JobEventRecord
        {
            Sequence = 0,
            TimestampUtc = timestampUtc,
            Type = JobEventType.FailureRecorded,
            JobId = jobId,
            FailureCategory = failureCategory,
            Signature = signature,
            Summary = summary,
            UpdatedAtUtc = timestampUtc,
        };
    }

    public static JobEventRecord CreateReviewRecorded(
        string jobId,
        ReviewVerdict verdict,
        ReviewRisk? risk,
        string summary,
        DateTimeOffset timestampUtc)
    {
        return new JobEventRecord
        {
            Sequence = 0,
            TimestampUtc = timestampUtc,
            Type = JobEventType.ReviewRecorded,
            JobId = jobId,
            ReviewVerdict = verdict,
            ReviewRisk = risk,
            Summary = summary,
            UpdatedAtUtc = timestampUtc,
        };
    }

    public static JobEventRecord CreateRunCompleted(JobRunRecord run)
    {
        DateTimeOffset completedAt = run.CompletedAtUtc ?? run.StartedAtUtc;
        return new JobEventRecord
        {
            Sequence = 0,
            TimestampUtc = completedAt,
            Type = JobEventType.RunCompleted,
            JobId = run.JobId,
            RunId = run.RunId,
            Stage = run.Stage,
            Role = run.Role,
            ExitCode = run.ExitCode,
            UpdatedAtUtc = completedAt,
        };
    }

    public static JobEventRecord CreateBlocked(string jobId, BlockerInfo blocker, DateTimeOffset timestampUtc)
    {
        return new JobEventRecord
        {
            Sequence = 0,
            TimestampUtc = timestampUtc,
            Type = JobEventType.JobBlocked,
            JobId = jobId,
            Blocker = blocker,
            ResumeState = blocker.ResumeState,
            UpdatedAtUtc = timestampUtc,
            NextEligibleAtUtc = timestampUtc,
        };
    }

    public static JobEventRecord CreateFailed(
        string jobId,
        FailureCategory failureCategory,
        string signature,
        string summary,
        DateTimeOffset timestampUtc)
    {
        return new JobEventRecord
        {
            Sequence = 0,
            TimestampUtc = timestampUtc,
            Type = JobEventType.JobFailed,
            JobId = jobId,
            FailureCategory = failureCategory,
            Summary = summary,
            Signature = signature,
            UpdatedAtUtc = timestampUtc,
        };
    }
}
