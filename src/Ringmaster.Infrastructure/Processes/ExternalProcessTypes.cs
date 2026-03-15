namespace Ringmaster.Infrastructure.Processes;

public sealed record class ExternalProcessSpec
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public required string WorkingDirectory { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(30);
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);
    public string? StdoutPath { get; init; }
    public string? StderrPath { get; init; }
}

public sealed record class ExternalProcessResult
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public required string WorkingDirectory { get; init; }
    public IReadOnlyList<string> EnvironmentVariableNames { get; init; } = [];
    public required TimeSpan Timeout { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required int ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public required string Stdout { get; init; }
    public required string Stderr { get; init; }
    public string? StdoutPath { get; init; }
    public string? StderrPath { get; init; }
    public int ProcessId { get; init; }
}
