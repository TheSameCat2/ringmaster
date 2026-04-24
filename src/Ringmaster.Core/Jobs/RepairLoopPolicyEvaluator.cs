namespace Ringmaster.Core.Jobs;

public sealed class RepairLoopPolicyEvaluator(RepairLoopPolicy policy)
{
    public VerificationFailureDisposition Decide(JobStatusSnapshot status, FailureClassification classification, int totalSignatureOccurrences = 0)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(classification);

        if (classification.Category == FailureCategory.TransientError)
        {
            return VerificationFailureDisposition.Block(
                classification,
                new BlockerInfo
                {
                    ReasonCode = BlockerReasonCode.MissingConfiguration,
                    Summary = $"Transient infrastructure failure '{classification.Signature}' persisted after retries. Verify external services or network state before resuming.",
                    Questions =
                    [
                        "Check that external services (network, package registries, build caches) are healthy.",
                        "Confirm the verification profile commands are available and correctly configured.",
                    ],
                    ResumeState = JobState.VERIFYING,
                });
        }

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

        if (totalSignatureOccurrences >= policy.MaxRepeatedFailureSignatures)
        {
            return VerificationFailureDisposition.Block(
                classification,
                new BlockerInfo
                {
                    ReasonCode = BlockerReasonCode.RepeatedFailureSignature,
                    Summary = $"Failure signature '{classification.Signature}' has appeared {totalSignatureOccurrences} times across the job history, suggesting a flaky or nondeterministic failure.",
                    Questions =
                    [
                        "Consider whether the failing test or build step is deterministic.",
                        "If the failure is flaky, the verification profile may need stabilization before resuming.",
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
