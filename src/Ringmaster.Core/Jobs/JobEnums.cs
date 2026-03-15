namespace Ringmaster.Core.Jobs;

public enum JobState
{
    QUEUED,
    PREPARING,
    IMPLEMENTING,
    VERIFYING,
    REPAIRING,
    REVIEWING,
    READY_FOR_PR,
    DONE,
    BLOCKED,
    FAILED,
}

public enum JobStage
{
    PREPARING,
    IMPLEMENTING,
    VERIFYING,
    REPAIRING,
    REVIEWING,
}

public enum StageRole
{
    Planner,
    Implementer,
    SystemVerifier,
    Reviewer,
}

public enum FailureCategory
{
    TransientError,
    RepairableCodeFailure,
    ToolFailure,
    HumanEscalationRequired,
    AgentProtocolFailure,
    MaxAttemptsExceeded,
}

public enum PullRequestStatus
{
    NotStarted,
    Draft,
    Open,
    Merged,
    Closed,
    Failed,
}

public enum ReviewVerdict
{
    Pending,
    Approved,
    RequestRepair,
    HumanReviewRequired,
}

public enum ReviewRisk
{
    Low,
    Medium,
    High,
}

public enum ExecutionStatus
{
    Idle,
    Running,
}

public enum BlockerReasonCode
{
    ArchitectureDecision,
    MissingConfiguration,
    MissingCredential,
    UnsupportedRepository,
    HumanReviewRequired,
    RepeatedFailureSignature,
}

public enum JobEventType
{
    JobCreated,
    StateChanged,
    GitStateCaptured,
    RunStarted,
    RunHeartbeat,
    RunCompleted,
    JobBlocked,
    JobFailed,
}

public enum RunResult
{
    Completed,
    Failed,
    Canceled,
    Blocked,
}
