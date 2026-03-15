using Ringmaster.Core.Jobs;
using Ringmaster.Infrastructure.Persistence;

namespace Ringmaster.Codex;

public sealed class CodexAgentRunner(
    ICodexRunner codexRunner,
    AtomicFileWriter atomicFileWriter) : IAgentRunner
{
    public async Task<AgentExecutionResult> RunAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        string promptPath = Path.Combine(request.RunDirectoryPath, "prompt.md");
        string outputSchemaPath = Path.Combine(request.RunDirectoryPath, "output-schema.json");
        string finalOutputPath = Path.Combine(request.RunDirectoryPath, "final-output.json");
        string eventLogPath = Path.Combine(request.RunDirectoryPath, "codex.events.jsonl");
        string stderrPath = Path.Combine(request.RunDirectoryPath, "stderr.log");

        await atomicFileWriter.WriteTextAsync(promptPath, request.PromptText, cancellationToken);
        await atomicFileWriter.WriteTextAsync(outputSchemaPath, request.OutputSchemaJson, cancellationToken);

        CodexExecResult result = await codexRunner.ExecuteAsync(
            new CodexExecRequest
            {
                Kind = request.Kind,
                WorkingDirectory = request.WorkingDirectory,
                AdditionalWritableDirectories = request.AdditionalWritableDirectories,
                PromptText = request.PromptText,
                OutputSchemaPath = outputSchemaPath,
                OutputLastMessagePath = finalOutputPath,
                EventLogPath = eventLogPath,
                StderrPath = stderrPath,
                SandboxMode = request.SandboxMode,
                Model = request.Model,
                SkipGitRepoCheck = request.SkipGitRepoCheck,
            },
            cancellationToken);

        return new AgentExecutionResult
        {
            ExitCode = result.ExitCode,
            SessionId = result.SessionId,
            FinalOutputText = result.FinalOutputText,
            Artifacts = new RunArtifacts
            {
                Prompt = Path.GetFileName(promptPath),
                Schema = Path.GetFileName(outputSchemaPath),
                FinalOutput = Path.GetFileName(finalOutputPath),
                EventLog = Path.GetFileName(eventLogPath),
                Stderr = Path.GetFileName(stderrPath),
            },
        };
    }
}
