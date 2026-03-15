using Ringmaster.Abstractions.Jobs;
using Ringmaster.Core.Jobs;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.IntegrationTests.Testing;

namespace Ringmaster.IntegrationTests;

public sealed class LocalFilesystemJobRepositoryTests
{
    [Fact]
    public async Task CreateAsyncWritesTheExpectedJobLayout()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DateTimeOffset createdAt = new(2026, 3, 15, 16, 45, 0, TimeSpan.Zero);
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path, createdAt);

        StoredJob storedJob = await repository.CreateAsync(
            new JobCreateRequest
            {
                Title = "Add retry handling",
                Description = "Implement bounded retries for retryable failures.",
                AcceptanceCriteria = ["Adds tests"],
                CreatedBy = "tester",
            },
            CancellationToken.None);

        string jobRoot = storedJob.JobDirectoryPath;

        Assert.Equal("job-20260315-7f3c9b2a", storedJob.Definition.JobId);
        Assert.True(File.Exists(System.IO.Path.Combine(jobRoot, "JOB.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(jobRoot, "JOB.md")));
        Assert.True(File.Exists(System.IO.Path.Combine(jobRoot, "STATUS.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(jobRoot, "PLAN.md")));
        Assert.True(File.Exists(System.IO.Path.Combine(jobRoot, "NOTES.md")));
        Assert.True(File.Exists(System.IO.Path.Combine(jobRoot, "REVIEW.md")));
        Assert.True(File.Exists(System.IO.Path.Combine(jobRoot, "PR.md")));
        Assert.True(File.Exists(System.IO.Path.Combine(jobRoot, "events", "events.jsonl")));
        Assert.True(Directory.Exists(System.IO.Path.Combine(jobRoot, "runs")));
        Assert.True(Directory.Exists(System.IO.Path.Combine(jobRoot, "artifacts")));
        Assert.True(Directory.Exists(System.IO.Path.Combine(jobRoot, "locks")));

        string eventLog = await File.ReadAllTextAsync(System.IO.Path.Combine(jobRoot, "events", "events.jsonl"));
        Assert.Contains("\"type\":\"JobCreated\"", eventLog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RebuildStatusAsyncRestoresMissingSnapshotFromEvents()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DateTimeOffset createdAt = new(2026, 3, 15, 16, 45, 0, TimeSpan.Zero);
        LocalFilesystemJobRepository repository = CreateRepository(temporaryDirectory.Path, createdAt);

        StoredJob storedJob = await repository.CreateAsync(
            new JobCreateRequest
            {
                Title = "Add retry handling",
                Description = "Implement bounded retries for retryable failures.",
                CreatedBy = "tester",
            },
            CancellationToken.None);

        string statusPath = System.IO.Path.Combine(storedJob.JobDirectoryPath, "STATUS.json");
        File.Delete(statusPath);

        JobStatusSnapshot rebuilt = await repository.RebuildStatusAsync(storedJob.Definition.JobId, CancellationToken.None);

        Assert.True(File.Exists(statusPath));
        Assert.Equal(JobState.QUEUED, rebuilt.State);
        Assert.Equal(storedJob.Definition.Title, rebuilt.Title);
    }

    [Fact]
    public async Task AtomicWriterOverwritesFileWithoutLeavingTemporaryArtifacts()
    {
        using TemporaryDirectory temporaryDirectory = new();
        AtomicFileWriter writer = new();
        string targetPath = System.IO.Path.Combine(temporaryDirectory.Path, "state.json");

        await writer.WriteTextAsync(targetPath, "first", CancellationToken.None);
        await writer.WriteTextAsync(targetPath, "second", CancellationToken.None);

        string content = await File.ReadAllTextAsync(targetPath);
        string[] tempFiles = Directory.GetFiles(temporaryDirectory.Path, "*.tmp");

        Assert.Equal("second", content);
        Assert.Empty(tempFiles);
    }

    private static LocalFilesystemJobRepository CreateRepository(string repositoryRoot, DateTimeOffset createdAt)
    {
        return new LocalFilesystemJobRepository(
            repositoryRoot,
            new StaticTimeProvider(createdAt),
            new FixedJobIdGenerator("job-20260315-7f3c9b2a"),
            new AtomicFileWriter(),
            new JobEventLogStore(),
            new JobSnapshotRebuilder());
    }
}
