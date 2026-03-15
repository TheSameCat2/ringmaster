using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;

namespace Ringmaster.Infrastructure.Persistence;

public sealed class FileLeaseManager(
    string repositoryRoot,
    AtomicFileWriter atomicFileWriter,
    TimeProvider timeProvider) : ILeaseManager
{
    private readonly string _repositoryRoot = Path.GetFullPath(repositoryRoot);

    public Task<LeaseRecord?> ReadJobLeaseAsync(StoredJob job, CancellationToken cancellationToken)
    {
        return ReadLeaseAsync(Path.Combine(job.JobDirectoryPath, "locks", "lease.json"), cancellationToken);
    }

    public Task<ILeaseHandle?> TryAcquireJobLeaseAsync(StoredJob job, string ownerId, CancellationToken cancellationToken)
    {
        string locksDirectory = Path.Combine(job.JobDirectoryPath, "locks");
        return TryAcquireAsync(
            Path.Combine(locksDirectory, "job.lock"),
            Path.Combine(locksDirectory, "lease.json"),
            ownerId,
            cancellationToken);
    }

    public Task<ILeaseHandle?> TryAcquireRepoMutationLeaseAsync(string ownerId, CancellationToken cancellationToken)
    {
        string runtimeRoot = RingmasterPaths.RuntimeRoot(_repositoryRoot);
        return TryAcquireAsync(
            Path.Combine(runtimeRoot, "repo-mutation.lock"),
            Path.Combine(runtimeRoot, "repo-mutation.json"),
            ownerId,
            cancellationToken);
    }

    public Task<ILeaseHandle?> TryAcquireSchedulerLeaseAsync(string ownerId, CancellationToken cancellationToken)
    {
        string runtimeRoot = RingmasterPaths.RuntimeRoot(_repositoryRoot);
        return TryAcquireAsync(
            Path.Combine(runtimeRoot, "scheduler.lock"),
            Path.Combine(runtimeRoot, "scheduler.json"),
            ownerId,
            cancellationToken);
    }

    private async Task<ILeaseHandle?> TryAcquireAsync(
        string lockPath,
        string leasePath,
        string ownerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath) ?? throw new InvalidOperationException($"Lock path '{lockPath}' does not have a parent directory."));

        try
        {
            FileStream stream = new(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.Asynchronous);

            FileLeaseHandle handle = new(
                stream,
                leasePath,
                ownerId,
                atomicFileWriter,
                timeProvider);
            await handle.RenewAsync(runId: null, cancellationToken);
            return handle;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task<LeaseRecord?> ReadLeaseAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string json = await File.ReadAllTextAsync(path, cancellationToken);
        return RingmasterJsonSerializer.Deserialize<LeaseRecord>(json);
    }

    private sealed class FileLeaseHandle(
        FileStream stream,
        string leasePath,
        string ownerId,
        AtomicFileWriter atomicFileWriter,
        TimeProvider timeProvider) : ILeaseHandle
    {
        private readonly DateTimeOffset _acquiredAtUtc = timeProvider.GetUtcNow();
        private readonly string _ownerId = ownerId;

        public string OwnerId => _ownerId;

        public async Task RenewAsync(string? runId, CancellationToken cancellationToken)
        {
            await atomicFileWriter.WriteJsonAsync(
                leasePath,
                new LeaseRecord
                {
                    OwnerId = _ownerId,
                    AcquiredAtUtc = _acquiredAtUtc,
                    HeartbeatAtUtc = timeProvider.GetUtcNow(),
                    ProcessId = Environment.ProcessId,
                    MachineName = Environment.MachineName,
                    RunId = runId,
                },
                cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            stream.Dispose();
            if (File.Exists(leasePath))
            {
                File.Delete(leasePath);
            }

            await ValueTask.CompletedTask;
        }
    }
}
