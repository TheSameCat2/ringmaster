using Ringmaster.App.CommandLine;
using Spectre.Console;

namespace Ringmaster.IntegrationTests;

public sealed class RingmasterCliTests
{
    [Fact]
    public void RootCommandExposesThePlannedTopLevelCommands()
    {
        RingmasterCli cli = new(AnsiConsole.Console);
        string[] commandNames = cli.CreateRootCommand().Subcommands.Select(command => command.Name).ToArray();

        Assert.Equal(
            ["init", "doctor", "job", "queue", "status", "logs", "pr", "worktree", "cleanup"],
            commandNames);
    }

    [Fact]
    public void JobCommandExposesThePlannedSubcommands()
    {
        RingmasterCli cli = new(AnsiConsole.Console);
        string[] commandNames = cli.CreateRootCommand()
            .Subcommands
            .Single(command => command.Name == "job")
            .Subcommands
            .Select(command => command.Name)
            .ToArray();

        Assert.Equal(
            ["create", "show", "run", "resume", "unblock", "cancel"],
            commandNames);
    }
}
