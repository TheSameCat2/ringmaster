namespace Ringmaster.Core.Jobs;

public sealed class JobSnapshotRebuilder
{
    public JobStatusSnapshot Rebuild(IEnumerable<JobEventRecord> events)
    {
        JobStatusSnapshot? snapshot = null;

        foreach (JobEventRecord jobEvent in events.OrderBy(jobEvent => jobEvent.Sequence))
        {
            snapshot = Apply(snapshot, jobEvent);
        }

        return snapshot ?? throw new InvalidOperationException("The event log did not contain a JobCreated event.");
    }

    private static JobStatusSnapshot Apply(JobStatusSnapshot? current, JobEventRecord jobEvent)
    {
        return jobEvent.Type switch
        {
            JobEventType.JobCreated => ApplyJobCreated(jobEvent),
            JobEventType.StateChanged => ApplyStateChanged(current, jobEvent),
            JobEventType.GitStateCaptured => ApplyGitStateCaptured(current, jobEvent),
            JobEventType.FailureRecorded => ApplyFailureRecorded(current, jobEvent),
            JobEventType.ReviewRecorded => ApplyReviewRecorded(current, jobEvent),
            JobEventType.RunStarted => ApplyRunStarted(current, jobEvent),
            JobEventType.RunHeartbeat => ApplyRunHeartbeat(current, jobEvent),
            JobEventType.RunCompleted => ApplyRunCompleted(current, jobEvent),
            JobEventType.JobBlocked => ApplyBlocked(current, jobEvent),
            JobEventType.JobFailed => ApplyFailed(current, jobEvent),
            _ => throw new InvalidOperationException($"Unsupported event type '{jobEvent.Type}'."),
        };
    }

    private static JobStatusSnapshot ApplyJobCreated(JobEventRecord jobEvent)
    {
        DateTimeOffset createdAt = jobEvent.CreatedAtUtc ?? jobEvent.TimestampUtc;

        return new JobStatusSnapshot
        {
            JobId = jobEvent.JobId,
            Title = jobEvent.Title ?? throw new InvalidOperationException("JobCreated events must include a title."),
            State = jobEvent.State ?? JobState.QUEUED,
            ResumeState = jobEvent.ResumeState ?? jobEvent.State ?? JobState.QUEUED,
            Execution = JobExecutionSnapshot.Idle(),
            Priority = jobEvent.Priority ?? 0,
            NextEligibleAtUtc = jobEvent.NextEligibleAtUtc ?? createdAt,
            Attempts = new JobAttemptCounters(),
            Review = new JobReviewSnapshot(),
            Pr = new JobPullRequestSnapshot
            {
                Draft = jobEvent.PullRequestDraft ?? false,
            },
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = jobEvent.UpdatedAtUtc ?? createdAt,
        };
    }

    private static JobStatusSnapshot ApplyStateChanged(JobStatusSnapshot? current, JobEventRecord jobEvent)
    {
        JobStatusSnapshot snapshot = current ?? throw new InvalidOperationException("StateChanged cannot be applied before JobCreated.");

        return snapshot with
        {
            State = jobEvent.To ?? snapshot.State,
            ResumeState = jobEvent.ResumeState ?? jobEvent.To ?? snapshot.ResumeState,
            Blocker = null,
            NextEligibleAtUtc = jobEvent.NextEligibleAtUtc ?? jobEvent.TimestampUtc,
            UpdatedAtUtc = jobEvent.UpdatedAtUtc ?? jobEvent.TimestampUtc,
        };
    }

    private static JobStatusSnapshot ApplyRunStarted(JobStatusSnapshot? current, JobEventRecord jobEvent)
    {
        JobStatusSnapshot snapshot = current ?? throw new InvalidOperationException("RunStarted cannot be applied before JobCreated.");
        JobAttemptCounters attempts = snapshot.Attempts;

        if (jobEvent.Stage is not null)
        {
            attempts = jobEvent.Stage.Value switch
            {
                JobStage.PREPARING => attempts with { Preparing = jobEvent.Attempt ?? attempts.Preparing },
                JobStage.IMPLEMENTING => attempts with { Implementing = jobEvent.Attempt ?? attempts.Implementing },
                JobStage.VERIFYING => attempts with { Verifying = jobEvent.Attempt ?? attempts.Verifying },
                JobStage.REPAIRING => attempts with { Repairing = jobEvent.Attempt ?? attempts.Repairing },
                JobStage.REVIEWING => attempts with { Reviewing = jobEvent.Attempt ?? attempts.Reviewing },
                _ => attempts,
            };
        }

        return snapshot with
        {
            Execution = new JobExecutionSnapshot
            {
                Status = ExecutionStatus.Running,
                RunId = jobEvent.RunId,
                Stage = jobEvent.Stage,
                Role = jobEvent.Role,
                Attempt = jobEvent.Attempt ?? 0,
                StartedAtUtc = jobEvent.StartedAtUtc ?? jobEvent.TimestampUtc,
                HeartbeatAtUtc = jobEvent.HeartbeatAtUtc ?? jobEvent.StartedAtUtc ?? jobEvent.TimestampUtc,
                ProcessId = jobEvent.ProcessId,
                SessionId = jobEvent.SessionId,
            },
            Attempts = attempts,
            UpdatedAtUtc = jobEvent.UpdatedAtUtc ?? jobEvent.TimestampUtc,
        };
    }

    private static JobStatusSnapshot ApplyFailureRecorded(JobStatusSnapshot? current, JobEventRecord jobEvent)
    {
        JobStatusSnapshot snapshot = current ?? throw new InvalidOperationException("FailureRecorded cannot be applied before JobCreated.");

        return snapshot with
        {
            LastFailure = BuildFailureSnapshot(snapshot.LastFailure, jobEvent),
            UpdatedAtUtc = jobEvent.UpdatedAtUtc ?? jobEvent.TimestampUtc,
        };
    }

