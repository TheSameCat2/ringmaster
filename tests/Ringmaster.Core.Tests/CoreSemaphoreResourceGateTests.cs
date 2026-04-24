using Ringmaster.Core.Jobs;

namespace Ringmaster.Core.Tests;

public sealed class CoreSemaphoreResourceGateTests
{
    [Fact]
    public void TryAcquire_SucceedsWhenCapacityAvailable()
    {
        CoreSemaphoreResourceGate gate = new(new ResourceGateLimits
        {
            MaxCodexRuns = 2,
        });

        Assert.True(gate.TryAcquire(ResourceClass.Codex));
        Assert.True(gate.TryAcquire(ResourceClass.Codex));
        Assert.False(gate.TryAcquire(ResourceClass.Codex));

        gate.Dispose();
    }

    [Fact]
    public void Release_MakesSlotAvailableAgain()
    {
        CoreSemaphoreResourceGate gate = new(new ResourceGateLimits
        {
            MaxCodexRuns = 1,
        });

        Assert.True(gate.TryAcquire(ResourceClass.Codex));
        Assert.False(gate.TryAcquire(ResourceClass.Codex));
        gate.Release(ResourceClass.Codex);
        Assert.True(gate.TryAcquire(ResourceClass.Codex));

        gate.Dispose();
    }

    [Fact]
    public void DifferentResourceClasses_AreIndependent()
    {
        CoreSemaphoreResourceGate gate = new(new ResourceGateLimits
        {
            MaxCodexRuns = 1,
            MaxVerificationRuns = 1,
        });

        Assert.True(gate.TryAcquire(ResourceClass.Codex));
        Assert.True(gate.TryAcquire(ResourceClass.Verification));
        Assert.False(gate.TryAcquire(ResourceClass.Codex));
        Assert.False(gate.TryAcquire(ResourceClass.Verification));

        gate.Dispose();
    }

    [Fact]
    public void Limits_AreAtLeastOne()
    {
        CoreSemaphoreResourceGate gate = new(new ResourceGateLimits
        {
            MaxCodexRuns = 0,
            MaxVerificationRuns = -5,
        });

        Assert.True(gate.TryAcquire(ResourceClass.Codex));
        Assert.False(gate.TryAcquire(ResourceClass.Codex));
        Assert.True(gate.TryAcquire(ResourceClass.Verification));

        gate.Dispose();
    }

    [Fact]
    public void TryAcquire_ReturnsFalseAfterDispose()
    {
        CoreSemaphoreResourceGate gate = new(new ResourceGateLimits
        {
            MaxCodexRuns = 1,
        });

        gate.Dispose();
        Assert.False(gate.TryAcquire(ResourceClass.Codex));
    }

    [Fact]
    public void Release_IsSafeAfterDispose()
    {
        CoreSemaphoreResourceGate gate = new(new ResourceGateLimits
        {
            MaxCodexRuns = 1,
        });

        Assert.True(gate.TryAcquire(ResourceClass.Codex));
        gate.Dispose();
        gate.Release(ResourceClass.Codex); // Should not throw.
    }
}
