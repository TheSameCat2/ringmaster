using Ringmaster.Abstractions.Jobs;

namespace Ringmaster.Infrastructure.Persistence;

public sealed class DefaultJobIdGenerator : IJobIdGenerator
{
    public string CreateId(DateTimeOffset timestampUtc)
    {
        return $"job-{timestampUtc:yyyyMMdd}-{Guid.NewGuid():N}"[..22];
    }
}
