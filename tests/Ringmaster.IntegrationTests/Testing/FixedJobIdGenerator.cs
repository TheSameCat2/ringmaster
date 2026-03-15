using Ringmaster.Core.Jobs;

namespace Ringmaster.IntegrationTests.Testing;

internal sealed class FixedJobIdGenerator(string jobId) : IJobIdGenerator
{
    public string CreateId(DateTimeOffset timestampUtc)
    {
        return jobId;
    }
}
