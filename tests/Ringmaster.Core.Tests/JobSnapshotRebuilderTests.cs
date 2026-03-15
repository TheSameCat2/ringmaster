using Ringmaster.Core.Jobs;

namespace Ringmaster.Core.Tests;

public sealed class JobSnapshotRebuilderTests
{
    [Fact]
    public void RebuildCreatesInitialQueuedSnapshotFromJobCreatedEvent()
    {
        JobDefinition definition = new()
        {
            JobId = "job-20260315-7f3c9b2a",
            Title = "Add retry handling",
            Description = "Implement bounded retries for retryable failures.",
            Repo = new JobRepositoryTarget
            {
                BaseBranch = "master",
                VerificationProfile = "default",
            },
            CreatedAtUtc = new DateTimeOffset(2026, 3, 15, 16, 45, 0, TimeSpan.Zero),
            CreatedBy = "tester",
        };
        JobStatusSnapshot initialStatus = JobStatusSnapshot.CreateInitial(definition);
        JobEventRecord created = JobEventRecord.CreateJobCreated(1, definition, initialStatus);
        JobSnapshotRebuilder rebuilder = new();

        JobStatusSnapshot rebuilt = rebuilder.Rebuild([created]);

        Assert.Equal(initialStatus, rebuilt);
    }

    [Fact]
    public void RebuildAppliesStateChangesAfterCreation()
    {
        JobDefinition definition = new()
        {
            JobId = "job-20260315-7f3c9b2a",
            Title = "Add retry handling",
            Description = "Implement bounded retries for retryable failures.",
            Repo = new JobRepositoryTarget
            {
                BaseBranch = "master",
                VerificationProfile = "default",
            },
            CreatedAtUtc = new DateTimeOffset(2026, 3, 15, 16, 45, 0, TimeSpan.Zero),
            CreatedBy = "tester",
        };
        JobStatusSnapshot initialStatus = JobStatusSnapshot.CreateInitial(definition);
        JobEventRecord created = JobEventRecord.CreateJobCreated(1, definition, initialStatus);
        JobEventRecord stateChanged = new()
        {
            Sequence = 2,
            TimestampUtc = definition.CreatedAtUtc.AddMinutes(1),
            Type = JobEventType.StateChanged,
            JobId = definition.JobId,
            From = JobState.QUEUED,
            To = JobState.PREPARING,
            ResumeState = JobState.PREPARING,
            UpdatedAtUtc = definition.CreatedAtUtc.AddMinutes(1),
            NextEligibleAtUtc = definition.CreatedAtUtc.AddMinutes(1),
        };
        JobSnapshotRebuilder rebuilder = new();

        JobStatusSnapshot rebuilt = rebuilder.Rebuild([created, stateChanged]);

        Assert.Equal(JobState.PREPARING, rebuilt.State);
        Assert.Equal(JobState.PREPARING, rebuilt.ResumeState);
        Assert.Equal(definition.CreatedAtUtc.AddMinutes(1), rebuilt.UpdatedAtUtc);
    }

    [Fact]
    public void RebuildTracksCurrentRunAndClearsExecutionWhenTheRunCompletes()
    {
        DateTimeOffset createdAt = new(2026, 3, 15, 16, 45, 0, TimeSpan.Zero);
        JobDefinition definition = new()
        {
            JobId = "job-20260315-7f3c9b2a",
            Title = "Add retry handling",
            Description = "Implement bounded retries for retryable failures.",
            Repo = new JobRepositoryTarget
            {
                BaseBranch = "master",
                VerificationProfile = "default",
            },
            CreatedAtUtc = createdAt,
            CreatedBy = "tester",
        };

        JobStatusSnapshot initialStatus = JobStatusSnapshot.CreateInitial(definition);
        JobRunRecord run = new()
        {
            RunId = "0001-preparing-planner",
            JobId = definition.JobId,
            Stage = JobStage.PREPARING,
            Role = StageRole.Planner,
            Attempt = 1,
            StartedAtUtc = createdAt.AddMinutes(1),
            CompletedAtUtc = createdAt.AddMinutes(2),
            Tool = "fake",
            Command = ["fake-runner", "PREPARING"],
            ExitCode = 0,
            Result = RunResult.Completed,
        };
        JobSnapshotRebuilder rebuilder = new();

        JobStatusSnapshot running = rebuilder.Rebuild(
        [
            JobEventRecord.CreateJobCreated(1, definition, initialStatus),
            JobEventRecord.CreateStateChanged(definition.JobId, JobState.QUEUED, JobState.PREPARING, createdAt.AddSeconds(30)) with { Sequence = 2 },
            JobEventRecord.CreateRunStarted(run) with { Sequence = 3 },
        ]);

        Assert.Equal(ExecutionStatus.Running, running.Execution.Status);
        Assert.Equal(run.RunId, running.Execution.RunId);
        Assert.Equal(1, running.Attempts.Preparing);

        JobStatusSnapshot completed = rebuilder.Rebuild(
        [
            JobEventRecord.CreateJobCreated(1, definition, initialStatus),
            JobEventRecord.CreateStateChanged(definition.JobId, JobState.QUEUED, JobState.PREPARING, createdAt.AddSeconds(30)) with { Sequence = 2 },
            JobEventRecord.CreateRunStarted(run) with { Sequence = 3 },
            JobEventRecord.CreateRunCompleted(run) with { Sequence = 4 },
            JobEventRecord.CreateStateChanged(definition.JobId, JobState.PREPARING, JobState.IMPLEMENTING, createdAt.AddMinutes(2)) with { Sequence = 5 },
        ]);

        Assert.Equal(ExecutionStatus.Idle, completed.Execution.Status);
        Assert.Equal(JobState.IMPLEMENTING, completed.State);
        Assert.Equal(1, completed.Attempts.Preparing);
    }

