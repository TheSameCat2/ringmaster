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
        return await ExecuteAsync(jobId, allowResume: false, cancellationToken);
    }

    public async Task<JobStatusSnapshot> ResumeAsync(string jobId, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(jobId, allowResume: true, cancellationToken);
    }

    private async Task<JobStatusSnapshot> ExecuteAsync(string jobId, bool allowResume, CancellationToken cancellationToken)
    {
        StoredJob storedJob = await jobRepository.GetAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException($"Job '{jobId}' was not found.");

        JobStatusSnapshot status = storedJob.Status.State switch
        {
            JobState.QUEUED => await TransitionAsync(jobId, JobState.QUEUED, JobState.PREPARING, cancellationToken),
            JobState.PREPARING or JobState.IMPLEMENTING or JobState.VERIFYING or JobState.REPAIRING or JobState.REVIEWING when allowResume => storedJob.Status,
            _ => throw new InvalidOperationException(
                allowResume
                    ? $"Job '{jobId}' is in state {storedJob.Status.State} and cannot be resumed automatically."
                    : $"Job '{jobId}' is in state {storedJob.Status.State} and cannot be started with 'job run'."),
        };

        if (allowResume && storedJob.Status.Execution.Status is ExecutionStatus.Running)
        {
            await MarkAbandonedRunAsync(storedJob, cancellationToken);
        }

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
            StageRunDescriptor runDescriptor = runner.DescribeRun(storedJob);

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
                Tool = runDescriptor.Tool,
                Command = runDescriptor.Command,
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
                SessionId = result.SessionId,
                ExitCode = result.ExitCode ?? (result.Outcome == StageExecutionOutcome.Failed ? 1 : 0),
                Result = result.Outcome switch
                {
                    StageExecutionOutcome.Succeeded => RunResult.Completed,
                    StageExecutionOutcome.Blocked => RunResult.Blocked,
                    StageExecutionOutcome.Failed => RunResult.Failed,
                    _ => throw new InvalidOperationException($"Unhandled stage outcome {result.Outcome}."),
                },
                Artifacts = result.Artifacts,
            };

            await jobRepository.SaveRunAsync(jobId, completedRun, cancellationToken);
            status = await jobRepository.AppendEventAsync(jobId, JobEventRecord.CreateRunCompleted(completedRun), cancellationToken);

            if (result.Outcome is not StageExecutionOutcome.Failed
                && result.FailureCategory is not null
                && !string.IsNullOrWhiteSpace(result.FailureSignature))
            {
                status = await jobRepository.AppendEventAsync(
                    jobId,
                    JobEventRecord.CreateFailureRecorded(
                        jobId,
                        result.FailureCategory.Value,
                        result.FailureSignature,
                        result.Summary,
                        timeProvider.GetUtcNow()),
                    cancellationToken);
            }

            if (result.ReviewVerdict is not null)
            {
                status = await jobRepository.AppendEventAsync(
                    jobId,
                    JobEventRecord.CreateReviewRecorded(
                        jobId,
                        result.ReviewVerdict.Value,
                        result.ReviewRisk,
                        result.Summary,
                        timeProvider.GetUtcNow()),
                    cancellationToken);
            }

            switch (result.Outcome)
            {
                case StageExecutionOutcome.Succeeded:
                    status = await TransitionAsync(jobId, status.State, result.NextState ?? throw new InvalidOperationException("Successful stages must provide a next state."), cancellationToken);
                    break;

                case StageExecutionOutcome.Blocked:
                    status = await jobRepository.AppendEventAsync(jobId, JobEventRecord.CreateBlocked(jobId, result.Blocker ?? throw new InvalidOperationException("Blocked stages must provide blocker info."), timeProvider.GetUtcNow()), cancellationToken);
                    break;

                case StageExecutionOutcome.Failed:
                    status = await jobRepository.AppendEventAsync(
                        jobId,
                        JobEventRecord.CreateFailed(
                            jobId,
                            result.FailureCategory ?? FailureCategory.ToolFailure,
                            result.FailureSignature ?? $"stage:{descriptor.Stage.ToString().ToLowerInvariant()}:{(result.FailureCategory ?? FailureCategory.ToolFailure).ToString().ToLowerInvariant()}",
                            result.Summary,
                            timeProvider.GetUtcNow()),
                        cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled stage outcome {result.Outcome}.");
            }
        }

        return status;
    }

    private async Task MarkAbandonedRunAsync(StoredJob storedJob, CancellationToken cancellationToken)
    {
        JobExecutionSnapshot execution = storedJob.Status.Execution;
        if (execution.RunId is null || execution.Stage is null || execution.Role is null || execution.StartedAtUtc is null)
        {
            return;
        }

        string runId = execution.RunId;
        if (!IsSafeRunId(runId))
        {
            return;
        }

        string tool = "abandoned";
        IReadOnlyList<string> command = [];
        string runPath = Path.Combine(storedJob.JobDirectoryPath, "runs", runId, "run.json");
        if (File.Exists(runPath))
        {
            string runJson = await File.ReadAllTextAsync(runPath, cancellationToken);
            JobRunRecord existing = Core.Serialization.RingmasterJsonSerializer.Deserialize<JobRunRecord>(runJson);
            SchemaVersionSupport.NormalizeForRead("Job run record", existing.SchemaVersion);
            tool = existing.Tool;
            command = existing.Command;

            if (existing.Result is not null && existing.CompletedAtUtc is not null)
            {
                return;
            }
        }

        await jobRepository.SaveRunAsync(
            storedJob.Definition.JobId,
            new JobRunRecord
            {
                RunId = runId,
                JobId = storedJob.Definition.JobId,
                Stage = execution.Stage.Value,
                Role = execution.Role.Value,
                Attempt = execution.Attempt,
                StartedAtUtc = execution.StartedAtUtc.Value,
                CompletedAtUtc = timeProvider.GetUtcNow(),
                Tool = tool,
                Command = command,
                SessionId = execution.SessionId,
                Result = RunResult.Canceled,
            },
            cancellationToken);
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

    private static bool IsSafeRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId) || Path.IsPathRooted(runId))
        {
            return false;
        }

        return runId.IndexOf(Path.DirectorySeparatorChar) < 0
            && runId.IndexOf(Path.AltDirectorySeparatorChar) < 0
            && !runId.Contains("..", StringComparison.Ordinal);
    }
}
