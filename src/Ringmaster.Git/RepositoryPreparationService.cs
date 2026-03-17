using Ringmaster.Core;
using Ringmaster.Core.Configuration;
using Ringmaster.Core.Jobs;
using Ringmaster.Infrastructure.Configuration;
using Ringmaster.Infrastructure.Processes;

namespace Ringmaster.Git;

public sealed class RepositoryPreparationService(
    string repositoryRoot,
    RingmasterRepoConfigLoader repoConfigLoader,
    GitWorktreeManager worktreeManager,
    IJobRepository jobRepository,
    TimeProvider timeProvider)
{
    public async Task<RepositoryPreparationResult> PrepareAsync(StoredJob job, CancellationToken cancellationToken)
    {
        RingmasterRepoConfig config;

        try
        {
            config = await repoConfigLoader.LoadAsync(repositoryRoot, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return BlockedForMissingConfig(ProductInfo.RepoConfigFileName, JobState.PREPARING);
        }
        catch (InvalidDataException exception)
        {
            return BlockedForMissingConfig(exception.Message, JobState.PREPARING);
        }

        string verificationProfile = job.Definition.Repo.VerificationProfile;
        if (!config.VerificationProfiles.TryGetValue(verificationProfile, out VerificationProfileDefinition? profile)
            || profile.Commands.Count == 0)
        {
            return BlockedForMissingConfig($"Verification profile '{verificationProfile}' is not configured.", JobState.PREPARING);
        }

        foreach (VerificationCommandDefinition command in profile.Commands)
        {
            if (!VerificationCommandSafetyPolicy.TryValidate(command, out string reason))
            {
                return BlockedForMissingConfig(reason, JobState.PREPARING);
            }
        }

        string baseBranch = string.IsNullOrWhiteSpace(job.Definition.Repo.BaseBranch)
            ? config.BaseBranch
            : job.Definition.Repo.BaseBranch;

        if (string.IsNullOrWhiteSpace(baseBranch))
        {
            return BlockedForMissingConfig("No base branch was configured for the repository.", JobState.PREPARING);
        }

        try
        {
            PreparedWorktree preparedWorktree = await worktreeManager.PrepareAsync(
                repositoryRoot,
                job.Definition.JobId,
                job.Definition.Title,
                baseBranch,
                cancellationToken);
            JobGitSnapshot gitSnapshot = await worktreeManager.CaptureSnapshotAsync(preparedWorktree, cancellationToken);

            await jobRepository.AppendEventAsync(
                job.Definition.JobId,
                JobEventRecord.CreateGitStateCaptured(job.Definition.JobId, gitSnapshot, timeProvider.GetUtcNow()),
                cancellationToken);

            return new RepositoryPreparationResult
            {
                Succeeded = true,
                Summary = $"Prepared worktree at '{gitSnapshot.WorktreePath}'.",
                GitSnapshot = gitSnapshot,
            };
        }
        catch (GitCliException exception)
        {
            return new RepositoryPreparationResult
            {
                FailureCategory = FailureCategory.ToolFailure,
                Summary = BuildGitFailureSummary(exception),
            };
        }
    }

    private static RepositoryPreparationResult BlockedForMissingConfig(string detail, JobState resumeState)
    {
        return new RepositoryPreparationResult
        {
            Blocker = new BlockerInfo
            {
                ReasonCode = BlockerReasonCode.MissingConfiguration,
                Summary = detail,
                Questions = [$"Create or fix '{ProductInfo.RepoConfigFileName}' so the stage can resolve the repository base branch and verification profile."],
                ResumeState = resumeState,
            },
            Summary = detail,
        };
    }

    private static string BuildGitFailureSummary(GitCliException exception)
    {
        ExternalProcessResult result = exception.ProcessResult;
        return $"Git command failed with exit code {result.ExitCode}: {exception.Message} {result.Stderr}".Trim();
    }
}
