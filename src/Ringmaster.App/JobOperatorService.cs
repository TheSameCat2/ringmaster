using System.Text;
using Ringmaster.Core.Jobs;
using Ringmaster.Infrastructure.Persistence;

namespace Ringmaster.App;

public sealed class JobOperatorService(
    IJobRepository jobRepository,
    JobEngine jobEngine,
    IPullRequestService pullRequestService,
    IStateMachine stateMachine,
    AtomicFileWriter atomicFileWriter,
    TimeProvider timeProvider,
    RingmasterApplicationContext applicationContext)
{
    public async Task<JobActionResult> ResumeAsync(string jobId, CancellationToken cancellationToken)
    {
        StoredJob job = await LoadRequiredJobAsync(jobId, cancellationToken);

        return job.Status.State switch
        {
            JobState.BLOCKED => await ResumeBlockedAsync(job, cancellationToken),
            JobState.READY_FOR_PR => await PublishReadyPullRequestAsync(job, cancellationToken),
            JobState.QUEUED or JobState.PREPARING or JobState.IMPLEMENTING or JobState.VERIFYING or JobState.REPAIRING or JobState.REVIEWING
                => await ContinueExecutionAsync(job.Definition.JobId, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Job '{job.Definition.JobId}' is in state {job.Status.State} and cannot be resumed."),
        };
    }

    public async Task<JobActionResult> UnblockAsync(string jobId, string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException("A non-empty --message value is required to unblock a job.");
        }

        StoredJob job = await LoadRequiredJobAsync(jobId, cancellationToken);
        if (job.Status.State is not JobState.BLOCKED)
        {
            throw new InvalidOperationException(
                $"Job '{job.Definition.JobId}' is in state {job.Status.State} and cannot be unblocked.");
        }

        await AppendNotesEntryAsync(
            job,
            heading: "Human Input",
            body: BuildHumanInputBody(job, message),
            cancellationToken);

        JobActionResult resumed = await ResumeBlockedAsync(job, cancellationToken);
        return resumed with
        {
            NotesUpdated = true,
            Summary = $"Stored human input and resumed job. {resumed.Summary}",
        };
    }

    public async Task<JobActionResult> CancelAsync(string jobId, CancellationToken cancellationToken)
    {
        StoredJob job = await LoadRequiredJobAsync(jobId, cancellationToken);
        if (job.Status.Execution.Status is ExecutionStatus.Running)
        {
            throw new InvalidOperationException(
                $"Job '{job.Definition.JobId}' is actively running and cannot be canceled safely.");
        }

        if (job.Status.State is not (JobState.QUEUED or JobState.BLOCKED or JobState.READY_FOR_PR))
        {
            throw new InvalidOperationException(
                $"Job '{job.Definition.JobId}' is in state {job.Status.State} and cannot be canceled.");
        }

        string summary = $"Canceled by operator '{applicationContext.CurrentActor}'.";
        await AppendNotesEntryAsync(
            job,
            heading: "Operator Cancellation",
            body: $"{summary}{Environment.NewLine}{Environment.NewLine}Previous state: {job.Status.State}",
            cancellationToken);

        JobStatusSnapshot status = await jobRepository.AppendEventAsync(
            job.Definition.JobId,
            JobEventRecord.CreateFailed(
                job.Definition.JobId,
                FailureCategory.HumanEscalationRequired,
                "operator:cancelled",
                summary,
                timeProvider.GetUtcNow()),
            cancellationToken);

        return new JobActionResult
        {
            JobId = job.Definition.JobId,
            Status = status,
            Summary = summary,
            NotesUpdated = true,
        };
    }

    private async Task<JobActionResult> ResumeBlockedAsync(StoredJob job, CancellationToken cancellationToken)
    {
        JobState resumeState = job.Status.ResumeState;
        stateMachine.EnsureCanTransition(JobState.BLOCKED, resumeState);

        await jobRepository.AppendEventAsync(
            job.Definition.JobId,
            JobEventRecord.CreateStateChanged(
                job.Definition.JobId,
                JobState.BLOCKED,
                resumeState,
                timeProvider.GetUtcNow()),
            cancellationToken);

        return await ContinueExecutionAsync(job.Definition.JobId, cancellationToken);
    }

    private async Task<JobActionResult> PublishReadyPullRequestAsync(StoredJob job, CancellationToken cancellationToken)
    {
        PullRequestOperationResult result = await pullRequestService.PublishAsync(job.Definition.JobId, cancellationToken);
        return new JobActionResult
        {
            JobId = job.Definition.JobId,
            Status = result.Status,
            Summary = result.Summary,
            PullRequestAttempted = result.Attempted,
        };
    }

    private async Task<JobActionResult> ContinueExecutionAsync(string jobId, CancellationToken cancellationToken)
    {
        JobStatusSnapshot status = await jobEngine.ResumeAsync(jobId, cancellationToken);
        string summary = $"Job reached state {status.State}.";
        bool pullRequestAttempted = false;

        if (status.State is JobState.READY_FOR_PR)
        {
            PullRequestOperationResult publication = await pullRequestService.PublishIfConfiguredAsync(jobId, cancellationToken);
            status = publication.Status;
            pullRequestAttempted = publication.Attempted;
            summary = publication.Summary;
        }

        return new JobActionResult
        {
            JobId = jobId,
            Status = status,
            Summary = summary,
            PullRequestAttempted = pullRequestAttempted,
        };
    }

    private async Task<StoredJob> LoadRequiredJobAsync(string jobId, CancellationToken cancellationToken)
    {
        return await jobRepository.GetAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException($"Job '{jobId}' was not found.");
    }

    private async Task AppendNotesEntryAsync(
        StoredJob job,
        string heading,
        string body,
        CancellationToken cancellationToken)
    {
        string notesPath = Path.Combine(job.JobDirectoryPath, "NOTES.md");
        string existing = File.Exists(notesPath)
            ? await File.ReadAllTextAsync(notesPath, cancellationToken)
            : "# Notes" + Environment.NewLine;

        StringBuilder builder = new();
        builder.Append(existing);

        if (!existing.EndsWith('\n'))
        {
            builder.AppendLine();
        }

        if (!existing.EndsWith(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal)
            && !existing.EndsWith("\n\n", StringComparison.Ordinal))
        {
            builder.AppendLine();
        }

        builder.AppendLine($"## {heading}");
        builder.AppendLine($"Timestamp: {timeProvider.GetUtcNow():u}");
        builder.AppendLine($"Actor: {applicationContext.CurrentActor}");
        builder.AppendLine();
        builder.AppendLine(body.Trim());
        builder.AppendLine();

        await atomicFileWriter.WriteTextAsync(notesPath, builder.ToString(), cancellationToken);
    }

    private static string BuildHumanInputBody(StoredJob job, string message)
    {
        StringBuilder builder = new();
        builder.AppendLine($"Blocked state: {job.Status.State}");
        builder.AppendLine($"Resume state: {job.Status.ResumeState}");

        if (job.Status.Blocker is not null)
        {
            builder.AppendLine($"Reason: {job.Status.Blocker.ReasonCode}");
            builder.AppendLine($"Blocker summary: {job.Status.Blocker.Summary}");

            if (job.Status.Blocker.Questions.Count > 0)
            {
                builder.AppendLine("Outstanding questions:");
                foreach (string question in job.Status.Blocker.Questions)
                {
                    builder.AppendLine($"- {question}");
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("Operator answer:");
        builder.AppendLine(message.Trim());
        return builder.ToString().TrimEnd();
    }
}

public sealed record class JobActionResult
{
    public required string JobId { get; init; }
    public required JobStatusSnapshot Status { get; init; }
    public string Summary { get; init; } = string.Empty;
    public bool NotesUpdated { get; init; }
    public bool PullRequestAttempted { get; init; }
}
