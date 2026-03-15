namespace Ringmaster.Core.Jobs;

public sealed record class JobDefinition
{
    public int SchemaVersion { get; init; } = ProductInfo.SchemaVersion;
    public required string JobId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];
    public JobConstraints Constraints { get; init; } = new();
    public required JobRepositoryTarget Repo { get; init; }
    public JobPullRequestOptions Pr { get; init; } = new();
    public int Priority { get; init; } = 50;
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string CreatedBy { get; init; }
}

public sealed record class JobConstraints
{
    public IReadOnlyList<string> AllowedPaths { get; init; } = [];
    public IReadOnlyList<string> ForbiddenPaths { get; init; } = [];
    public int MaxFilesChangedSoft { get; init; } = 0;
}

public sealed record class JobRepositoryTarget
{
    public required string BaseBranch { get; init; }
    public required string VerificationProfile { get; init; }
}

public sealed record class JobPullRequestOptions
{
    public bool AutoOpen { get; init; }
    public bool DraftByDefault { get; init; } = true;
    public IReadOnlyList<string> Labels { get; init; } = [];
}
