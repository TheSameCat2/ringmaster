namespace Ringmaster.Core.Jobs;

public sealed record class ResourceGateLimits
{
    public int MaxCodexRuns { get; init; } = 1;
    public int MaxVerificationRuns { get; init; } = 1;
    public int MaxPrOperations { get; init; } = 1;
}
