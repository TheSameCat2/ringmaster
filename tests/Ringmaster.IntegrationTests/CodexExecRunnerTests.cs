using Ringmaster.Codex;
using Ringmaster.IntegrationTests.Testing;
using Ringmaster.Infrastructure.Processes;

namespace Ringmaster.IntegrationTests;

public sealed class CodexExecRunnerTests
{
    [Fact]
    public async Task ExecuteAsyncBuildsTheExpectedCommandLineAndCapturesTheSessionId()
    {
        using TemporaryDirectory temporaryDirectory = new();
        ExternalProcessSpec? capturedSpec = null;
        CodexExecRunner runner = new(
            new FakeExternalProcessRunner(async (spec, cancellationToken) =>
            {
                capturedSpec = spec;
                await File.WriteAllTextAsync(spec.StdoutPath!, "{\"type\":\"thread.started\",\"thread_id\":\"session-123\"}" + Environment.NewLine, cancellationToken);
                await File.WriteAllTextAsync(spec.StderrPath!, string.Empty, cancellationToken);
                await File.WriteAllTextAsync(
                    System.IO.Path.Combine(temporaryDirectory.Path, "final-output.json"),
                    "{\"result\":\"completed\"}",
                    cancellationToken);

                return new ExternalProcessResult
                {
                    FileName = spec.FileName,
                    Arguments = spec.Arguments,
                    WorkingDirectory = spec.WorkingDirectory,
                    EnvironmentVariableNames = [],
                    Timeout = spec.Timeout,
                    StartedAtUtc = new DateTimeOffset(2026, 3, 15, 19, 0, 0, TimeSpan.Zero),
                    CompletedAtUtc = new DateTimeOffset(2026, 3, 15, 19, 0, 1, TimeSpan.Zero),
                    ExitCode = 0,
                    Stdout = "{\"type\":\"thread.started\",\"thread_id\":\"session-123\"}" + Environment.NewLine,
                    Stderr = string.Empty,
                    StdoutPath = spec.StdoutPath,
                    StderrPath = spec.StderrPath,
                    ProcessId = 1234,
                };
            }));

        CodexExecResult result = await runner.ExecuteAsync(
            new CodexExecRequest
            {
                Kind = AgentRunKind.Implementer,
                WorkingDirectory = temporaryDirectory.Path,
                AdditionalWritableDirectories = [temporaryDirectory.Path],
                PromptText = "Implement the change.",
                OutputSchemaPath = System.IO.Path.Combine(temporaryDirectory.Path, "output-schema.json"),
                OutputLastMessagePath = System.IO.Path.Combine(temporaryDirectory.Path, "final-output.json"),
                EventLogPath = System.IO.Path.Combine(temporaryDirectory.Path, "codex.events.jsonl"),
                StderrPath = System.IO.Path.Combine(temporaryDirectory.Path, "stderr.log"),
                SandboxMode = AgentSandboxMode.WorkspaceWrite,
                SkipGitRepoCheck = true,
            },
            CancellationToken.None);

        Assert.NotNull(capturedSpec);
        Assert.Equal("codex", capturedSpec.FileName);
        Assert.Equal("Implement the change.", capturedSpec.StandardInputText);
        Assert.Contains("--ask-for-approval", capturedSpec.Arguments);
        Assert.Contains("never", capturedSpec.Arguments);
        Assert.Contains("exec", capturedSpec.Arguments);
        Assert.Contains("--json", capturedSpec.Arguments);
        Assert.Contains("--cd", capturedSpec.Arguments);
        Assert.Contains("--add-dir", capturedSpec.Arguments);
        Assert.Contains("--sandbox", capturedSpec.Arguments);
        Assert.Contains("workspace-write", capturedSpec.Arguments);
        Assert.Contains("--output-schema", capturedSpec.Arguments);
        Assert.Contains("--output-last-message", capturedSpec.Arguments);
        Assert.Contains("-", capturedSpec.Arguments);
        Assert.Equal("session-123", result.SessionId);
        Assert.Equal("{\"result\":\"completed\"}", result.FinalOutputText);
    }
}