    [Fact]
    public void RebuildCapturesGitSnapshotFromGitStateCapturedEvents()
    {
        DateTimeOffset createdAt = new(2026, 3, 15, 16, 45, 0, TimeSpan.Zero);
        JobDefinition definition = new()
        {
            JobId = "job-20260315-7f3c9b2a",
            Title = "Add retry handling",
            Description = "Implement bounded retries for retryable failures.",
            Repo = new JobRepositoryTarget
            {
                BaseBranch = "master",
                VerificationProfile = "default",
            },
            CreatedAtUtc = createdAt,
            CreatedBy = "tester",
        };
        JobStatusSnapshot initialStatus = JobStatusSnapshot.CreateInitial(definition);
        JobSnapshotRebuilder rebuilder = new();

        JobStatusSnapshot rebuilt = rebuilder.Rebuild(
        [
            JobEventRecord.CreateJobCreated(1, definition, initialStatus),
            JobEventRecord.CreateGitStateCaptured(
                definition.JobId,
                new JobGitSnapshot
                {
                    RepoRoot = "/tmp/sample",
                    BaseBranch = "master",
                    BaseCommit = "abc123",
                    JobBranch = "ringmaster/j-7f3c9b2a-retry",
                    WorktreePath = "/tmp/.ringmaster-worktrees/sample/j-7f3c9b2a",
                    HeadCommit = "abc123",
                    HasUncommittedChanges = true,
                    ChangedFiles = ["README.md"],
                },
                createdAt.AddMinutes(1)) with { Sequence = 2 },
        ]);

        Assert.NotNull(rebuilt.Git);
        Assert.Equal("ringmaster/j-7f3c9b2a-retry", rebuilt.Git.JobBranch);
        Assert.True(rebuilt.Git.HasUncommittedChanges);
        Assert.Equal(["README.md"], rebuilt.Git.ChangedFiles);
    }

    [Fact]
    public void RebuildTracksRepeatedFailuresWithoutChangingLifecycleState()
    {
        DateTimeOffset createdAt = new(2026, 3, 15, 16, 45, 0, TimeSpan.Zero);
        JobDefinition definition = new()
        {
            JobId = "job-20260315-7f3c9b2a",
            Title = "Add retry handling",
            Description = "Implement bounded retries for retryable failures.",
            Repo = new JobRepositoryTarget
            {
                BaseBranch = "master",
                VerificationProfile = "default",
            },
            CreatedAtUtc = createdAt,
            CreatedBy = "tester",
        };
        JobStatusSnapshot initialStatus = JobStatusSnapshot.CreateInitial(definition);
        JobSnapshotRebuilder rebuilder = new();

        JobStatusSnapshot rebuilt = rebuilder.Rebuild(
        [
            JobEventRecord.CreateJobCreated(1, definition, initialStatus),
            JobEventRecord.CreateFailureRecorded(definition.JobId, FailureCategory.RepairableCodeFailure, "verify:tests:RetryTests.Should_retry", "Test failure in RetryTests.Should_retry.", createdAt.AddMinutes(1)) with { Sequence = 2 },
            JobEventRecord.CreateFailureRecorded(definition.JobId, FailureCategory.RepairableCodeFailure, "verify:tests:RetryTests.Should_retry", "Test failure in RetryTests.Should_retry.", createdAt.AddMinutes(2)) with { Sequence = 3 },
        ]);

        Assert.Equal(JobState.QUEUED, rebuilt.State);
        Assert.NotNull(rebuilt.LastFailure);
        Assert.Equal(2, rebuilt.LastFailure.RepetitionCount);
        Assert.Equal(createdAt.AddMinutes(1), rebuilt.LastFailure.FirstSeenAtUtc);
        Assert.Equal(createdAt.AddMinutes(2), rebuilt.LastFailure.LastSeenAtUtc);
    }

