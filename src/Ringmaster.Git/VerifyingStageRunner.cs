using Ringmaster.Core;
using Ringmaster.Core.Configuration;
using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Ringmaster.Infrastructure.Configuration;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.Infrastructure.Processes;

namespace Ringmaster.Git;

public sealed class VerifyingStageRunner(
    string repositoryRoot,
    RingmasterRepoConfigLoader repoConfigLoader,
    ExternalProcessRunner processRunner,
    GitCli gitCli,
    GitWorktreeManager worktreeManager,
    AtomicFileWriter atomicFileWriter,
    IJobRepository jobRepository,
    TimeProvider timeProvider,
    IFailureClassifier failureClassifier,
    RepairLoopPolicyEvaluator repairLoopPolicyEvaluator) : IStageRunner
{
    public JobStage Stage => JobStage.VERIFYING;
    public StageRole Role => StageRole.SystemVerifier;

    public StageRunDescriptor DescribeRun(StoredJob job)
    {
        return new StageRunDescriptor
        {
            Tool = "verification",
            Command = ["verification-profile", job.Definition.Repo.VerificationProfile],
        };
    }

    public async Task<StageExecutionResult> RunAsync(StageExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Job.Status.Git?.WorktreePath is not { Length: > 0 } worktreePath)
        {
            return StageExecutionResult.Failed(
                FailureCategory.ToolFailure,
                "Verification cannot run because the job does not have a prepared worktree.");
        }

        RingmasterRepoConfig config;

        try
        {
            config = await repoConfigLoader.LoadAsync(repositoryRoot, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return BlockedForMissingConfig(ProductInfo.RepoConfigFileName);
        }
        catch (InvalidDataException exception)
        {
            return BlockedForMissingConfig(exception.Message);
        }

        string verificationProfile = context.Job.Definition.Repo.VerificationProfile;
        if (!config.VerificationProfiles.TryGetValue(verificationProfile, out VerificationProfileDefinition? profile)
            || profile.Commands.Count == 0)
        {
            return BlockedForMissingConfig($"Verification profile '{verificationProfile}' is not configured.");
        }

        List<VerificationCommandRecord> commandRecords = [];
        ExternalProcessResult? failure = null;
        VerificationCommandDefinition? failureDefinition = null;

        for (int index = 0; index < profile.Commands.Count; index++)
        {
            VerificationCommandDefinition command = profile.Commands[index];
            if (!VerificationCommandSafetyPolicy.TryValidate(command, out string reason))
            {
                return BlockedForMissingConfig(reason);
            }

            string baseName = $"{index + 1:D2}-{SanitizeFileName(command.Name)}";
            string stdoutPath = Path.Combine(context.RunDirectoryPath, $"{baseName}.log");
            string stderrPath = Path.Combine(context.RunDirectoryPath, $"{baseName}.stderr.log");

            ExternalProcessResult result = await processRunner.RunAsync(
                new ExternalProcessSpec
                {
                    FileName = command.FileName,
                    Arguments = command.Arguments,
                    WorkingDirectory = worktreePath,
                    Timeout = TimeSpan.FromSeconds(command.TimeoutSeconds),
                    StdoutPath = stdoutPath,
                    StderrPath = stderrPath,
                },
                cancellationToken);

            commandRecords.Add(new VerificationCommandRecord
            {
                TimestampUtc = result.StartedAtUtc,
                RunId = context.Run.RunId,
                JobId = context.Job.Definition.JobId,
                WorkingDirectory = worktreePath,
                FileName = command.FileName,
                Arguments = command.Arguments,
                EnvironmentVariableNamesUsed = result.EnvironmentVariableNames,
                TimeoutSeconds = command.TimeoutSeconds,
                ExitCode = result.ExitCode,
                DurationMs = (result.CompletedAtUtc - result.StartedAtUtc).TotalMilliseconds,
                TimedOut = result.TimedOut,
                StdoutPath = Path.GetFileName(stdoutPath),
                StderrPath = Path.GetFileName(stderrPath),
            });

            if (result.TimedOut || result.ExitCode != 0)
            {
                failure = result;
                failureDefinition = command;
                break;
            }
        }

        RunArtifacts artifacts = await PersistVerificationArtifactsAsync(
            context,
            worktreePath,
            verificationProfile,
            commandRecords,
            cancellationToken);

        if (failure is not null && failureDefinition is not null)
        {
            VerificationCommandRecord failureRecord = commandRecords[^1];
            FailureClassification classification = await ClassifyFailureAsync(
                context,
                changedFiles: context.Job.Status.Git?.ChangedFiles ?? [],
                failureDefinition,
                failureRecord,
                cancellationToken);
            await PersistRepairArtifactAsync(
                context,
                verificationProfile,
                failureDefinition,
                failureRecord,
                classification,
                cancellationToken);

            VerificationFailureDisposition disposition = repairLoopPolicyEvaluator.Decide(context.Job.Status, classification);

            return disposition.Action switch
            {
                VerificationFailureAction.Repair => StageExecutionResult.Succeeded(
                    JobState.REPAIRING,
                    classification.Summary,
                    artifacts,
                    classification.Category,
                    classification.Signature),
                VerificationFailureAction.Block => StageExecutionResult.Blocked(
                    disposition.Blocker ?? throw new InvalidOperationException("Blocked verification failures must provide blocker info."),
                    classification.Summary,
                    artifacts,
                    classification.Category,
                    classification.Signature),
                VerificationFailureAction.Fail => StageExecutionResult.Failed(
                    classification.Category,
                    classification.Summary,
                    artifacts,
                    classification.Signature),
                _ => throw new InvalidOperationException($"Unhandled verification failure action '{disposition.Action}'."),
            };
        }

        return StageExecutionResult.Succeeded(JobState.REVIEWING, $"Verification profile '{verificationProfile}' passed.", artifacts);
    }

    private async Task<RunArtifacts> PersistVerificationArtifactsAsync(
        StageExecutionContext context,
        string worktreePath,
        string profileName,
        IReadOnlyList<VerificationCommandRecord> commandRecords,
        CancellationToken cancellationToken)
    {
        string commandsPath = Path.Combine(context.RunDirectoryPath, "commands.jsonl");
        string commandsJsonl = string.Join(
            Environment.NewLine,
            commandRecords.Select(RingmasterJsonSerializer.SerializeCompact));
        if (commandRecords.Count > 0)
        {
            commandsJsonl += Environment.NewLine;
        }

        await atomicFileWriter.WriteTextAsync(commandsPath, commandsJsonl, cancellationToken);

        JobGitSnapshot preparedSnapshot = context.Job.Status.Git
            ?? throw new InvalidOperationException("Verification requires the prepared git snapshot.");
        JobGitSnapshot gitSnapshot = await worktreeManager.CaptureSnapshotAsync(
            new PreparedWorktree
            {
                RepoRoot = preparedSnapshot.RepoRoot ?? repositoryRoot,
                BaseBranch = preparedSnapshot.BaseBranch ?? context.Job.Definition.Repo.BaseBranch,
                BaseCommit = preparedSnapshot.BaseCommit ?? throw new InvalidOperationException("The prepared git snapshot does not contain a base commit."),
                JobBranch = preparedSnapshot.JobBranch ?? throw new InvalidOperationException("The prepared git snapshot does not contain a job branch."),
                WorktreePath = preparedSnapshot.WorktreePath ?? worktreePath,
                HeadCommit = preparedSnapshot.HeadCommit ?? preparedSnapshot.BaseCommit!,
            },
            cancellationToken);

        string artifactsDirectory = Path.Combine(context.Job.JobDirectoryPath, "artifacts");
        await atomicFileWriter.WriteJsonAsync(Path.Combine(artifactsDirectory, "changed-files.json"), gitSnapshot.ChangedFiles, cancellationToken);
        await atomicFileWriter.WriteTextAsync(
            Path.Combine(artifactsDirectory, "diff.patch"),
            await gitCli.CaptureDiffPatchAsync(worktreePath, gitSnapshot.BaseCommit ?? throw new InvalidOperationException("Base commit was not available for diff generation."), cancellationToken),
            cancellationToken);
        await atomicFileWriter.WriteTextAsync(
            Path.Combine(artifactsDirectory, "diffstat.txt"),
            await gitCli.CaptureDiffStatAsync(worktreePath, gitSnapshot.BaseCommit ?? throw new InvalidOperationException("Base commit was not available for diff generation."), cancellationToken),
            cancellationToken);
        await atomicFileWriter.WriteJsonAsync(
            Path.Combine(artifactsDirectory, "verification-summary.json"),
            new VerificationSummary
            {
                JobId = context.Job.Definition.JobId,
                RunId = context.Run.RunId,
                ProfileName = profileName,
                Succeeded = commandRecords.All(record => !record.TimedOut && record.ExitCode == 0),
                Commands = commandRecords,
                ChangedFiles = gitSnapshot.ChangedFiles,
            },
            cancellationToken);
        await jobRepository.AppendEventAsync(
            context.Job.Definition.JobId,
            JobEventRecord.CreateGitStateCaptured(context.Job.Definition.JobId, gitSnapshot, timeProvider.GetUtcNow()),
            cancellationToken);

        return new RunArtifacts
        {
            EventLog = Path.GetFileName(commandsPath),
        };
    }

    private async Task<FailureClassification> ClassifyFailureAsync(
        StageExecutionContext context,
        IReadOnlyList<string> changedFiles,
        VerificationCommandDefinition failureDefinition,
        VerificationCommandRecord failureRecord,
        CancellationToken cancellationToken)
    {
        string stdoutText = failureRecord.StdoutPath is { Length: > 0 }
            ? await File.ReadAllTextAsync(Path.Combine(context.RunDirectoryPath, failureRecord.StdoutPath), cancellationToken)
            : string.Empty;
        string stderrText = failureRecord.StderrPath is { Length: > 0 }
            ? await File.ReadAllTextAsync(Path.Combine(context.RunDirectoryPath, failureRecord.StderrPath), cancellationToken)
            : string.Empty;

        return failureClassifier.Classify(
            new FailureClassificationContext
            {
                Stage = JobStage.VERIFYING,
                CommandName = failureDefinition.Name,
                CommandFileName = failureDefinition.FileName,
                CommandArguments = failureDefinition.Arguments,
                ExitCode = failureRecord.ExitCode,
                TimedOut = failureRecord.TimedOut,
                StdoutText = stdoutText,
                StderrText = stderrText,
                ChangedFiles = changedFiles,
            });
    }

    private async Task PersistRepairArtifactAsync(
        StageExecutionContext context,
        string verificationProfile,
        VerificationCommandDefinition failureDefinition,
        VerificationCommandRecord failureRecord,
        FailureClassification classification,
        CancellationToken cancellationToken)
    {
        string artifactsDirectory = Path.Combine(context.Job.JobDirectoryPath, "artifacts");
        await atomicFileWriter.WriteJsonAsync(
            Path.Combine(artifactsDirectory, "repair-summary.json"),
            new VerificationFailureSummary
            {
                JobId = context.Job.Definition.JobId,
                RunId = context.Run.RunId,
                ProfileName = verificationProfile,
                CommandName = failureDefinition.Name,
                Category = classification.Category,
                Signature = classification.Signature,
                Summary = classification.Summary,
                ExitCode = failureRecord.ExitCode,
                TimedOut = failureRecord.TimedOut,
                StdoutPath = failureRecord.StdoutPath,
                StderrPath = failureRecord.StderrPath,
                Highlights = classification.Highlights,
                ChangedFiles = context.Job.Status.Git?.ChangedFiles ?? [],
            },
            cancellationToken);
    }

    private static StageExecutionResult BlockedForMissingConfig(string detail)
    {
        return StageExecutionResult.Blocked(
            new BlockerInfo
            {
                ReasonCode = BlockerReasonCode.MissingConfiguration,
                Summary = detail,
                Questions = [$"Create or fix '{ProductInfo.RepoConfigFileName}' so VERIFYING can load the configured command profile."],
                ResumeState = JobState.VERIFYING,
            },
            detail);
    }

    private static string SanitizeFileName(string name)
    {
        char[] characters = name
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();

        string sanitized = new string(characters).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "command" : sanitized;
    }
}
