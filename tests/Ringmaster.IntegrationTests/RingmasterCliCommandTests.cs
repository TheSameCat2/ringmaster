using Ringmaster.App;
using Ringmaster.App.CommandLine;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.IntegrationTests.Testing;
using Spectre.Console.Testing;

namespace Ringmaster.IntegrationTests;

public sealed class RingmasterCliCommandTests
{
    [Fact]
    public void JobCreateShowAndStatusCommandsWorkAgainstTheFilesystemRepository()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DateTimeOffset createdAt = new(2026, 3, 15, 16, 45, 0, TimeSpan.Zero);
        TestConsole console = new();
        RingmasterCli cli = new(
            console,
            new LocalFilesystemJobRepository(
                temporaryDirectory.Path,
                new StaticTimeProvider(createdAt),
                new FixedJobIdGenerator("job-20260315-7f3c9b2a"),
                new AtomicFileWriter(),
                new JobEventLogStore(),
                new Ringmaster.Core.Jobs.JobSnapshotRebuilder()),
            new RingmasterApplicationContext(temporaryDirectory.Path, "tester"));

        int createExitCode = cli.CreateRootCommand().Parse(
            ["job", "create", "--title", "Add retry handling", "--description", "Implement bounded retries.", "--json"]).Invoke();
        int showExitCode = cli.CreateRootCommand().Parse(
            ["job", "show", "job-20260315-7f3c9b2a", "--json"]).Invoke();
        int statusExitCode = cli.CreateRootCommand().Parse(
            ["status", "--json"]).Invoke();

        string output = console.Output;

        Assert.Equal(0, createExitCode);
        Assert.Equal(0, showExitCode);
        Assert.Equal(0, statusExitCode);
        Assert.Contains("\"jobId\": \"job-20260315-7f3c9b2a\"", output, StringComparison.Ordinal);
        Assert.Contains("\"state\": \"QUEUED\"", output, StringComparison.Ordinal);
    }
}