    [Fact]
    public void RebuildUpdatesReviewSnapshotFromReviewRecordedEvents()
    {
        DateTimeOffset createdAt = new(2026, 3, 15, 16, 45, 0, TimeSpan.Zero);
        JobDefinition definition = new()
        {
            JobId = "job-20260315-7f3c9b2a",
            Title = "Add retry handling",
            Description = "Implement bounded retries for retryable failures.",
            Repo = new JobRepositoryTarget
            {
                BaseBranch = "master",
                VerificationProfile = "default",
            },
            CreatedAtUtc = createdAt,
            CreatedBy = "tester",
        };
        JobStatusSnapshot initialStatus = JobStatusSnapshot.CreateInitial(definition);
        JobSnapshotRebuilder rebuilder = new();

        JobStatusSnapshot rebuilt = rebuilder.Rebuild(
        [
            JobEventRecord.CreateJobCreated(1, definition, initialStatus),
            JobEventRecord.CreateReviewRecorded(definition.JobId, ReviewVerdict.RequestRepair, ReviewRisk.Medium, "Need one more test.", createdAt.AddMinutes(1)) with { Sequence = 2 },
        ]);

        Assert.Equal(ReviewVerdict.RequestRepair, rebuilt.Review.Verdict);
        Assert.Equal(ReviewRisk.Medium, rebuilt.Review.Risk);
    }

    [Fact]
    public void RebuildUpdatesPullRequestSnapshotFromPullRequestRecordedEvents()
    {
        DateTimeOffset createdAt = new(2026, 3, 15, 16, 45, 0, TimeSpan.Zero);
        JobDefinition definition = new()
        {
            JobId = "job-20260315-7f3c9b2a",
            Title = "Add retry handling",
            Description = "Implement bounded retries for retryable failures.",
            Repo = new JobRepositoryTarget
            {
                BaseBranch = "master",
                VerificationProfile = "default",
            },
            CreatedAtUtc = createdAt,
            CreatedBy = "tester",
        };
        JobStatusSnapshot initialStatus = JobStatusSnapshot.CreateInitial(definition);
        JobSnapshotRebuilder rebuilder = new();

        JobStatusSnapshot rebuilt = rebuilder.Rebuild(
        [
            JobEventRecord.CreateJobCreated(1, definition, initialStatus),
            JobEventRecord.CreatePullRequestRecorded(
                definition.JobId,
                PullRequestStatus.Draft,
                "https://example.test/pr/1",
                draft: true,
                summary: "Created a draft PR.",
                createdAt.AddMinutes(1)) with { Sequence = 2 },
        ]);

        Assert.Equal(PullRequestStatus.Draft, rebuilt.Pr.Status);
        Assert.Equal("https://example.test/pr/1", rebuilt.Pr.Url);
        Assert.True(rebuilt.Pr.Draft);
    }

    [Fact]
    public void RebuildClearsBlockersWhenTheJobFails()
    {
        DateTimeOffset createdAt = new(2026, 3, 15, 16, 45, 0, TimeSpan.Zero);
        JobDefinition definition = new()
        {
            JobId = "job-20260315-7f3c9b2a",
            Title = "Add retry handling",
            Description = "Implement bounded retries for retryable failures.",
            Repo = new JobRepositoryTarget
            {
                BaseBranch = "master",
                VerificationProfile = "default",
            },
            CreatedAtUtc = createdAt,
            CreatedBy = "tester",
        };
        JobStatusSnapshot initialStatus = JobStatusSnapshot.CreateInitial(definition);
        JobSnapshotRebuilder rebuilder = new();

        JobStatusSnapshot rebuilt = rebuilder.Rebuild(
        [
            JobEventRecord.CreateJobCreated(1, definition, initialStatus),
            JobEventRecord.CreateBlocked(
                definition.JobId,
                new BlockerInfo
                {
                    ReasonCode = BlockerReasonCode.MissingConfiguration,
                    Summary = "Missing repo config.",
                    Questions = ["Create ringmaster.json?"],
                    ResumeState = JobState.VERIFYING,
                },
                createdAt.AddMinutes(1)) with { Sequence = 2 },
            JobEventRecord.CreateFailed(
                definition.JobId,
                FailureCategory.HumanEscalationRequired,
                "operator:cancelled",
                "Canceled by operator.",
                createdAt.AddMinutes(2)) with { Sequence = 3 },
        ]);

        Assert.Equal(JobState.FAILED, rebuilt.State);
        Assert.Null(rebuilt.Blocker);
    }
}
