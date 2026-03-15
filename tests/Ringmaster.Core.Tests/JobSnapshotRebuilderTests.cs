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
}
