using System.CommandLine;
using Spectre.Console;

namespace Ringmaster.App.CommandLine;

public sealed class RingmasterCli(IAnsiConsole console)
{
    public RootCommand CreateRootCommand()
    {
        RootCommand rootCommand = new("Durable Codex orchestration for git-backed engineering work.");

        rootCommand.Subcommands.Add(CreatePlaceholderCommand("init", "Initialize local Ringmaster runtime layout."));
        rootCommand.Subcommands.Add(CreatePlaceholderCommand("doctor", "Check local prerequisites and repo health."));
        rootCommand.Subcommands.Add(CreateJobCommand());
        rootCommand.Subcommands.Add(CreateQueueCommand());
        rootCommand.Subcommands.Add(CreatePlaceholderCommand("status", "Show job or queue status."));
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
        command.Subcommands.Add(CreatePlaceholderCommand("create", "Create a durable queued job."));
        command.Subcommands.Add(CreatePlaceholderCommand("show", "Show one job in detail."));
        command.Subcommands.Add(CreatePlaceholderCommand("run", "Run one job synchronously."));
        command.Subcommands.Add(CreatePlaceholderCommand("resume", "Resume a blocked or interrupted job."));
        command.Subcommands.Add(CreatePlaceholderCommand("unblock", "Store human input and resume a blocked job."));
        command.Subcommands.Add(CreatePlaceholderCommand("cancel", "Cancel a queued or blocked job."));
        return command;
    }

    private Command CreateQueueCommand()
    {
        Command command = new("queue", "Run the scheduler loop or a single scheduling pass.");
        command.Subcommands.Add(CreatePlaceholderCommand("run", "Start the long-running worker loop."));
        command.Subcommands.Add(CreatePlaceholderCommand("once", "Run one scheduling pass."));
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
}
