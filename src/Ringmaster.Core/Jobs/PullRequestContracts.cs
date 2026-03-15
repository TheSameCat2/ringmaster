namespace Ringmaster.Core.Jobs;

public interface IPullRequestService
{
    Task<PullRequestOperationResult> PublishAsync(string jobId, CancellationToken cancellationToken);
    Task<PullRequestOperationResult> PublishIfConfiguredAsync(string jobId, CancellationToken cancellationToken);
}

public interface IPullRequestProvider
{
    Task<PullRequestProviderResult> OpenOrGetAsync(PullRequestPublicationRequest request, CancellationToken cancellationToken);
}

public sealed record class PullRequestOperationResult
{
    public required string JobId { get; init; }
    public required JobStatusSnapshot Status { get; init; }
    public PullRequestStatus PullRequestStatus { get; init; } = PullRequestStatus.NotStarted;
    public string? Url { get; init; }
    public bool Attempted { get; init; }
    public bool Published { get; init; }
    public bool Created { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public sealed record class PullRequestPublicationRequest
{
    public required string RepositoryRoot { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string HeadBranch { get; init; }
    public required string BaseBranch { get; init; }
    public required string Title { get; init; }
    public required string BodyPath { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = [];
    public bool Draft { get; init; }
}

public sealed record class PullRequestProviderResult
{
    public required PullRequestStatus Status { get; init; }
    public string? Url { get; init; }
    public bool Draft { get; init; }
    public bool Created { get; init; }
    public string Summary { get; init; } = string.Empty;
}
