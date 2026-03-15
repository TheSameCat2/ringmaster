using Ringmaster.App;
using Ringmaster.App.CommandLine;
using Ringmaster.Core.Jobs;
using Ringmaster.Infrastructure.Fakes;
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
        TestConsole console = new();
        RingmasterCli cli = CreateCli(console, temporaryDirectory.Path);

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

    [Fact]
    public void JobRunCommandUsesFakeStageRunnersToReachReadyForPr()
    {
        using TemporaryDirectory temporaryDirectory = new();
        TestConsole console = new();
        RingmasterCli cli = CreateCli(console, temporaryDirectory.Path);

        int createExitCode = cli.CreateRootCommand().Parse(
            ["job", "create", "--title", "Add retry handling", "--description", "Implement bounded retries.", "--json"]).Invoke();
        int runExitCode = cli.CreateRootCommand().Parse(
            ["job", "run", "job-20260315-7f3c9b2a", "--json"]).Invoke();

        string output = console.Output;

        Assert.Equal(0, createExitCode);
        Assert.Equal(0, runExitCode);
        Assert.Contains("\"state\": \"READY_FOR_PR\"", output, StringComparison.Ordinal);
        Assert.Contains("\"attempts\": {", output, StringComparison.Ordinal);
    }

    private static RingmasterCli CreateCli(TestConsole console, string repositoryRoot)
    {
        DateTimeOffset createdAt = new(2026, 3, 15, 16, 45, 0, TimeSpan.Zero);
        StaticTimeProvider timeProvider = new(createdAt);
        LocalFilesystemJobRepository repository = new(
            repositoryRoot,
            timeProvider,
            new FixedJobIdGenerator("job-20260315-7f3c9b2a"),
            new AtomicFileWriter(),
            new JobEventLogStore(),
            new JobSnapshotRebuilder());
        JobEngine jobEngine = new(
            repository,
            new RingmasterStateMachine(),
            [
                new FakeStageRunner(JobStage.PREPARING, StageRole.Planner, JobState.IMPLEMENTING, "Planner completed."),
                new FakeStageRunner(JobStage.IMPLEMENTING, StageRole.Implementer, JobState.VERIFYING, "Implementer completed."),
                new FakeStageRunner(JobStage.VERIFYING, StageRole.SystemVerifier, JobState.REVIEWING, "Verifier completed."),
                new FakeStageRunner(JobStage.REPAIRING, StageRole.Implementer, JobState.VERIFYING, "Repair completed."),
                new FakeStageRunner(JobStage.REVIEWING, StageRole.Reviewer, JobState.READY_FOR_PR, "Reviewer approved."),
            ],
            timeProvider);

        return new RingmasterCli(console, repository, jobEngine, new RingmasterApplicationContext(repositoryRoot, "tester"));
    }
}
