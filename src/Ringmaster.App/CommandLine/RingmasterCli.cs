using System.CommandLine;
using System.Diagnostics;
using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Ringmaster.Git;
using Spectre.Console;

namespace Ringmaster.App.CommandLine;

public sealed class RingmasterCli(
    IAnsiConsole console,
    IJobRepository jobRepository,
    JobEngine jobEngine,
    QueueProcessor queueProcessor,
    IPullRequestService pullRequestService,
    DoctorService doctorService,
    RepositoryInitializationService repositoryInitializationService,
    JobOperatorService jobOperatorService,
    RunLogService runLogService,
    StatusDisplayService statusDisplayService,
    CleanupService cleanupService,
    RingmasterApplicationContext applicationContext)
{
    public RootCommand CreateRootCommand()
    {
        RootCommand rootCommand = new("Durable Codex orchestration for git-backed engineering work.");

        rootCommand.Subcommands.Add(CreateInitCommand());
        rootCommand.Subcommands.Add(CreateDoctorCommand());
        rootCommand.Subcommands.Add(CreateJobCommand());
        rootCommand.Subcommands.Add(CreateQueueCommand());
        rootCommand.Subcommands.Add(CreateStatusCommand());
        rootCommand.Subcommands.Add(CreateLogsCommand());
        rootCommand.Subcommands.Add(CreatePrCommand());
        rootCommand.Subcommands.Add(CreateWorktreeCommand());
        rootCommand.Subcommands.Add(CreateCleanupCommand());

        rootCommand.SetAction(_ =>
        {
            console.MarkupLine("[bold green]Ringmaster[/] coordinates Codex-driven repository work.");
            console.MarkupLine("Run [yellow]ringmaster --help[/] or inspect a subcommand for available operations.");
        });

        return rootCommand;
    }

    private Command CreateDoctorCommand()
    {
        Command command = new("doctor", "Check local prerequisites and repo health.");
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };
        command.Options.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            DoctorReport report = await doctorService.RunAsync(cancellationToken);

            if (parseResult.GetValue(jsonOption))
            {
                WriteJson(report);
                return OperatorExitCodes.FromDoctorReport(report);
            }

            foreach (DoctorCheckResult check in report.Checks)
            {
                string statusText = check.Succeeded ? "[green]ok[/]" : "[red]fail[/]";
                console.MarkupLine($"{statusText} {Markup.Escape(check.Name)}: {Markup.Escape(check.Detail)}");
            }

            return OperatorExitCodes.FromDoctorReport(report);
        });

        return command;
    }

    private Command CreateJobCommand()
    {
        Command command = new("job", "Create, inspect, and run individual jobs.");
        command.Subcommands.Add(CreateJobCreateCommand());
        command.Subcommands.Add(CreateJobShowCommand());
        command.Subcommands.Add(CreateJobRunCommand());
        command.Subcommands.Add(CreateJobResumeCommand());
        command.Subcommands.Add(CreateJobUnblockCommand());
        command.Subcommands.Add(CreateJobCancelCommand());
        return command;
    }

    private Command CreateQueueCommand()
    {
        Command command = new("queue", "Run the scheduler loop or a single scheduling pass.");
        command.Subcommands.Add(CreateQueueRunCommand());
        command.Subcommands.Add(CreateQueueOnceCommand());
        return command;
    }

    private Command CreatePrCommand()
    {
        Command command = new("pr", "Open and inspect pull request state.");
        command.Subcommands.Add(CreatePrOpenCommand());
        return command;
    }

    private Command CreateWorktreeCommand()
    {
        Command command = new("worktree", "Inspect or open job worktrees.");
        command.Subcommands.Add(CreateWorktreeOpenCommand());
        return command;
    }

    private Command CreateInitCommand()
    {
        Command command = new("init", "Initialize local Ringmaster runtime layout.");
        Option<string> baseBranchOption = new("--base-branch") { Description = "Repository base branch.", DefaultValueFactory = _ => "master" };
        Option<string> prProviderOption = new("--pr-provider") { Description = "Pull request provider. Only 'github' is supported.", DefaultValueFactory = _ => "github" };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Options.Add(baseBranchOption);
        command.Options.Add(prProviderOption);
        command.Options.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                RepositoryInitializationResult result = await repositoryInitializationService.InitializeAsync(
                    new RepositoryInitializationOptions
                    {
                        BaseBranch = parseResult.GetValue(baseBranchOption) ?? "master",
                        PullRequestProvider = parseResult.GetValue(prProviderOption) ?? "github",
                    },
                    cancellationToken);

                if (parseResult.GetValue(jsonOption))
                {
                    WriteJson(result);
                    return OperatorExitCodes.Success;
                }

                console.MarkupLine($"[green]Initialized Ringmaster runtime[/] at [grey]{Markup.Escape(result.RuntimeRoot)}[/]");
                console.MarkupLine(result.ConfigCreated
                    ? $"Config: [green]created[/] [grey]{Markup.Escape(result.ConfigPath)}[/]"
                    : $"Config: [yellow]existing[/] [grey]{Markup.Escape(result.ConfigPath)}[/]");
                console.MarkupLine(result.GitIgnoreUpdated
                    ? "Git ignore: [green]updated[/] with `.ringmaster/`"
                    : "Git ignore: [blue]already covered[/]");

                if (result.ConfigCreated && result.VerificationCommandsScaffolded && result.SolutionPath is not null)
                {
                    console.MarkupLine($"Verification profile: scaffolded from [grey]{Markup.Escape(Path.GetFileName(result.SolutionPath))}[/]");
                }
                else if (result.ConfigCreated)
                {
                    console.MarkupLine("Verification profile: [yellow]created without commands; edit ringmaster.json before running jobs.[/]");
                }

                return OperatorExitCodes.Success;
            }
            catch (Exception exception) when (IsOperatorError(exception))
            {
                return WriteOperatorError(exception);
            }
        });

        return command;
    }

    private Command CreateJobCreateCommand()
    {
        Command command = new("create", "Create a durable queued job.");
        Option<string> titleOption = new("--title") { Description = "Short job title.", Required = true };
        Option<string?> descriptionOption = new("--description") { Description = "Inline task description." };
        Option<FileInfo?> taskFileOption = new("--task-file") { Description = "Markdown file containing the task description." };
        Option<string[]> acceptanceOption = new("--acceptance") { Description = "Acceptance criterion. Repeat for multiple items." };
        Option<string> verifyProfileOption = new("--verify-profile") { Description = "Verification profile name.", DefaultValueFactory = _ => "default" };
        Option<string> baseBranchOption = new("--base-branch") { Description = "Repository base branch.", DefaultValueFactory = _ => "master" };
        Option<int> priorityOption = new("--priority") { Description = "Queue priority.", DefaultValueFactory = _ => 50 };
        Option<bool> autoOpenPrOption = new("--auto-open-pr") { Description = "Automatically publish the PR after review." };
        Option<bool> draftPrOption = new("--draft-pr") { Description = "Create the PR as a draft.", DefaultValueFactory = _ => true };
        Option<string[]> labelsOption = new("--label") { Description = "Pull request label. Repeat for multiple labels." };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Options.Add(titleOption);
        command.Options.Add(descriptionOption);
        command.Options.Add(taskFileOption);
        command.Options.Add(acceptanceOption);
        command.Options.Add(verifyProfileOption);
        command.Options.Add(baseBranchOption);
        command.Options.Add(priorityOption);
        command.Options.Add(autoOpenPrOption);
        command.Options.Add(draftPrOption);
        command.Options.Add(labelsOption);
        command.Options.Add(jsonOption);

        command.Validators.Add(result =>
        {
            string? description = result.GetValue(descriptionOption);
            FileInfo? taskFile = result.GetValue(taskFileOption);

            if (string.IsNullOrWhiteSpace(description) && taskFile is null)
            {
                result.AddError("Either --description or --task-file is required.");
            }
        });

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string title = parseResult.GetValue(titleOption)
                ?? throw new InvalidOperationException("The required --title option was not provided.");
            string? description = parseResult.GetValue(descriptionOption);
            FileInfo? taskFile = parseResult.GetValue(taskFileOption);

            if (taskFile is not null)
            {
                string taskPath = taskFile.FullName;
                description = await File.ReadAllTextAsync(taskPath, cancellationToken);
            }

            StoredJob storedJob = await jobRepository.CreateAsync(
                new JobCreateRequest
                {
                    Title = title,
                    Description = description ?? string.Empty,
                    JobMarkdown = taskFile is null ? null : description,
                    AcceptanceCriteria = parseResult.GetValue(acceptanceOption) ?? [],
                    VerificationProfile = parseResult.GetValue(verifyProfileOption) ?? "default",
                    BaseBranch = parseResult.GetValue(baseBranchOption) ?? "master",
                    Priority = parseResult.GetValue(priorityOption),
                    AutoOpenPullRequest = parseResult.GetValue(autoOpenPrOption),
                    DraftPullRequest = parseResult.GetValue(draftPrOption),
                    PullRequestLabels = parseResult.GetValue(labelsOption) ?? [],
                    CreatedBy = applicationContext.CurrentActor,
                },
                cancellationToken);

            if (parseResult.GetValue(jsonOption))
            {
                WriteJson(new
                {
                    storedJob.Definition.JobId,
                    storedJob.JobDirectoryPath,
                    storedJob.Status.State,
                    storedJob.Definition.Title,
                });

                return OperatorExitCodes.Success;
            }

            console.MarkupLine($"[green]Created job[/] [bold]{Markup.Escape(storedJob.Definition.JobId)}[/]");
            console.MarkupLine($"Path: [grey]{Markup.Escape(storedJob.JobDirectoryPath)}[/]");
            console.MarkupLine($"State: [blue]{storedJob.Status.State}[/]");
            return OperatorExitCodes.Success;
        });

        return command;
    }

    private Command CreateJobShowCommand()
    {
        Command command = new("show", "Show one job in detail.");
        Argument<string> jobIdArgument = new("job-id") { Description = "Job identifier." };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Arguments.Add(jobIdArgument);
        command.Options.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string jobId = parseResult.GetValue(jobIdArgument)
                ?? throw new InvalidOperationException("The required job identifier was not provided.");
            StoredJob? storedJob = await jobRepository.GetAsync(jobId, cancellationToken);

            if (storedJob is null)
            {
                console.MarkupLine($"[red]Job not found:[/] {Markup.Escape(jobId)}");
                return OperatorExitCodes.ToolOrConfigError;
            }

            if (parseResult.GetValue(jsonOption))
            {
                WriteJson(storedJob);
                return OperatorExitCodes.FromJobStatus(storedJob.Status);
            }

            Table table = new();
            table.AddColumn("Field");
            table.AddColumn("Value");
            table.AddRow("Job Id", storedJob.Definition.JobId);
            table.AddRow("Title", storedJob.Definition.Title);
            table.AddRow("State", storedJob.Status.State.ToString());
            table.AddRow("Priority", storedJob.Status.Priority.ToString());
            table.AddRow("Base Branch", storedJob.Definition.Repo.BaseBranch);
            table.AddRow("Verification Profile", storedJob.Definition.Repo.VerificationProfile);
            table.AddRow("Created At", storedJob.Definition.CreatedAtUtc.ToString("u"));
            table.AddRow("Updated At", storedJob.Status.UpdatedAtUtc.ToString("u"));
            table.AddRow("Path", storedJob.JobDirectoryPath);

            console.Write(table);
            console.WriteLine();
            console.MarkupLine(storedJob.Definition.Description);
            return OperatorExitCodes.FromJobStatus(storedJob.Status);
        });

        return command;
    }

    private Command CreateJobRunCommand()
    {
        Command command = new("run", "Run one job synchronously.");
        Argument<string> jobIdArgument = new("job-id") { Description = "Job identifier." };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Arguments.Add(jobIdArgument);
        command.Options.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string jobId = parseResult.GetValue(jobIdArgument)
                ?? throw new InvalidOperationException("The required job identifier was not provided.");

            try
            {
                JobStatusSnapshot status = await jobEngine.RunAsync(jobId, cancellationToken);
                if (status.State is JobState.READY_FOR_PR)
                {
                    PullRequestOperationResult publication = await pullRequestService.PublishIfConfiguredAsync(jobId, cancellationToken);
                    status = publication.Status;
                }

                if (parseResult.GetValue(jsonOption))
                {
                    WriteJson(status);
                    return OperatorExitCodes.FromJobStatus(status);
                }

                console.MarkupLine($"[green]Job finished in state[/] [blue]{status.State}[/]");
                return OperatorExitCodes.FromJobStatus(status);
            }
            catch (Exception exception) when (IsOperatorError(exception))
            {
                return WriteOperatorError(exception);
            }
        });

        return command;
    }

    private Command CreateStatusCommand()
    {
        Command command = new("status", "Show job or queue status.");
        Option<string?> jobIdOption = new("--job-id") { Description = "Show a single job status." };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };
        Option<bool> watchOption = new("--watch") { Description = "Refresh the human-readable status view until canceled." };

        command.Options.Add(jobIdOption);
        command.Options.Add(jsonOption);
        command.Options.Add(watchOption);

        command.Validators.Add(result =>
        {
            if (result.GetValue(jsonOption) && result.GetValue(watchOption))
            {
                result.AddError("--watch cannot be combined with --json.");
            }
        });

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string? jobId = parseResult.GetValue(jobIdOption);
            bool emitJson = parseResult.GetValue(jsonOption);
            bool watch = parseResult.GetValue(watchOption);

            if (watch)
            {
                try
                {
                    await statusDisplayService.WatchAsync(
                        jobId,
                        TimeSpan.FromSeconds(1),
                        (snapshot, _) =>
                        {
                            RenderStatusDashboard(snapshot);
                            return Task.CompletedTask;
                        },
                        cancellationToken);
                    return OperatorExitCodes.Success;
                }
                catch (OperationCanceledException)
                {
                    IReadOnlyList<StatusDisplayItem> finalSnapshot = await statusDisplayService.GetSnapshotAsync(jobId, CancellationToken.None);
                    return OperatorExitCodes.FromJobStates(finalSnapshot.Select(item => item.State));
                }
                catch (Exception exception) when (IsOperatorError(exception))
                {
                    return WriteOperatorError(exception);
                }
            }

            if (!string.IsNullOrWhiteSpace(jobId))
            {
                StoredJob? storedJob = await jobRepository.GetAsync(jobId, cancellationToken);
                if (storedJob is null)
                {
                    console.MarkupLine($"[red]Job not found:[/] {Markup.Escape(jobId)}");
                    return OperatorExitCodes.ToolOrConfigError;
                }

                if (emitJson)
                {
                    WriteJson(storedJob.Status);
                    return OperatorExitCodes.FromJobStatus(storedJob.Status);
                }

                console.MarkupLine($"[bold]{Markup.Escape(storedJob.Status.JobId)}[/] [blue]{storedJob.Status.State}[/] {Markup.Escape(storedJob.Status.Title)}");
                return OperatorExitCodes.FromJobStatus(storedJob.Status);
            }

            IReadOnlyList<JobStatusListItem> jobs = await jobRepository.ListAsync(cancellationToken);
            if (emitJson)
            {
                WriteJson(jobs);
                return OperatorExitCodes.FromJobStates(jobs.Select(job => job.State));
            }

            if (jobs.Count == 0)
            {
                console.MarkupLine("[yellow]No jobs found.[/]");
                return OperatorExitCodes.Success;
            }

            Table table = new();
            table.AddColumn("Job Id");
            table.AddColumn("State");
            table.AddColumn("Priority");
            table.AddColumn("Updated");
            table.AddColumn("Title");

            foreach (JobStatusListItem job in jobs)
            {
                table.AddRow(
                    job.JobId,
                    job.State.ToString(),
                    job.Priority.ToString(),
                    job.UpdatedAtUtc.ToString("u"),
                    job.Title);
            }

            console.Write(table);
            return OperatorExitCodes.FromJobStates(jobs.Select(job => job.State));
        });

        return command;
    }

    private Command CreateLogsCommand()
    {
        Command command = new("logs", "Inspect stored run logs.");
        Argument<string> jobIdArgument = new("job-id") { Description = "Job identifier." };
        Option<string?> runOption = new("--run") { Description = "Specific run identifier to inspect." };
        Option<bool> followOption = new("--follow") { Description = "Follow the selected log until canceled." };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Arguments.Add(jobIdArgument);
        command.Options.Add(runOption);
        command.Options.Add(followOption);
        command.Options.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string jobId = parseResult.GetValue(jobIdArgument)
                ?? throw new InvalidOperationException("The required job identifier was not provided.");

            try
            {
                RunLogSelection selection = await runLogService.SelectAsync(
                    jobId,
                    parseResult.GetValue(runOption),
                    cancellationToken);

                if (parseResult.GetValue(jsonOption))
                {
                    WriteJson(selection);
                    return OperatorExitCodes.Success;
                }

                if (parseResult.GetValue(followOption))
                {
                    await runLogService.FollowAsync(
                        selection,
                        (chunk, _) =>
                        {
                            console.Write(new Text(chunk));
                            return Task.CompletedTask;
                        },
                        TimeSpan.FromMilliseconds(250),
                        cancellationToken);
                    return OperatorExitCodes.Success;
                }

                string content = await runLogService.ReadAsync(selection, cancellationToken);
                console.Write(new Text(content));
                if (content.Length > 0 && !content.EndsWith('\n'))
                {
                    console.WriteLine();
                }

                return OperatorExitCodes.Success;
            }
            catch (OperationCanceledException)
            {
                return OperatorExitCodes.Success;
            }
            catch (Exception exception) when (IsOperatorError(exception))
            {
                return WriteOperatorError(exception);
            }
        });

        return command;
    }

    private Command CreateQueueOnceCommand()
    {
        Command command = new("once", "Run one scheduling pass.");
        Option<int> maxParallelOption = new("--max-parallel") { Description = "Maximum jobs to start in this pass.", DefaultValueFactory = _ => 1 };
        Option<int> maxCodexOption = new("--max-codex") { Description = "Maximum concurrent Codex-backed stages.", DefaultValueFactory = _ => 1 };
        Option<int> maxVerifyOption = new("--max-verify") { Description = "Maximum concurrent verification runs.", DefaultValueFactory = _ => 1 };
        Option<int> maxPrOption = new("--max-pr") { Description = "Maximum concurrent PR operations.", DefaultValueFactory = _ => 1 };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Options.Add(maxParallelOption);
        command.Options.Add(maxCodexOption);
        command.Options.Add(maxVerifyOption);
        command.Options.Add(maxPrOption);
        command.Options.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            QueuePassResult result = await queueProcessor.RunOnceAsync(
                new QueueRunOptions
                {
                    MaxParallelJobs = Math.Max(1, parseResult.GetValue(maxParallelOption)),
                    MaxConcurrentCodexRuns = Math.Max(1, parseResult.GetValue(maxCodexOption)),
                    MaxConcurrentVerificationRuns = Math.Max(1, parseResult.GetValue(maxVerifyOption)),
                    MaxConcurrentPrOperations = Math.Max(1, parseResult.GetValue(maxPrOption)),
                    OwnerId = applicationContext.CurrentActor,
                },
                cancellationToken);

            if (parseResult.GetValue(jsonOption))
            {
                WriteJson(result);
                return OperatorExitCodes.FromQueuePassResult(result);
            }

            if (result.Jobs.Count == 0)
            {
                console.MarkupLine("[yellow]No runnable jobs.[/]");
                return OperatorExitCodes.Success;
            }

            foreach (QueueJobResult job in result.Jobs)
            {
                console.MarkupLine($"{Markup.Escape(job.JobId)} [blue]{job.Disposition}[/] {Markup.Escape(job.Summary)}");
            }

            return OperatorExitCodes.FromQueuePassResult(result);
        });

        return command;
    }

    private Command CreateQueueRunCommand()
    {
        Command command = new("run", "Start the long-running worker loop.");
        Option<int> maxParallelOption = new("--max-parallel") { Description = "Maximum jobs to run concurrently.", DefaultValueFactory = _ => 1 };
        Option<int> maxCodexOption = new("--max-codex") { Description = "Maximum concurrent Codex-backed stages.", DefaultValueFactory = _ => 1 };
        Option<int> maxVerifyOption = new("--max-verify") { Description = "Maximum concurrent verification runs.", DefaultValueFactory = _ => 1 };
        Option<int> maxPrOption = new("--max-pr") { Description = "Maximum concurrent PR operations.", DefaultValueFactory = _ => 1 };
        Option<int> pollIntervalMsOption = new("--poll-interval-ms") { Description = "Delay between idle scheduling passes.", DefaultValueFactory = _ => 2000 };

        command.Options.Add(maxParallelOption);
        command.Options.Add(maxCodexOption);
        command.Options.Add(maxVerifyOption);
        command.Options.Add(maxPrOption);
        command.Options.Add(pollIntervalMsOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                await queueProcessor.RunAsync(
                    new QueueRunOptions
                    {
                        MaxParallelJobs = Math.Max(1, parseResult.GetValue(maxParallelOption)),
                        MaxConcurrentCodexRuns = Math.Max(1, parseResult.GetValue(maxCodexOption)),
                        MaxConcurrentVerificationRuns = Math.Max(1, parseResult.GetValue(maxVerifyOption)),
                        MaxConcurrentPrOperations = Math.Max(1, parseResult.GetValue(maxPrOption)),
                        PollInterval = TimeSpan.FromMilliseconds(Math.Max(100, parseResult.GetValue(pollIntervalMsOption))),
                        OwnerId = applicationContext.CurrentActor,
                    },
                    cancellationToken);
                return OperatorExitCodes.Success;
            }
            catch (OperationCanceledException)
            {
                return OperatorExitCodes.Success;
            }
            catch (Exception exception) when (IsOperatorError(exception))
            {
                return WriteOperatorError(exception);
            }
        });

        return command;
    }

    private Command CreateJobResumeCommand()
    {
        Command command = new("resume", "Resume a blocked or interrupted job.");
        Argument<string> jobIdArgument = new("job-id") { Description = "Job identifier." };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Arguments.Add(jobIdArgument);
        command.Options.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string jobId = parseResult.GetValue(jobIdArgument)
                ?? throw new InvalidOperationException("The required job identifier was not provided.");

            try
            {
                JobActionResult result = await jobOperatorService.ResumeAsync(jobId, cancellationToken);
                if (parseResult.GetValue(jsonOption))
                {
                    WriteJson(result);
                }
                else
                {
                    console.MarkupLine($"[green]{Markup.Escape(result.Summary)}[/]");
                }

                return OperatorExitCodes.FromJobStatus(result.Status);
            }
            catch (Exception exception) when (IsOperatorError(exception))
            {
                return WriteOperatorError(exception);
            }
        });

        return command;
    }

    private Command CreateJobUnblockCommand()
    {
        Command command = new("unblock", "Store human input and resume a blocked job.");
        Argument<string> jobIdArgument = new("job-id") { Description = "Job identifier." };
        Option<string> messageOption = new("--message") { Description = "Human guidance to store durably.", Required = true };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Arguments.Add(jobIdArgument);
        command.Options.Add(messageOption);
        command.Options.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string jobId = parseResult.GetValue(jobIdArgument)
                ?? throw new InvalidOperationException("The required job identifier was not provided.");
            string message = parseResult.GetValue(messageOption)
                ?? throw new InvalidOperationException("The required --message option was not provided.");

            try
            {
                JobActionResult result = await jobOperatorService.UnblockAsync(jobId, message, cancellationToken);
                if (parseResult.GetValue(jsonOption))
                {
                    WriteJson(result);
                }
                else
                {
                    console.MarkupLine($"[green]{Markup.Escape(result.Summary)}[/]");
                }

                return OperatorExitCodes.FromJobStatus(result.Status);
            }
            catch (Exception exception) when (IsOperatorError(exception))
            {
                return WriteOperatorError(exception);
            }
        });

        return command;
    }

    private Command CreateJobCancelCommand()
    {
        Command command = new("cancel", "Cancel a queued or blocked job.");
        Argument<string> jobIdArgument = new("job-id") { Description = "Job identifier." };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Arguments.Add(jobIdArgument);
        command.Options.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string jobId = parseResult.GetValue(jobIdArgument)
                ?? throw new InvalidOperationException("The required job identifier was not provided.");

            try
            {
                JobActionResult result = await jobOperatorService.CancelAsync(jobId, cancellationToken);
                if (parseResult.GetValue(jsonOption))
                {
                    WriteJson(result);
                }
                else
                {
                    console.MarkupLine($"[yellow]{Markup.Escape(result.Summary)}[/]");
                }

                return OperatorExitCodes.FromJobStatus(result.Status);
            }
            catch (Exception exception) when (IsOperatorError(exception))
            {
                return WriteOperatorError(exception);
            }
        });

        return command;
    }

    private Command CreatePrOpenCommand()
    {
        Command command = new("open", "Open the pull request for a ready job.");
        Argument<string> jobIdArgument = new("job-id") { Description = "Job identifier." };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Arguments.Add(jobIdArgument);
        command.Options.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string jobId = parseResult.GetValue(jobIdArgument)
                ?? throw new InvalidOperationException("The required job identifier was not provided.");

            try
            {
                PullRequestOperationResult result = await pullRequestService.PublishAsync(jobId, cancellationToken);

                if (parseResult.GetValue(jsonOption))
                {
                    WriteJson(result);
                    return OperatorExitCodes.FromPullRequestResult(result);
                }

                if (result.Published)
                {
                    console.MarkupLine($"[green]{Markup.Escape(result.Summary)}[/]");
                    if (!string.IsNullOrWhiteSpace(result.Url))
                    {
                        console.MarkupLine($"URL: {Markup.Escape(result.Url)}");
                    }
                    return OperatorExitCodes.FromPullRequestResult(result);
                }

                console.MarkupLine($"[red]{Markup.Escape(result.Summary)}[/]");
                return OperatorExitCodes.FromPullRequestResult(result);
            }
            catch (Exception exception) when (IsOperatorError(exception))
            {
                return WriteOperatorError(exception);
            }
        });

        return command;
    }

    private Command CreateWorktreeOpenCommand()
    {
        Command command = new("open", "Print the job worktree path.");
        Argument<string> jobIdArgument = new("job-id") { Description = "Job identifier." };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Arguments.Add(jobIdArgument);
        command.Options.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string jobId = parseResult.GetValue(jobIdArgument)
                ?? throw new InvalidOperationException("The required job identifier was not provided.");
            StoredJob? storedJob = await jobRepository.GetAsync(jobId, cancellationToken);

            if (storedJob is null)
            {
                console.MarkupLine($"[red]Job not found:[/] {Markup.Escape(jobId)}");
                return OperatorExitCodes.ToolOrConfigError;
            }

            string? worktreePath = storedJob.Status.Git?.WorktreePath;
            bool exists = !string.IsNullOrWhiteSpace(worktreePath) && Directory.Exists(worktreePath);

            if (parseResult.GetValue(jsonOption))
            {
                WriteJson(new
                {
                    storedJob.Definition.JobId,
                    WorktreePath = worktreePath,
                    Exists = exists,
                });
                return exists ? OperatorExitCodes.Success : OperatorExitCodes.ToolOrConfigError;
            }

            if (!exists)
            {
                console.MarkupLine($"[red]No live worktree is available for[/] {Markup.Escape(jobId)}");
                if (!string.IsNullOrWhiteSpace(worktreePath))
                {
                    console.MarkupLine($"Last recorded path: [grey]{Markup.Escape(worktreePath)}[/]");
                }
                return OperatorExitCodes.ToolOrConfigError;
            }

            bool opened = TryOpenPath(worktreePath!);
            if (!opened)
            {
                console.MarkupLine($"[yellow]Unable to open a graphical file manager.[/] Copy-paste this command:");
                console.MarkupLine($"[dim]{Markup.Escape(GetOpenShellCommand(worktreePath!))}[/]");
            }

            console.MarkupLine(Markup.Escape(worktreePath!));
            return OperatorExitCodes.Success;
        });

        return command;
    }

    private Command CreateCleanupCommand()
    {
        Command command = new("cleanup", "Prune expired worktrees and old artifacts.");
        Option<int> retainDaysOption = new("--retain-days") { Description = "Days to retain finished worktrees.", DefaultValueFactory = _ => 7 };
        Option<int> artifactRetainDaysOption = new("--artifact-retain-days") { Description = "Days to retain run log artifacts.", DefaultValueFactory = _ => 30 };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Options.Add(retainDaysOption);
        command.Options.Add(artifactRetainDaysOption);
        command.Options.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            CleanupResult result = await cleanupService.RunAsync(
                new CleanupOptions
                {
                    WorktreeRetention = TimeSpan.FromDays(Math.Max(0, parseResult.GetValue(retainDaysOption))),
                    ArtifactRetention = TimeSpan.FromDays(Math.Max(0, parseResult.GetValue(artifactRetainDaysOption))),
                },
                cancellationToken);

            if (parseResult.GetValue(jsonOption))
            {
                WriteJson(result);
                return OperatorExitCodes.FromCleanupResult(result);
            }

            if (result.Jobs.Count == 0)
            {
                console.MarkupLine("[yellow]No jobs available for cleanup.[/]");
                return OperatorExitCodes.Success;
            }

            foreach (JobCleanupResult job in result.Jobs)
            {
                console.MarkupLine($"{Markup.Escape(job.JobId)} [blue]{job.Disposition}[/] {Markup.Escape(job.Summary)}");
            }

            return OperatorExitCodes.FromCleanupResult(result);
        });

        return command;
    }

    private void WriteJson<T>(T value)
    {
        console.Write(new Text(RingmasterJsonSerializer.Serialize(value)));
        console.WriteLine();
    }

    private void RenderStatusDashboard(IReadOnlyList<StatusDisplayItem> snapshot)
    {
        console.Clear();

        if (snapshot.Count == 0)
        {
            console.MarkupLine("[yellow]No jobs found.[/]");
            return;
        }

        Table table = new();
        table.AddColumn("Job Id");
        table.AddColumn("State");
        table.AddColumn("Stage");
        table.AddColumn("Run");
        table.AddColumn("Elapsed");
        table.AddColumn("Retries");
        table.AddColumn("Last Failure");
        table.AddColumn("PR");
        table.AddColumn("Title");

        foreach (StatusDisplayItem item in snapshot)
        {
            table.AddRow(
                item.JobId,
                item.State.ToString(),
                item.CurrentStage?.ToString() ?? "-",
                item.ActiveRunId ?? "-",
                item.Elapsed is { } elapsed ? elapsed.ToString(@"hh\:mm\:ss") : "-",
                item.RetryCount.ToString(),
                Truncate(item.LastFailureSummary, 40),
                Truncate(item.PullRequestUrl, 40),
                Truncate(item.Title, 40));
        }

        console.Write(table);
    }

    private int WriteOperatorError(Exception exception)
    {
        console.MarkupLine($"[red]{Markup.Escape(exception.Message)}[/]");
        return OperatorExitCodes.ToolOrConfigError;
    }

    private static bool IsOperatorError(Exception exception)
    {
        return exception is InvalidOperationException
            or IOException
            or FileNotFoundException
            or InvalidDataException;
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static bool TryOpenPath(string path)
    {
        try
        {
            Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
            if (process is not null)
            {
                process.Dispose();
                return true;
            }

            return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static string GetOpenShellCommand(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return $"explorer \"{path}\"";
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"open \"{path}\"";
        }

        return $"xdg-open \"{path}\"";
    }
}
