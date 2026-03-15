namespace Ringmaster.Core.Jobs;

public sealed class JobEngine(
    IJobRepository jobRepository,
    IStateMachine stateMachine,
    IEnumerable<IStageRunner> stageRunners,
    TimeProvider timeProvider)
{
    private readonly Dictionary<JobStage, IStageRunner> _stageRunners = stageRunners.ToDictionary(runner => runner.Stage);

    public async Task<JobStatusSnapshot> RunAsync(string jobId, CancellationToken cancellationToken)
    {
        StoredJob storedJob = await jobRepository.GetAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException($"Job '{jobId}' was not found.");

        if (storedJob.Status.State is not JobState.QUEUED)
        {
            throw new InvalidOperationException(
                $"Job '{jobId}' is in state {storedJob.Status.State} and cannot be started with 'job run'.");
        }

        JobStatusSnapshot status = await TransitionAsync(jobId, JobState.QUEUED, JobState.PREPARING, cancellationToken);

        while (!stateMachine.IsAutomaticTerminal(status.State))
        {
            StageDescriptor descriptor = stateMachine.GetStageDescriptor(status.State)
                ?? throw new InvalidOperationException($"State {status.State} is not executable.");
            if (!_stageRunners.TryGetValue(descriptor.Stage, out IStageRunner? runner))
            {
                throw new InvalidOperationException($"No stage runner is registered for {descriptor.Stage}.");
            }

            storedJob = await jobRepository.GetAsync(jobId, cancellationToken)
                ?? throw new InvalidOperationException($"Job '{jobId}' was not found.");

            int attempt = GetAttemptCount(storedJob.Status.Attempts, descriptor.Stage) + 1;
            int runNumber = await jobRepository.GetNextRunNumberAsync(jobId, cancellationToken);
            string runId = CreateRunId(runNumber, descriptor.Stage, descriptor.Role);
            DateTimeOffset startedAt = timeProvider.GetUtcNow();
            string runDirectoryPath = Path.Combine(storedJob.JobDirectoryPath, "runs", runId);

            JobRunRecord run = new()
            {
                RunId = runId,
                JobId = jobId,
                Stage = descriptor.Stage,
                Role = descriptor.Role,
                Attempt = attempt,
                StartedAtUtc = startedAt,
                Tool = "fake",
                Command = ["fake-runner", descriptor.Stage.ToString()],
            };

            await jobRepository.SaveRunAsync(jobId, run, cancellationToken);
            status = await jobRepository.AppendEventAsync(jobId, JobEventRecord.CreateRunStarted(run), cancellationToken);

            StageExecutionResult result = await runner.RunAsync(
                new StageExecutionContext
                {
                    Job = storedJob with { Status = status },
                    Run = run,
                    RunDirectoryPath = runDirectoryPath,
                },
                cancellationToken);

            JobRunRecord completedRun = run with
            {
                CompletedAtUtc = timeProvider.GetUtcNow(),
                ExitCode = result.Outcome == StageExecutionOutcome.Failed ? 1 : 0,
                Result = result.Outcome switch
                {
                    StageExecutionOutcome.Succeeded => RunResult.Completed,
                    StageExecutionOutcome.Blocked => RunResult.Blocked,
                    StageExecutionOutcome.Failed => RunResult.Failed,
                    _ => throw new InvalidOperationException($"Unhandled stage outcome {result.Outcome}."),
                },
            };

            await jobRepository.SaveRunAsync(jobId, completedRun, cancellationToken);
            status = await jobRepository.AppendEventAsync(jobId, JobEventRecord.CreateRunCompleted(completedRun), cancellationToken);

            switch (result.Outcome)
            {
                case StageExecutionOutcome.Succeeded:
                    status = await TransitionAsync(jobId, status.State, result.NextState ?? throw new InvalidOperationException("Successful stages must provide a next state."), cancellationToken);
                    break;

                case StageExecutionOutcome.Blocked:
                    status = await jobRepository.AppendEventAsync(jobId, JobEventRecord.CreateBlocked(jobId, result.Blocker ?? throw new InvalidOperationException("Blocked stages must provide blocker info."), timeProvider.GetUtcNow()), cancellationToken);
                    break;

                case StageExecutionOutcome.Failed:
                    status = await jobRepository.AppendEventAsync(jobId, JobEventRecord.CreateFailed(jobId, result.FailureCategory ?? FailureCategory.ToolFailure, result.Summary, timeProvider.GetUtcNow()), cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled stage outcome {result.Outcome}.");
            }
        }

        return status;
    }

    private async Task<JobStatusSnapshot> TransitionAsync(string jobId, JobState from, JobState to, CancellationToken cancellationToken)
    {
        stateMachine.EnsureCanTransition(from, to);
        return await jobRepository.AppendEventAsync(jobId, JobEventRecord.CreateStateChanged(jobId, from, to, timeProvider.GetUtcNow()), cancellationToken);
    }

    private static int GetAttemptCount(JobAttemptCounters attempts, JobStage stage)
    {
        return stage switch
        {
            JobStage.PREPARING => attempts.Preparing,
            JobStage.IMPLEMENTING => attempts.Implementing,
            JobStage.VERIFYING => attempts.Verifying,
            JobStage.REPAIRING => attempts.Repairing,
            JobStage.REVIEWING => attempts.Reviewing,
            _ => 0,
        };
    }

    private static string CreateRunId(int runNumber, JobStage stage, StageRole role)
    {
        return $"{runNumber:D4}-{stage.ToString().ToLowerInvariant()}-{RoleSlug(role)}";
    }

    private static string RoleSlug(StageRole role)
    {
        return role switch
        {
            StageRole.SystemVerifier => "system",
            _ => role.ToString().ToLowerInvariant(),
        };
    }
}
