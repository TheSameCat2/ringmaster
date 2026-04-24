using System.Text.Json;
using Ringmaster.Infrastructure.Processes;

namespace Ringmaster.Codex;

public sealed class CodexExecRunner(IExternalProcessRunner processRunner) : ICodexRunner
{
    public async Task<CodexExecResult> ExecuteAsync(CodexExecRequest request, CancellationToken cancellationToken)
    {
        List<string> arguments = BuildArguments(request);

        ExternalProcessResult processResult = await processRunner.RunAsync(
            new ExternalProcessSpec
            {
                FileName = "codex",
                Arguments = arguments,
                WorkingDirectory = request.WorkingDirectory,
                StandardInputText = request.PromptText,
                StdoutPath = request.EventLogPath,
                StderrPath = request.StderrPath,
                Timeout = TimeSpan.FromHours(1),
            },
            cancellationToken);

        string? finalOutputText = File.Exists(request.OutputLastMessagePath)
            ? await File.ReadAllTextAsync(request.OutputLastMessagePath, cancellationToken)
            : null;

        return new CodexExecResult
        {
            ExitCode = processResult.ExitCode,
            TimedOut = processResult.TimedOut,
            SessionId = ExtractSessionId(processResult.Stdout),
            FinalOutputText = finalOutputText,
        };
    }

    internal static List<string> BuildArguments(CodexExecRequest request)
    {
        List<string> arguments = ["--ask-for-approval", "never", "exec", "--json", "--cd", request.WorkingDirectory];

        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            arguments.Add("--model");
            arguments.Add(request.Model);
        }

        if (request.SkipGitRepoCheck)
        {
            arguments.Add("--skip-git-repo-check");
        }

        foreach (string writableDirectory in request.AdditionalWritableDirectories)
        {
            arguments.Add("--add-dir");
            arguments.Add(writableDirectory);
        }

        arguments.Add("--sandbox");
        arguments.Add(request.SandboxMode switch
        {
            AgentSandboxMode.ReadOnly => "read-only",
            AgentSandboxMode.WorkspaceWrite => "workspace-write",
            _ => throw new InvalidOperationException($"Unhandled sandbox mode '{request.SandboxMode}'."),
        });
        arguments.Add("--output-schema");
        arguments.Add(request.OutputSchemaPath);
        arguments.Add("--output-last-message");
        arguments.Add(request.OutputLastMessagePath);
        arguments.Add("-");
        return arguments;
    }

    private static string? ExtractSessionId(string jsonl)
    {
        foreach (string line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                if (TryFindString(document.RootElement, "thread_id", out string? threadId))
                {
                    return threadId;
                }
            }
            catch (JsonException)
            {
                // Ignore malformed lines in the event stream and keep scanning for the session id.
            }
        }

        return null;
    }

    private static bool TryFindString(JsonElement element, string propertyName, out string? value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    value = property.Value.GetString();
                    return true;
                }

                if (TryFindString(property.Value, propertyName, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                if (TryFindString(child, propertyName, out value))
                {
                    return true;
                }
            }
        }

        value = null;
        return false;
    }
}
