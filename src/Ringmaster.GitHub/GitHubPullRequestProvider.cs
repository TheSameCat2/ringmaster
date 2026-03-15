using System.Text.Json;
using Ringmaster.Core.Jobs;
using Ringmaster.Infrastructure.Processes;

namespace Ringmaster.GitHub;

public sealed class GitHubPullRequestProvider(IExternalProcessRunner processRunner) : IPullRequestProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<PullRequestProviderResult> OpenOrGetAsync(
        PullRequestPublicationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ExistingPullRequestRecord? existing = await FindExistingAsync(request, cancellationToken);
        if (existing is not null)
        {
            PullRequestStatus existingStatus = MapStatus(existing.State, existing.IsDraft);
            return new PullRequestProviderResult
            {
                Status = existingStatus,
                Url = existing.Url,
                Draft = existing.IsDraft,
                Created = false,
                Summary = $"Found existing PR in state {existingStatus}.",
            };
        }

        List<string> createArguments =
        [
            "pr",
            "create",
            "--head",
            request.HeadBranch,
            "--base",
            request.BaseBranch,
            "--title",
            request.Title,
            "--body-file",
            request.BodyPath,
        ];

        if (request.Draft)
        {
            createArguments.Add("--draft");
        }

        foreach (string label in request.Labels)
        {
            createArguments.Add("--label");
            createArguments.Add(label);
        }

        ExternalProcessResult createResult = await RunGhAsync(
            request.WorkingDirectory,
            createArguments,
            TimeSpan.FromMinutes(2),
            cancellationToken);
        string url = createResult.Stdout.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            existing = await FindExistingAsync(request, cancellationToken);
            url = existing?.Url ?? string.Empty;
        }

        return new PullRequestProviderResult
        {
            Status = request.Draft ? PullRequestStatus.Draft : PullRequestStatus.Open,
            Url = string.IsNullOrWhiteSpace(url) ? null : url,
            Draft = request.Draft,
            Created = true,
            Summary = "Created a pull request via GitHub CLI.",
        };
    }

    private async Task<ExistingPullRequestRecord?> FindExistingAsync(
        PullRequestPublicationRequest request,
        CancellationToken cancellationToken)
    {
        ExternalProcessResult result = await RunGhAsync(
            request.WorkingDirectory,
            [
                "pr",
                "list",
                "--state",
                "all",
                "--head",
                request.HeadBranch,
                "--base",
                request.BaseBranch,
                "--limit",
                "1",
                "--json",
                "url,isDraft,state",
            ],
            TimeSpan.FromMinutes(1),
            cancellationToken);

        ExistingPullRequestRecord[] records = JsonSerializer.Deserialize<ExistingPullRequestRecord[]>(result.Stdout, JsonOptions)
            ?? [];
        return records.FirstOrDefault();
    }

    private async Task<ExternalProcessResult> RunGhAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ExternalProcessResult result = await processRunner.RunAsync(
            new ExternalProcessSpec
            {
                FileName = "gh",
                WorkingDirectory = workingDirectory,
                Arguments = arguments,
                Timeout = timeout,
            },
            cancellationToken);

        if (result.TimedOut || result.ExitCode != 0)
        {
            throw new PullRequestProviderException($"gh {string.Join(' ', arguments)} failed.", result);
        }

        return result;
    }

    private static PullRequestStatus MapStatus(string? state, bool isDraft)
    {
        return state?.ToUpperInvariant() switch
        {
            "OPEN" when isDraft => PullRequestStatus.Draft,
            "OPEN" => PullRequestStatus.Open,
            "MERGED" => PullRequestStatus.Merged,
            "CLOSED" => PullRequestStatus.Closed,
            _ => PullRequestStatus.Open,
        };
    }

    private sealed record class ExistingPullRequestRecord
    {
        public string? Url { get; init; }
        public bool IsDraft { get; init; }
        public string? State { get; init; }
    }
}

public sealed class PullRequestProviderException(string message, ExternalProcessResult processResult) : Exception(message)
{
    public ExternalProcessResult ProcessResult { get; } = processResult;
}
