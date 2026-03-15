using System.CommandLine;
using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Spectre.Console;

namespace Ringmaster.App.CommandLine;

public sealed class RingmasterCli(
    IAnsiConsole console,
    IJobRepository jobRepository,
    JobEngine jobEngine,
    QueueProcessor queueProcessor,
    RingmasterApplicationContext applicationContext)
{
    public RootCommand CreateRootCommand()
    {
        RootCommand rootCommand = new("Durable Codex orchestration for git-backed engineering work.");

        rootCommand.Subcommands.Add(CreatePlaceholderCommand("init", "Initialize local Ringmaster runtime layout."));
        rootCommand.Subcommands.Add(CreatePlaceholderCommand("doctor", "Check local prerequisites and repo health."));
        rootCommand.Subcommands.Add(CreateJobCommand());
        rootCommand.Subcommands.Add(CreateQueueCommand());
        rootCommand.Subcommands.Add(CreateStatusCommand());
        rootCommand.Subcommands.Add(CreatePlaceholderCommand("logs", "Inspect stored run logs."));
        rootCommand.Subcommands.Add(CreatePrCommand());
        rootCommand.Subcommands.Add(CreateWorktreeCommand());
        rootCommand.Subcommands.Add(CreatePlaceholderCommand("cleanup", "Prune expired worktrees and old artifacts."));

        rootCommand.SetAction(_ =>
        {
            console.MarkupLine("[bold green]Ringmaster[/] coordinates Codex-driven repository work.");
            console.MarkupLine("Run [yellow]ringmaster --help[/] or inspect a subcommand for available operations.");
        });

        return rootCommand;
    }

    private Command CreateJobCommand()
    {
        Command command = new("job", "Create, inspect, and run individual jobs.");
        command.Subcommands.Add(CreateJobCreateCommand());
        command.Subcommands.Add(CreateJobShowCommand());
        command.Subcommands.Add(CreateJobRunCommand());
        command.Subcommands.Add(CreatePlaceholderCommand("resume", "Resume a blocked or interrupted job."));
        command.Subcommands.Add(CreatePlaceholderCommand("unblock", "Store human input and resume a blocked job."));
        command.Subcommands.Add(CreatePlaceholderCommand("cancel", "Cancel a queued or blocked job."));
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
        command.Subcommands.Add(CreatePlaceholderCommand("open", "Open the pull request for a ready job."));
        return command;
    }

    private Command CreateWorktreeCommand()
    {
        Command command = new("worktree", "Inspect or open job worktrees.");
        command.Subcommands.Add(CreatePlaceholderCommand("open", "Print or open a job worktree path."));
        return command;
    }

    private Command CreatePlaceholderCommand(string name, string description)
    {
        Command command = new(name, description);
        command.SetAction(_ => console.MarkupLine($"[yellow]{name}[/] is not implemented yet."));
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
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Options.Add(titleOption);
        command.Options.Add(descriptionOption);
        command.Options.Add(taskFileOption);
        command.Options.Add(acceptanceOption);
        command.Options.Add(verifyProfileOption);
        command.Options.Add(baseBranchOption);
        command.Options.Add(priorityOption);
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

                return 0;
            }

            console.MarkupLine($"[green]Created job[/] [bold]{Markup.Escape(storedJob.Definition.JobId)}[/]");
            console.MarkupLine($"Path: [grey]{Markup.Escape(storedJob.JobDirectoryPath)}[/]");
            console.MarkupLine($"State: [blue]{storedJob.Status.State}[/]");
            return 0;
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
                return 1;
            }

            if (parseResult.GetValue(jsonOption))
            {
                WriteJson(storedJob);
                return 0;
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
            return 0;
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

                if (parseResult.GetValue(jsonOption))
                {
                    WriteJson(status);
                    return 0;
                }

                console.MarkupLine($"[green]Job finished in state[/] [blue]{status.State}[/]");
                return 0;
            }
            catch (InvalidOperationException exception)
            {
                console.MarkupLine($"[red]{Markup.Escape(exception.Message)}[/]");
                return 1;
            }
        });

        return command;
    }

    private Command CreateStatusCommand()
    {
        Command command = new("status", "Show job or queue status.");
        Option<string?> jobIdOption = new("--job-id") { Description = "Show a single job status." };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Options.Add(jobIdOption);
        command.Options.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string? jobId = parseResult.GetValue(jobIdOption);
            bool emitJson = parseResult.GetValue(jsonOption);

            if (!string.IsNullOrWhiteSpace(jobId))
            {
                StoredJob? storedJob = await jobRepository.GetAsync(jobId, cancellationToken);
                if (storedJob is null)
                {
                    console.MarkupLine($"[red]Job not found:[/] {Markup.Escape(jobId)}");
                    return 1;
                }

                if (emitJson)
                {
                    WriteJson(storedJob.Status);
                    return 0;
                }

                console.MarkupLine($"[bold]{Markup.Escape(storedJob.Status.JobId)}[/] [blue]{storedJob.Status.State}[/] {Markup.Escape(storedJob.Status.Title)}");
                return 0;
            }

            IReadOnlyList<JobStatusListItem> jobs = await jobRepository.ListAsync(cancellationToken);
            if (emitJson)
            {
                WriteJson(jobs);
                return 0;
            }

            if (jobs.Count == 0)
            {
                console.MarkupLine("[yellow]No jobs found.[/]");
                return 0;
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
            return 0;
        });

        return command;
    }

    private void WriteJson<T>(T value)
    {
        console.Write(new Text(RingmasterJsonSerializer.Serialize(value)));
        console.WriteLine();
    }

    private Command CreateQueueOnceCommand()
    {
        Command command = new("once", "Run one scheduling pass.");
        Option<int> maxParallelOption = new("--max-parallel") { Description = "Maximum jobs to start in this pass.", DefaultValueFactory = _ => 1 };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON output." };

        command.Options.Add(maxParallelOption);
        command.Options.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            QueuePassResult result = await queueProcessor.RunOnceAsync(
                new QueueRunOptions
                {
                    MaxParallelJobs = Math.Max(1, parseResult.GetValue(maxParallelOption)),
                    OwnerId = applicationContext.CurrentActor,
                },
                cancellationToken);

            if (parseResult.GetValue(jsonOption))
            {
                WriteJson(result);
                return 0;
            }

            if (result.Jobs.Count == 0)
            {
                console.MarkupLine("[yellow]No runnable jobs.[/]");
                return 0;
            }

            foreach (QueueJobResult job in result.Jobs)
            {
                console.MarkupLine($"{Markup.Escape(job.JobId)} [blue]{job.Disposition}[/] {Markup.Escape(job.Summary)}");
            }

            return 0;
        });

        return command;
    }

    private Command CreateQueueRunCommand()
    {
        Command command = new("run", "Start the long-running worker loop.");
        Option<int> maxParallelOption = new("--max-parallel") { Description = "Maximum jobs to run concurrently.", DefaultValueFactory = _ => 1 };
        Option<int> pollIntervalMsOption = new("--poll-interval-ms") { Description = "Delay between idle scheduling passes.", DefaultValueFactory = _ => 2000 };

        command.Options.Add(maxParallelOption);
        command.Options.Add(pollIntervalMsOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                await queueProcessor.RunAsync(
                    new QueueRunOptions
                    {
                        MaxParallelJobs = Math.Max(1, parseResult.GetValue(maxParallelOption)),
                        PollInterval = TimeSpan.FromMilliseconds(Math.Max(100, parseResult.GetValue(pollIntervalMsOption))),
                        OwnerId = applicationContext.CurrentActor,
                    },
                    cancellationToken);
                return 0;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            catch (InvalidOperationException exception)
            {
                console.MarkupLine($"[red]{Markup.Escape(exception.Message)}[/]");
                return 1;
            }
        });

        return command;
    }
}
