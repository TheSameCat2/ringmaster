namespace Ringmaster.Core.Jobs;

public enum ResourceClass
{
    Codex,
    Verification,
    PullRequest,
}

public static class ResourceClassifier
{
    public static ResourceClass? Classify(JobState state)
    {
        return state switch
        {
            JobState.QUEUED => ResourceClass.Codex,
            JobState.PREPARING => ResourceClass.Codex,
            JobState.IMPLEMENTING => ResourceClass.Codex,
            JobState.VERIFYING => ResourceClass.Verification,
            JobState.REPAIRING => ResourceClass.Codex,
            JobState.REVIEWING => ResourceClass.Codex,
            JobState.READY_FOR_PR => ResourceClass.PullRequest,
            _ => null,
        };
    }
}
