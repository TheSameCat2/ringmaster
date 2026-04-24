using Ringmaster.Core.Jobs;
using Ringmaster.Git;

namespace Ringmaster.App;

public static class OperatorExitCodes
{
    public const int Success = 0;
    public const int Blocked = 10;
    public const int Failed = 20;
    public const int ToolOrConfigError = 30;

    public static int FromDoctorReport(DoctorReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.Succeeded ? Success : ToolOrConfigError;
    }

    public static int FromJobStatus(JobStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return FromJobState(status.State);
    }

    public static int FromJobStates(IEnumerable<JobState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        int exitCode = Success;

        foreach (JobState state in states)
        {
            int candidate = FromJobState(state);
            if (candidate > exitCode)
            {
                exitCode = candidate;
            }
        }

        return exitCode;
    }

    public static int FromQueuePassResult(QueuePassResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Jobs.Any(job => job.Disposition is QueueJobDisposition.FailedToStart))
        {
            return ToolOrConfigError;
        }

        return FromJobStates(result.Jobs
            .Where(job => job.FinalState is not null)
            .Select(job => job.FinalState!.Value));
    }

    public static int FromPullRequestResult(PullRequestOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Published)
        {
            return Success;
        }

        return result.PullRequestStatus is PullRequestStatus.Failed
            ? ToolOrConfigError
            : Failed;
    }

    public static int FromCleanupResult(CleanupResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Jobs.Any(job => job.Disposition is CleanupDisposition.Error)
            ? ToolOrConfigError
            : Success;
    }

    private static int FromJobState(JobState state)
    {
        return state switch
        {
            JobState.BLOCKED => Blocked,
            JobState.FAILED => Failed,
            _ => Success,
        };
    }
}
