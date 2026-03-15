using Ringmaster.Core;
using Ringmaster.Core.Configuration;
using Ringmaster.Core.Jobs;
using Ringmaster.Infrastructure.Configuration;
using Ringmaster.Infrastructure.Processes;

namespace Ringmaster.Git;

public sealed class PreparingStageRunner(
    string repositoryRoot,
    RingmasterRepoConfigLoader repoConfigLoader,
    GitWorktreeManager worktreeManager,
    IJobRepository jobRepository,
    TimeProvider timeProvider) : IStageRunner
{
    public JobStage Stage => JobStage.PREPARING;
    public StageRole Role => StageRole.Planner;

    public StageRunDescriptor DescribeRun(StoredJob job)
    {
        return new StageRunDescriptor
        {
            Tool = "git",
            Command = ["prepare-worktree", job.Definition.JobId],
        };
    }

    public async Task<StageExecutionResult> RunAsync(StageExecutionContext context, CancellationToken cancellationToken)
    {
        RingmasterRepoConfig config;

        try
        {
            config = await repoConfigLoader.LoadAsync(repositoryRoot, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return BlockedForMissingConfig(ProductInfo.RepoConfigFileName);
        }
        catch (InvalidDataException exception)
        {
            return BlockedForMissingConfig(exception.Message);
        }

        string verificationProfile = context.Job.Definition.Repo.VerificationProfile;
        if (!config.VerificationProfiles.TryGetValue(verificationProfile, out VerificationProfileDefinition? profile)
            || profile.Commands.Count == 0)
        {
            return BlockedForMissingConfig($"Verification profile '{verificationProfile}' is not configured.");
        }

        string baseBranch = string.IsNullOrWhiteSpace(context.Job.Definition.Repo.BaseBranch)
            ? config.BaseBranch
            : context.Job.Definition.Repo.BaseBranch;

        if (string.IsNullOrWhiteSpace(baseBranch))
        {
            return BlockedForMissingConfig("No base branch was configured for the repository.");
        }

        try
        {
            PreparedWorktree preparedWorktree = await worktreeManager.PrepareAsync(
                repositoryRoot,
                context.Job.Definition.JobId,
                context.Job.Definition.Title,
                baseBranch,
                cancellationToken);
            JobGitSnapshot gitSnapshot = await worktreeManager.CaptureSnapshotAsync(preparedWorktree, cancellationToken);

            await jobRepository.AppendEventAsync(
                context.Job.Definition.JobId,
                JobEventRecord.CreateGitStateCaptured(context.Job.Definition.JobId, gitSnapshot, timeProvider.GetUtcNow()),
                cancellationToken);

            return StageExecutionResult.Succeeded(JobState.IMPLEMENTING, $"Prepared worktree at '{gitSnapshot.WorktreePath}'.");
        }
        catch (GitCliException exception)
        {
            return StageExecutionResult.Failed(
                FailureCategory.ToolFailure,
                BuildGitFailureSummary(exception));
        }
    }

    private static StageExecutionResult BlockedForMissingConfig(string detail)
    {
        return StageExecutionResult.Blocked(
            new BlockerInfo
            {
                ReasonCode = BlockerReasonCode.MissingConfiguration,
                Summary = detail,
                Questions = [$"Create or fix '{ProductInfo.RepoConfigFileName}' so PREPARING can resolve the repository base branch and verification profile."],
                ResumeState = JobState.PREPARING,
            },
            detail);
    }

    private static string BuildGitFailureSummary(GitCliException exception)
    {
        ExternalProcessResult result = exception.ProcessResult;
        return $"Git command failed with exit code {result.ExitCode}: {exception.Message} {result.Stderr}".Trim();
    }
}
