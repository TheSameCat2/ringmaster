namespace Ringmaster.IntegrationTests.Testing;

internal sealed class StaticTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }
}
