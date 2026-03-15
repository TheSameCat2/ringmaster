namespace Ringmaster.Core.Jobs;

public sealed class RepairLoopPolicyEvaluator(RepairLoopPolicy policy)
{
    public VerificationFailureDisposition Decide(JobStatusSnapshot status, FailureClassification classification)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(classification);

        if (classification.Category is not FailureCategory.RepairableCodeFailure)
        {
            return VerificationFailureDisposition.Fail(classification);
        }

        if (status.Attempts.Repairing >= policy.MaxRepairAttempts)
        {
            return VerificationFailureDisposition.Block(
                classification,
                new BlockerInfo
                {
                    ReasonCode = BlockerReasonCode.RepeatedFailureSignature,
                    Summary = $"Repair budget exhausted after {policy.MaxRepairAttempts} repair attempts.",
                    Questions =
                    [
                        "Inspect the latest verifier failure and decide whether the repair loop should continue manually.",
                    ],
                    ResumeState = JobState.REPAIRING,
                });
        }

        if (status.LastFailure is not null
            && string.Equals(status.LastFailure.Signature, classification.Signature, StringComparison.Ordinal)
            && status.LastFailure.RepetitionCount >= policy.MaxRepeatedFailureSignatures - 1)
        {
            return VerificationFailureDisposition.Block(
                classification,
                new BlockerInfo
                {
                    ReasonCode = BlockerReasonCode.RepeatedFailureSignature,
                    Summary = $"Failure signature '{classification.Signature}' repeated without enough progress.",
                    Questions =
                    [
                        "Inspect the repeated failure and decide whether the plan or constraints need to change.",
                    ],
                    ResumeState = JobState.REPAIRING,
                });
        }

        return VerificationFailureDisposition.Repair(classification);
    }
}

public sealed record class VerificationFailureDisposition
{
    public required VerificationFailureAction Action { get; init; }
    public required FailureClassification Classification { get; init; }
    public BlockerInfo? Blocker { get; init; }

    public static VerificationFailureDisposition Repair(FailureClassification classification)
    {
        return new VerificationFailureDisposition
        {
            Action = VerificationFailureAction.Repair,
            Classification = classification,
        };
    }

    public static VerificationFailureDisposition Block(FailureClassification classification, BlockerInfo blocker)
    {
        return new VerificationFailureDisposition
        {
            Action = VerificationFailureAction.Block,
            Classification = classification,
            Blocker = blocker,
        };
    }

    public static VerificationFailureDisposition Fail(FailureClassification classification)
    {
        return new VerificationFailureDisposition
        {
            Action = VerificationFailureAction.Fail,
            Classification = classification,
        };
    }
}

public enum VerificationFailureAction
{
    Repair,
    Block,
    Fail,
}
