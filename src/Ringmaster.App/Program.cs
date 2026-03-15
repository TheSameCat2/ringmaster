using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ringmaster.App;
using Ringmaster.App.CommandLine;
using Ringmaster.Core.Jobs;
using Ringmaster.Infrastructure.Fakes;
using Ringmaster.Infrastructure.Persistence;
using Spectre.Console;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
string repositoryRoot = Path.GetFullPath(builder.Environment.ContentRootPath);

builder.Services.AddSingleton<IAnsiConsole>(_ => AnsiConsole.Console);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new RingmasterApplicationContext(repositoryRoot, Environment.UserName));
builder.Services.AddSingleton<JobSnapshotRebuilder>();
builder.Services.AddSingleton<AtomicFileWriter>();
builder.Services.AddSingleton<JobEventLogStore>();
builder.Services.AddSingleton<IJobIdGenerator, DefaultJobIdGenerator>();
builder.Services.AddSingleton<IJobRepository>(serviceProvider =>
{
    RingmasterApplicationContext applicationContext = serviceProvider.GetRequiredService<RingmasterApplicationContext>();
    return new LocalFilesystemJobRepository(
        applicationContext.RepositoryRoot,
        serviceProvider.GetRequiredService<TimeProvider>(),
        serviceProvider.GetRequiredService<IJobIdGenerator>(),
        serviceProvider.GetRequiredService<AtomicFileWriter>(),
        serviceProvider.GetRequiredService<JobEventLogStore>(),
        serviceProvider.GetRequiredService<JobSnapshotRebuilder>());
});
builder.Services.AddSingleton<IStateMachine, RingmasterStateMachine>();
builder.Services.AddSingleton<IStageRunner>(_ => new FakeStageRunner(JobStage.PREPARING, StageRole.Planner, JobState.IMPLEMENTING, "Planner completed."));
builder.Services.AddSingleton<IStageRunner>(_ => new FakeStageRunner(JobStage.IMPLEMENTING, StageRole.Implementer, JobState.VERIFYING, "Implementer completed."));
builder.Services.AddSingleton<IStageRunner>(_ => new FakeStageRunner(JobStage.VERIFYING, StageRole.SystemVerifier, JobState.REVIEWING, "Verifier completed."));
builder.Services.AddSingleton<IStageRunner>(_ => new FakeStageRunner(JobStage.REPAIRING, StageRole.Implementer, JobState.VERIFYING, "Repair completed."));
builder.Services.AddSingleton<IStageRunner>(_ => new FakeStageRunner(JobStage.REVIEWING, StageRole.Reviewer, JobState.READY_FOR_PR, "Reviewer approved."));
builder.Services.AddSingleton<JobEngine>();
builder.Services.AddSingleton<RingmasterCli>();

using IHost host = builder.Build();

RingmasterCli cli = host.Services.GetRequiredService<RingmasterCli>();
return cli.CreateRootCommand().Parse(args).Invoke();
