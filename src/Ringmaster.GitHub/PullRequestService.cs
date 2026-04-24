using Ringmaster.Core.Jobs;
using Ringmaster.Git;

namespace Ringmaster.GitHub;

public sealed class PullRequestService(
    IJobRepository jobRepository,
    IStateMachine stateMachine,
    GitCli gitCli,
    IPullRequestProvider pullRequestProvider,
    TimeProvider timeProvider) : IPullRequestService
{
    public async Task<PullRequestOperationResult> PublishAsync(string jobId, CancellationToken cancellationToken)
    {
        StoredJob job = await LoadAsync(jobId, cancellationToken);
        EnsureManualPublicationAllowed(job);
        return await PublishCoreAsync(job, cancellationToken);
    }

    public async Task<PullRequestOperationResult> PublishIfConfiguredAsync(string jobId, CancellationToken cancellationToken)
    {
        StoredJob job = await LoadAsync(jobId, cancellationToken);
        if (!job.Definition.Pr.AutoOpen || job.Status.State is not JobState.READY_FOR_PR)
        {
            return new PullRequestOperationResult
            {
                JobId = jobId,
                Status = job.Status,
                PullRequestStatus = job.Status.Pr.Status,
                Url = job.Status.Pr.Url,
                Summary = "Pull request auto-open was not attempted.",
            };
        }

        return await PublishCoreAsync(job, cancellationToken);
    }

    private async Task<PullRequestOperationResult> PublishCoreAsync(StoredJob job, CancellationToken cancellationToken)
    {
        JobGitSnapshot git = job.Status.Git
            ?? throw new InvalidOperationException($"Job '{job.Definition.JobId}' does not have a prepared git snapshot.");
        string worktreePath = git.WorktreePath
            ?? throw new InvalidOperationException($"Job '{job.Definition.JobId}' does not have a worktree path.");
        string jobBranch = git.JobBranch
            ?? throw new InvalidOperationException($"Job '{job.Definition.JobId}' does not have a job branch.");
        string baseBranch = git.BaseBranch
            ?? throw new InvalidOperationException($"Job '{job.Definition.JobId}' does not have a base branch.");
        string pullRequestBodyPath = Path.Combine(job.JobDirectoryPath, "PR.md");

        if (!File.Exists(pullRequestBodyPath))
        {
            throw new InvalidOperationException($"Job '{job.Definition.JobId}' does not have a PR draft at '{pullRequestBodyPath}'.");
        }

        try
        {
            await gitCli.PushBranchAsync(worktreePath, jobBranch, cancellationToken);

            PullRequestProviderResult providerResult = await pullRequestProvider.OpenOrGetAsync(
                new PullRequestPublicationRequest
                {
                    RepositoryRoot = git.RepoRoot ?? worktreePath,
                    WorkingDirectory = worktreePath,
                    HeadBranch = jobBranch,
                    BaseBranch = baseBranch,
                    Title = job.Definition.Title,
                    BodyPath = pullRequestBodyPath,
                    Labels = job.Definition.Pr.Labels,
                    Draft = job.Status.Pr.Draft,
                },
                cancellationToken);

            JobStatusSnapshot status = await jobRepository.AppendEventAsync(
                job.Definition.JobId,
                JobEventRecord.CreatePullRequestRecorded(
                    job.Definition.JobId,
                    providerResult.Status,
                    providerResult.Url,
                    providerResult.Draft,
                    providerResult.Summary,
                    timeProvider.GetUtcNow()),
                cancellationToken);

            if (job.Status.State is JobState.READY_FOR_PR)
            {
                stateMachine.EnsureCanTransition(JobState.READY_FOR_PR, JobState.DONE);
                status = await jobRepository.AppendEventAsync(
                    job.Definition.JobId,
                    JobEventRecord.CreateStateChanged(
                        job.Definition.JobId,
                        JobState.READY_FOR_PR,
                        JobState.DONE,
                        timeProvider.GetUtcNow()),
                    cancellationToken);
            }

            return new PullRequestOperationResult
            {
                JobId = job.Definition.JobId,
                Status = status,
                PullRequestStatus = status.Pr.Status,
                Url = status.Pr.Url,
                Attempted = true,
                Published = true,
                Created = providerResult.Created,
                Summary = providerResult.Summary,
            };
        }
        catch (Exception exception) when (exception is GitCliException or PullRequestProviderException)
        {
            string summary = BuildFailureSummary(exception);

            JobStatusSnapshot failureStatus = await jobRepository.AppendEventAsync(
                job.Definition.JobId,
                JobEventRecord.CreatePullRequestRecorded(
                    job.Definition.JobId,
                    PullRequestStatus.Failed,
                    job.Status.Pr.Url,
                    job.Status.Pr.Draft,
                    summary,
                    timeProvider.GetUtcNow()),
                cancellationToken);

            if (job.Status.State is JobState.READY_FOR_PR)
            {
                stateMachine.EnsureCanTransition(JobState.READY_FOR_PR, JobState.BLOCKED);
                failureStatus = await jobRepository.AppendEventAsync(
                    job.Definition.JobId,
                    JobEventRecord.CreateBlocked(
                        job.Definition.JobId,
                        new BlockerInfo
                        {
                            ReasonCode = BlockerReasonCode.MissingCredential,
                            Summary = summary,
                            ResumeState = JobState.READY_FOR_PR,
                        },
                        timeProvider.GetUtcNow()),
                    cancellationToken);
            }

            return new PullRequestOperationResult
            {
                JobId = job.Definition.JobId,
                Status = failureStatus,
                PullRequestStatus = failureStatus.Pr.Status,
                Url = failureStatus.Pr.Url,
                Attempted = true,
                Published = false,
                Summary = summary,
            };
        }
    }

    private async Task<StoredJob> LoadAsync(string jobId, CancellationToken cancellationToken)
    {
        return await jobRepository.GetAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException($"Job '{jobId}' was not found.");
    }

    private static void EnsureManualPublicationAllowed(StoredJob job)
    {
        if (job.Status.State is JobState.READY_FOR_PR or JobState.DONE)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Job '{job.Definition.JobId}' is in state {job.Status.State} and cannot publish a pull request.");
    }

    private static string BuildFailureSummary(Exception exception)
    {
        return exception switch
        {
            GitCliException gitException => $"Git push failed: {gitException.ProcessResult.Stderr}".Trim(),
            PullRequestProviderException providerException => $"GitHub CLI failed: {providerException.ProcessResult.Stderr}".Trim(),
            _ => exception.Message,
        };
    }
}
