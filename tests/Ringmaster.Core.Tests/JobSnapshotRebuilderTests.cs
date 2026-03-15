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
}
