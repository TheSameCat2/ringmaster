using Ringmaster.Core.Jobs;

namespace Ringmaster.Abstractions.Jobs;

public interface IJobRepository
{
    Task<StoredJob> CreateAsync(JobCreateRequest request, CancellationToken cancellationToken);
    Task<StoredJob?> GetAsync(string jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobStatusListItem>> ListAsync(CancellationToken cancellationToken);
    Task<JobStatusSnapshot> RebuildStatusAsync(string jobId, CancellationToken cancellationToken);
}

public interface IJobIdGenerator
{
    string CreateId(DateTimeOffset timestampUtc);
}

public sealed record class JobCreateRequest
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public string? JobMarkdown { get; init; }
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];
    public IReadOnlyList<string> AllowedPaths { get; init; } = [];
    public IReadOnlyList<string> ForbiddenPaths { get; init; } = [];
    public int MaxFilesChangedSoft { get; init; }
    public string BaseBranch { get; init; } = "master";
    public string VerificationProfile { get; init; } = "default";
    public bool AutoOpenPullRequest { get; init; }
    public bool DraftPullRequest { get; init; } = true;
    public IReadOnlyList<string> PullRequestLabels { get; init; } = [];
    public int Priority { get; init; } = 50;
    public required string CreatedBy { get; init; }
}

public sealed record class StoredJob
{
    public required string JobDirectoryPath { get; init; }
    public required JobDefinition Definition { get; init; }
    public required string JobMarkdown { get; init; }
    public required JobStatusSnapshot Status { get; init; }
    public IReadOnlyList<JobEventRecord> Events { get; init; } = [];
}

public sealed record class JobStatusListItem
{
    public required string JobId { get; init; }
    public required string Title { get; init; }
    public required JobState State { get; init; }
    public int Priority { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}
