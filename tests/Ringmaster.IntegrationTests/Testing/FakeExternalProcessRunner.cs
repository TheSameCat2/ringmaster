using Ringmaster.Infrastructure.Processes;

namespace Ringmaster.IntegrationTests.Testing;

internal sealed class FakeExternalProcessRunner(
    Func<ExternalProcessSpec, CancellationToken, Task<ExternalProcessResult>> handler) : IExternalProcessRunner
{
    public Task<ExternalProcessResult> RunAsync(ExternalProcessSpec spec, CancellationToken cancellationToken)
    {
        return handler(spec, cancellationToken);
    }
}
