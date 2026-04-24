namespace Ringmaster.Core.Jobs;

public interface IResourceGate : IDisposable
{
    bool TryAcquire(ResourceClass resourceClass);
    void Release(ResourceClass resourceClass);
}
