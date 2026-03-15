using Ringmaster.Core.Jobs;

namespace Ringmaster.Infrastructure.Fakes;

public sealed class FakeStageRunner(JobStage stage, StageRole role, JobState nextState, string summary) : IStageRunner
{
    public JobStage Stage { get; } = stage;
    public StageRole Role { get; } = role;

    public Task<StageExecutionResult> RunAsync(StageExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StageExecutionResult.Succeeded(nextState, summary));
    }
}
