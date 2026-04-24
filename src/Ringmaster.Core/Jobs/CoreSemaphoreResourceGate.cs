using System.Collections.Concurrent;

namespace Ringmaster.Core.Jobs;

public sealed class CoreSemaphoreResourceGate : IResourceGate, IDisposable
{
    private readonly ConcurrentDictionary<ResourceClass, SemaphoreSlim> _semaphores;
    private bool _disposed;

    public CoreSemaphoreResourceGate(ResourceGateLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _semaphores = new ConcurrentDictionary<ResourceClass, SemaphoreSlim>();
        _semaphores.TryAdd(ResourceClass.Codex, new SemaphoreSlim(Math.Max(1, limits.MaxCodexRuns)));
        _semaphores.TryAdd(ResourceClass.Verification, new SemaphoreSlim(Math.Max(1, limits.MaxVerificationRuns)));
        _semaphores.TryAdd(ResourceClass.PullRequest, new SemaphoreSlim(Math.Max(1, limits.MaxPrOperations)));
    }

    public bool TryAcquire(ResourceClass resourceClass)
    {
        if (_disposed)
        {
            return false;
        }

        if (_semaphores.TryGetValue(resourceClass, out SemaphoreSlim? semaphore))
        {
            return semaphore.Wait(0);
        }

        return false;
    }

    public void Release(ResourceClass resourceClass)
    {
        if (_disposed)
        {
            return;
        }

        if (_semaphores.TryGetValue(resourceClass, out SemaphoreSlim? semaphore))
        {
            semaphore.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (SemaphoreSlim semaphore in _semaphores.Values)
        {
            semaphore.Dispose();
        }
    }
}
