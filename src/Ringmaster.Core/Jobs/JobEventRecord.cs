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
}
