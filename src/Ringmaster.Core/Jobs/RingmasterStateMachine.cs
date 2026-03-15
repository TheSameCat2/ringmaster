namespace Ringmaster.Core.Jobs;

public sealed class RingmasterStateMachine : IStateMachine
{
    private static readonly IReadOnlyDictionary<JobState, JobState[]> AllowedTransitions = new Dictionary<JobState, JobState[]>
    {
        [JobState.QUEUED] = [JobState.PREPARING],
        [JobState.PREPARING] = [JobState.IMPLEMENTING, JobState.BLOCKED, JobState.FAILED],
        [JobState.IMPLEMENTING] = [JobState.VERIFYING, JobState.BLOCKED, JobState.FAILED],
        [JobState.VERIFYING] = [JobState.REVIEWING, JobState.REPAIRING, JobState.BLOCKED, JobState.FAILED],
        [JobState.REPAIRING] = [JobState.VERIFYING, JobState.BLOCKED, JobState.FAILED],
        [JobState.REVIEWING] = [JobState.READY_FOR_PR, JobState.REPAIRING, JobState.BLOCKED],
        [JobState.READY_FOR_PR] = [JobState.DONE],
        [JobState.BLOCKED] = [JobState.PREPARING, JobState.IMPLEMENTING, JobState.VERIFYING, JobState.REPAIRING, JobState.REVIEWING],
        [JobState.FAILED] = [],
        [JobState.DONE] = [],
    };

    private static readonly IReadOnlyDictionary<JobState, StageDescriptor> ActiveStates = new Dictionary<JobState, StageDescriptor>
    {
        [JobState.PREPARING] = new(JobStage.PREPARING, StageRole.Planner),
        [JobState.IMPLEMENTING] = new(JobStage.IMPLEMENTING, StageRole.Implementer),
        [JobState.VERIFYING] = new(JobStage.VERIFYING, StageRole.SystemVerifier),
        [JobState.REPAIRING] = new(JobStage.REPAIRING, StageRole.Implementer),
        [JobState.REVIEWING] = new(JobStage.REVIEWING, StageRole.Reviewer),
    };

    public bool CanTransition(JobState from, JobState to)
    {
        return AllowedTransitions.TryGetValue(from, out JobState[]? allowed) && allowed.Contains(to);
    }

    public void EnsureCanTransition(JobState from, JobState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Invalid state transition from {from} to {to}.");
        }
    }

    public bool IsAutomaticTerminal(JobState state)
    {
        return state is JobState.READY_FOR_PR or JobState.DONE or JobState.FAILED or JobState.BLOCKED;
    }

    public StageDescriptor? GetStageDescriptor(JobState state)
    {
        return ActiveStates.TryGetValue(state, out StageDescriptor? descriptor) ? descriptor : null;
    }
}
