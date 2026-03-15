using Ringmaster.Core.Jobs;

namespace Ringmaster.IntegrationTests.Testing;

internal sealed class ScriptedStageRunner(
    JobStage stage,
    StageRole role,
    Func<StageExecutionContext, CancellationToken, Task<StageExecutionResult>> handler) : IStageRunner
{
    public JobStage Stage { get; } = stage;
    public StageRole Role { get; } = role;

    public StageRunDescriptor DescribeRun(StoredJob job)
    {
        return new StageRunDescriptor
        {
            Tool = "scripted",
            Command = ["scripted-runner", Stage.ToString()],
        };
    }

    public Task<StageExecutionResult> RunAsync(StageExecutionContext context, CancellationToken cancellationToken)
    {
        return handler(context, cancellationToken);
    }
}