    private static JobStatusSnapshot ApplyReviewRecorded(JobStatusSnapshot? current, JobEventRecord jobEvent)
    {
        JobStatusSnapshot snapshot = current ?? throw new InvalidOperationException("ReviewRecorded cannot be applied before JobCreated.");

        return snapshot with
        {
            Review = new JobReviewSnapshot
            {
                Verdict = jobEvent.ReviewVerdict ?? ReviewVerdict.Pending,
                Risk = jobEvent.ReviewRisk,
            },
            UpdatedAtUtc = jobEvent.UpdatedAtUtc ?? jobEvent.TimestampUtc,
        };
    }

    private static JobStatusSnapshot ApplyGitStateCaptured(JobStatusSnapshot? current, JobEventRecord jobEvent)
    {
        JobStatusSnapshot snapshot = current ?? throw new InvalidOperationException("GitStateCaptured cannot be applied before JobCreated.");

        return snapshot with
        {
            Git = new JobGitSnapshot
            {
                RepoRoot = jobEvent.RepoRoot,
                BaseBranch = jobEvent.BaseBranch,
                BaseCommit = jobEvent.BaseCommit,
                JobBranch = jobEvent.JobBranch,
                WorktreePath = jobEvent.WorktreePath,
                HeadCommit = jobEvent.HeadCommit,
                HasUncommittedChanges = jobEvent.HasUncommittedChanges ?? false,
                ChangedFiles = jobEvent.ChangedFiles,
            },
            UpdatedAtUtc = jobEvent.UpdatedAtUtc ?? jobEvent.TimestampUtc,
        };
    }

    private static JobStatusSnapshot ApplyRunHeartbeat(JobStatusSnapshot? current, JobEventRecord jobEvent)
    {
        JobStatusSnapshot snapshot = current ?? throw new InvalidOperationException("RunHeartbeat cannot be applied before JobCreated.");

        return snapshot with
        {
            Execution = snapshot.Execution with
            {
                HeartbeatAtUtc = jobEvent.HeartbeatAtUtc ?? jobEvent.TimestampUtc,
            },
            UpdatedAtUtc = jobEvent.UpdatedAtUtc ?? jobEvent.TimestampUtc,
            NextEligibleAtUtc = jobEvent.NextEligibleAtUtc ?? snapshot.NextEligibleAtUtc,
        };
    }

    private static JobStatusSnapshot ApplyRunCompleted(JobStatusSnapshot? current, JobEventRecord jobEvent)
    {
        JobStatusSnapshot snapshot = current ?? throw new InvalidOperationException("RunCompleted cannot be applied before JobCreated.");

        return snapshot with
        {
            Execution = JobExecutionSnapshot.Idle(),
            UpdatedAtUtc = jobEvent.UpdatedAtUtc ?? jobEvent.TimestampUtc,
            NextEligibleAtUtc = jobEvent.NextEligibleAtUtc ?? snapshot.NextEligibleAtUtc,
        };
    }

    private static JobStatusSnapshot ApplyBlocked(JobStatusSnapshot? current, JobEventRecord jobEvent)
    {
        JobStatusSnapshot snapshot = current ?? throw new InvalidOperationException("JobBlocked cannot be applied before JobCreated.");
        BlockerInfo blocker = jobEvent.Blocker ?? throw new InvalidOperationException("JobBlocked events must include blocker info.");

        return snapshot with
        {
            State = JobState.BLOCKED,
            ResumeState = blocker.ResumeState,
            Blocker = blocker,
            Execution = JobExecutionSnapshot.Idle(),
            UpdatedAtUtc = jobEvent.UpdatedAtUtc ?? jobEvent.TimestampUtc,
            NextEligibleAtUtc = jobEvent.NextEligibleAtUtc ?? jobEvent.TimestampUtc,
        };
    }

    private static JobStatusSnapshot ApplyFailed(JobStatusSnapshot? current, JobEventRecord jobEvent)
    {
        JobStatusSnapshot snapshot = current ?? throw new InvalidOperationException("JobFailed cannot be applied before JobCreated.");

        return snapshot with
        {
            State = JobState.FAILED,
            LastFailure = BuildFailureSnapshot(snapshot.LastFailure, jobEvent) ?? snapshot.LastFailure,
            Execution = JobExecutionSnapshot.Idle(),
            UpdatedAtUtc = jobEvent.UpdatedAtUtc ?? jobEvent.TimestampUtc,
            NextEligibleAtUtc = jobEvent.NextEligibleAtUtc ?? snapshot.NextEligibleAtUtc,
        };
    }

    private static JobFailureSnapshot? BuildFailureSnapshot(JobFailureSnapshot? previous, JobEventRecord jobEvent)
    {
        if (jobEvent.FailureCategory is null || jobEvent.Signature is null || jobEvent.Summary is null)
        {
            return null;
        }

        bool repeated = previous is not null
            && string.Equals(previous.Signature, jobEvent.Signature, StringComparison.Ordinal);

        return new JobFailureSnapshot
        {
            Category = jobEvent.FailureCategory.Value,
            Signature = jobEvent.Signature,
            Summary = jobEvent.Summary,
            FirstSeenAtUtc = repeated ? previous!.FirstSeenAtUtc : jobEvent.TimestampUtc,
            LastSeenAtUtc = jobEvent.TimestampUtc,
            RepetitionCount = repeated ? previous!.RepetitionCount + 1 : 1,
        };
    }
}
