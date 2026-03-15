using Ringmaster.Core.Jobs;

namespace Ringmaster.Core.Tests;

public sealed class RingmasterStateMachineTests
{
    private readonly RingmasterStateMachine _stateMachine = new();

    [Theory]
    [InlineData(JobState.QUEUED, JobState.PREPARING)]
    [InlineData(JobState.PREPARING, JobState.IMPLEMENTING)]
    [InlineData(JobState.IMPLEMENTING, JobState.VERIFYING)]
    [InlineData(JobState.VERIFYING, JobState.REPAIRING)]
    [InlineData(JobState.VERIFYING, JobState.REVIEWING)]
    [InlineData(JobState.REPAIRING, JobState.VERIFYING)]
    [InlineData(JobState.REVIEWING, JobState.READY_FOR_PR)]
    [InlineData(JobState.READY_FOR_PR, JobState.DONE)]
    public void CanTransitionReturnsTrueForAllowedTransitions(JobState from, JobState to)
    {
        Assert.True(_stateMachine.CanTransition(from, to));
        _stateMachine.EnsureCanTransition(from, to);
    }

    [Theory]
    [InlineData(JobState.QUEUED, JobState.REVIEWING)]
    [InlineData(JobState.PREPARING, JobState.READY_FOR_PR)]
    [InlineData(JobState.FAILED, JobState.PREPARING)]
    [InlineData(JobState.DONE, JobState.QUEUED)]
    public void EnsureCanTransitionThrowsForDisallowedTransitions(JobState from, JobState to)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => _stateMachine.EnsureCanTransition(from, to));

        Assert.Equal($"Invalid state transition from {from} to {to}.", exception.Message);
    }

    [Theory]
    [InlineData(JobState.PREPARING, JobStage.PREPARING, StageRole.Planner)]
    [InlineData(JobState.IMPLEMENTING, JobStage.IMPLEMENTING, StageRole.Implementer)]
    [InlineData(JobState.VERIFYING, JobStage.VERIFYING, StageRole.SystemVerifier)]
    [InlineData(JobState.REPAIRING, JobStage.REPAIRING, StageRole.Implementer)]
    [InlineData(JobState.REVIEWING, JobStage.REVIEWING, StageRole.Reviewer)]
    public void GetStageDescriptorMapsActiveStates(JobState state, JobStage stage, StageRole role)
    {
        StageDescriptor? descriptor = _stateMachine.GetStageDescriptor(state);

        Assert.NotNull(descriptor);
        Assert.Equal(stage, descriptor.Stage);
        Assert.Equal(role, descriptor.Role);
    }

    [Theory]
    [InlineData(JobState.READY_FOR_PR, true)]
    [InlineData(JobState.BLOCKED, true)]
    [InlineData(JobState.FAILED, true)]
    [InlineData(JobState.DONE, true)]
    [InlineData(JobState.PREPARING, false)]
    [InlineData(JobState.REVIEWING, false)]
    public void IsAutomaticTerminalReturnsExpectedValues(JobState state, bool expected)
    {
        Assert.Equal(expected, _stateMachine.IsAutomaticTerminal(state));
    }
}
