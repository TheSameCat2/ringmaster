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
    private readonly RepositoryPreparationService _preparationService = new(
        repositoryRoot,
        repoConfigLoader,
        worktreeManager,
        jobRepository,
        timeProvider);

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
        RepositoryPreparationResult preparation = await _preparationService.PrepareAsync(context.Job, cancellationToken);

        if (preparation.Blocker is not null)
        {
            return StageExecutionResult.Blocked(preparation.Blocker, preparation.Summary);
        }

        if (preparation.FailureCategory is not null)
        {
            return StageExecutionResult.Failed(preparation.FailureCategory.Value, preparation.Summary);
        }

        return StageExecutionResult.Succeeded(JobState.IMPLEMENTING, preparation.Summary);
    }
}
