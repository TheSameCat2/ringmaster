using Ringmaster.Abstractions.Jobs;
using Ringmaster.Core;
using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;

namespace Ringmaster.Infrastructure.Persistence;

public sealed class LocalFilesystemJobRepository(
    string repositoryRoot,
    TimeProvider timeProvider,
    IJobIdGenerator jobIdGenerator,
    AtomicFileWriter atomicFileWriter,
    JobEventLogStore jobEventLogStore,
    JobSnapshotRebuilder snapshotRebuilder) : IJobRepository
{
    private readonly string _repositoryRoot = Path.GetFullPath(repositoryRoot);

    public async Task<StoredJob> CreateAsync(JobCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Description);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CreatedBy);

        DateTimeOffset timestamp = timeProvider.GetUtcNow();
        string jobId = jobIdGenerator.CreateId(timestamp);
        string jobRoot = RingmasterPaths.JobRoot(_repositoryRoot, jobId);

        if (Directory.Exists(jobRoot))
        {
            throw new IOException($"A job already exists at '{jobRoot}'.");
        }

        CreateJobLayout(jobRoot);

        JobDefinition definition = new()
        {
            JobId = jobId,
            Title = request.Title,
            Description = request.Description,
            AcceptanceCriteria = request.AcceptanceCriteria,
            Constraints = new JobConstraints
            {
                AllowedPaths = request.AllowedPaths,
                ForbiddenPaths = request.ForbiddenPaths,
                MaxFilesChangedSoft = request.MaxFilesChangedSoft,
            },
            Repo = new JobRepositoryTarget
            {
                BaseBranch = request.BaseBranch,
                VerificationProfile = request.VerificationProfile,
            },
            Pr = new JobPullRequestOptions
            {
                AutoOpen = request.AutoOpenPullRequest,
                DraftByDefault = request.DraftPullRequest,
                Labels = request.PullRequestLabels,
            },
            Priority = request.Priority,
            CreatedAtUtc = timestamp,
            CreatedBy = request.CreatedBy,
        };

        string jobMarkdown = string.IsNullOrWhiteSpace(request.JobMarkdown)
            ? BuildJobMarkdown(definition)
            : request.JobMarkdown;

        JobStatusSnapshot status = JobStatusSnapshot.CreateInitial(definition);
        JobEventRecord createdEvent = JobEventRecord.CreateJobCreated(1, definition, status);

        await atomicFileWriter.WriteJsonAsync(RingmasterPaths.JobDefinitionPath(_repositoryRoot, jobId), definition, cancellationToken);
        await atomicFileWriter.WriteTextAsync(RingmasterPaths.JobMarkdownPath(_repositoryRoot, jobId), jobMarkdown, cancellationToken);
        await WritePlaceholderFilesAsync(jobId, cancellationToken);
        await jobEventLogStore.AppendAsync(RingmasterPaths.EventLogPath(_repositoryRoot, jobId), createdEvent, cancellationToken);
        await atomicFileWriter.WriteJsonAsync(RingmasterPaths.StatusPath(_repositoryRoot, jobId), status, cancellationToken);

        return new StoredJob
        {
            JobDirectoryPath = jobRoot,
            Definition = definition,
            JobMarkdown = jobMarkdown,
            Status = status,
            Events = [createdEvent],
        };
    }

    public async Task<StoredJob?> GetAsync(string jobId, CancellationToken cancellationToken)
    {
        string jobRoot = RingmasterPaths.JobRoot(_repositoryRoot, jobId);
        if (!Directory.Exists(jobRoot))
        {
            return null;
        }

        string definitionJson = await File.ReadAllTextAsync(RingmasterPaths.JobDefinitionPath(_repositoryRoot, jobId), cancellationToken);
        JobDefinition definition = RingmasterJsonSerializer.Deserialize<JobDefinition>(definitionJson);
        string jobMarkdown = await File.ReadAllTextAsync(RingmasterPaths.JobMarkdownPath(_repositoryRoot, jobId), cancellationToken);
        IReadOnlyList<JobEventRecord> events = await jobEventLogStore.ReadAllAsync(RingmasterPaths.EventLogPath(_repositoryRoot, jobId), cancellationToken);
        JobStatusSnapshot status = await LoadStatusAsync(jobId, cancellationToken);

        return new StoredJob
        {
            JobDirectoryPath = jobRoot,
            Definition = definition,
            JobMarkdown = jobMarkdown,
            Status = status,
            Events = events,
        };
    }

    public async Task<IReadOnlyList<JobStatusListItem>> ListAsync(CancellationToken cancellationToken)
    {
        string jobsRoot = RingmasterPaths.JobsRoot(_repositoryRoot);
        if (!Directory.Exists(jobsRoot))
        {
            return [];
        }

        List<JobStatusListItem> jobs = [];

        foreach (string jobDirectory in Directory.EnumerateDirectories(jobsRoot))
        {
            string jobId = Path.GetFileName(jobDirectory);
            JobStatusSnapshot status = await LoadStatusAsync(jobId, cancellationToken);
            jobs.Add(new JobStatusListItem
            {
                JobId = status.JobId,
                Title = status.Title,
                State = status.State,
                Priority = status.Priority,
                UpdatedAtUtc = status.UpdatedAtUtc,
            });
        }

        return jobs
            .OrderByDescending(job => job.UpdatedAtUtc)
            .ThenBy(job => job.JobId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<JobStatusSnapshot> RebuildStatusAsync(string jobId, CancellationToken cancellationToken)
    {
        IReadOnlyList<JobEventRecord> events = await jobEventLogStore.ReadAllAsync(RingmasterPaths.EventLogPath(_repositoryRoot, jobId), cancellationToken);
        JobStatusSnapshot rebuilt = snapshotRebuilder.Rebuild(events);
        await atomicFileWriter.WriteJsonAsync(RingmasterPaths.StatusPath(_repositoryRoot, jobId), rebuilt, cancellationToken);
        return rebuilt;
    }

    private async Task<JobStatusSnapshot> LoadStatusAsync(string jobId, CancellationToken cancellationToken)
    {
        string statusPath = RingmasterPaths.StatusPath(_repositoryRoot, jobId);
        if (!File.Exists(statusPath))
        {
            return await RebuildStatusAsync(jobId, cancellationToken);
        }

        string statusJson = await File.ReadAllTextAsync(statusPath, cancellationToken);
        return RingmasterJsonSerializer.Deserialize<JobStatusSnapshot>(statusJson);
    }

    private static string BuildJobMarkdown(JobDefinition definition)
    {
        List<string> lines =
        [
            $"# {definition.Title}",
            string.Empty,
            definition.Description,
        ];

        if (definition.AcceptanceCriteria.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Acceptance Criteria");
            foreach (string acceptanceCriterion in definition.AcceptanceCriteria)
            {
                lines.Add($"- {acceptanceCriterion}");
            }
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static void CreateJobLayout(string jobRoot)
    {
        Directory.CreateDirectory(jobRoot);
        Directory.CreateDirectory(Path.Combine(jobRoot, "events"));
        Directory.CreateDirectory(Path.Combine(jobRoot, "runs"));
        Directory.CreateDirectory(Path.Combine(jobRoot, "artifacts"));
        Directory.CreateDirectory(Path.Combine(jobRoot, "locks"));
    }

    private async Task WritePlaceholderFilesAsync(string jobId, CancellationToken cancellationToken)
    {
        await atomicFileWriter.WriteTextAsync(RingmasterPaths.PlanPath(_repositoryRoot, jobId), "# Plan" + Environment.NewLine, cancellationToken);
        await atomicFileWriter.WriteTextAsync(RingmasterPaths.NotesPath(_repositoryRoot, jobId), "# Notes" + Environment.NewLine, cancellationToken);
        await atomicFileWriter.WriteTextAsync(RingmasterPaths.ReviewPath(_repositoryRoot, jobId), "# Review" + Environment.NewLine, cancellationToken);
        await atomicFileWriter.WriteTextAsync(RingmasterPaths.PullRequestPath(_repositoryRoot, jobId), "# Pull Request" + Environment.NewLine, cancellationToken);
    }
}

internal static class RingmasterPaths
{
    public static string RingmasterRoot(string repositoryRoot) => Path.Combine(repositoryRoot, ProductInfo.RuntimeDirectoryName);
    public static string JobsRoot(string repositoryRoot) => Path.Combine(RingmasterRoot(repositoryRoot), "jobs");
    public static string JobRoot(string repositoryRoot, string jobId) => Path.Combine(JobsRoot(repositoryRoot), jobId);
    public static string JobDefinitionPath(string repositoryRoot, string jobId) => Path.Combine(JobRoot(repositoryRoot, jobId), "JOB.json");
    public static string JobMarkdownPath(string repositoryRoot, string jobId) => Path.Combine(JobRoot(repositoryRoot, jobId), "JOB.md");
    public static string StatusPath(string repositoryRoot, string jobId) => Path.Combine(JobRoot(repositoryRoot, jobId), "STATUS.json");
    public static string PlanPath(string repositoryRoot, string jobId) => Path.Combine(JobRoot(repositoryRoot, jobId), "PLAN.md");
    public static string NotesPath(string repositoryRoot, string jobId) => Path.Combine(JobRoot(repositoryRoot, jobId), "NOTES.md");
    public static string ReviewPath(string repositoryRoot, string jobId) => Path.Combine(JobRoot(repositoryRoot, jobId), "REVIEW.md");
    public static string PullRequestPath(string repositoryRoot, string jobId) => Path.Combine(JobRoot(repositoryRoot, jobId), "PR.md");
    public static string EventLogPath(string repositoryRoot, string jobId) => Path.Combine(JobRoot(repositoryRoot, jobId), "events", "events.jsonl");
}
