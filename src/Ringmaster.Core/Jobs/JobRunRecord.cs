namespace Ringmaster.Core.Jobs;

public sealed record class JobRunRecord
{
    public int SchemaVersion { get; init; } = ProductInfo.SchemaVersion;
    public required string RunId { get; init; }
    public required string JobId { get; init; }
    public required JobStage Stage { get; init; }
    public required StageRole Role { get; init; }
    public int Attempt { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public required string Tool { get; init; }
    public IReadOnlyList<string> Command { get; init; } = [];
    public string? SessionId { get; init; }
    public int? ExitCode { get; init; }
    public RunResult? Result { get; init; }
    public RunArtifacts Artifacts { get; init; } = new();
}

public sealed record class RunArtifacts
{
    public string? Prompt { get; init; }
    public string? Schema { get; init; }
    public string? FinalOutput { get; init; }
    public string? EventLog { get; init; }
    public string? Stderr { get; init; }
}
