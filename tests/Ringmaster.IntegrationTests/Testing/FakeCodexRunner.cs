using Ringmaster.Codex;

namespace Ringmaster.IntegrationTests.Testing;

internal sealed class FakeCodexRunner(
    Func<CodexExecRequest, CancellationToken, Task<CodexExecResult>> handler) : ICodexRunner
{
    public Task<CodexExecResult> ExecuteAsync(CodexExecRequest request, CancellationToken cancellationToken)
    {
        return handler(request, cancellationToken);
    }
}
