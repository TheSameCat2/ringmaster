using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ringmaster.App;
using Ringmaster.App.CommandLine;
using Ringmaster.Codex;
using Ringmaster.Core.Jobs;
using Ringmaster.Git;
using Ringmaster.Infrastructure.Fakes;
using Ringmaster.Infrastructure.Configuration;
using Ringmaster.Infrastructure.Persistence;
using Ringmaster.Infrastructure.Processes;
using Spectre.Console;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
string repositoryRoot = Path.GetFullPath(builder.Environment.ContentRootPath);

builder.Services.AddSingleton<IAnsiConsole>(_ => AnsiConsole.Console);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new RingmasterApplicationContext(repositoryRoot, Environment.UserName));
builder.Services.AddSingleton<JobSnapshotRebuilder>();
builder.Services.AddSingleton<AtomicFileWriter>();
builder.Services.AddSingleton<JobEventLogStore>();
builder.Services.AddSingleton<RingmasterRepoConfigLoader>();
builder.Services.AddSingleton<ExternalProcessRunner>();
builder.Services.AddSingleton<IExternalProcessRunner>(serviceProvider => serviceProvider.GetRequiredService<ExternalProcessRunner>());
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
builder.Services.AddSingleton<GitCli>();
builder.Services.AddSingleton<GitWorktreeManager>();
builder.Services.AddSingleton<RepositoryPreparationService>(serviceProvider =>
{
    RingmasterApplicationContext applicationContext = serviceProvider.GetRequiredService<RingmasterApplicationContext>();
    return new RepositoryPreparationService(
        applicationContext.RepositoryRoot,
        serviceProvider.GetRequiredService<RingmasterRepoConfigLoader>(),
        serviceProvider.GetRequiredService<GitWorktreeManager>(),
        serviceProvider.GetRequiredService<IJobRepository>(),
        serviceProvider.GetRequiredService<TimeProvider>());
});
builder.Services.AddSingleton<ICodexRunner, CodexExecRunner>();
builder.Services.AddSingleton<IAgentRunner, CodexAgentRunner>();
builder.Services.AddSingleton<CodexPromptBuilder>();
builder.Services.AddSingleton<IStateMachine, RingmasterStateMachine>();
builder.Services.AddSingleton<IStageRunner>(serviceProvider =>
{
    return new PlanningStageRunner(
        serviceProvider.GetRequiredService<RepositoryPreparationService>(),
        serviceProvider.GetRequiredService<IAgentRunner>(),
        serviceProvider.GetRequiredService<CodexPromptBuilder>(),
        serviceProvider.GetRequiredService<AtomicFileWriter>());
});
builder.Services.AddSingleton<IStageRunner>(serviceProvider =>
{
    return new ImplementingStageRunner(
        serviceProvider.GetRequiredService<IAgentRunner>(),
        serviceProvider.GetRequiredService<CodexPromptBuilder>(),
        serviceProvider.GetRequiredService<AtomicFileWriter>());
});
builder.Services.AddSingleton<IStageRunner>(serviceProvider =>
{
    RingmasterApplicationContext applicationContext = serviceProvider.GetRequiredService<RingmasterApplicationContext>();
    return new VerifyingStageRunner(
        applicationContext.RepositoryRoot,
        serviceProvider.GetRequiredService<RingmasterRepoConfigLoader>(),
        serviceProvider.GetRequiredService<ExternalProcessRunner>(),
        serviceProvider.GetRequiredService<GitCli>(),
        serviceProvider.GetRequiredService<GitWorktreeManager>(),
        serviceProvider.GetRequiredService<AtomicFileWriter>(),
        serviceProvider.GetRequiredService<IJobRepository>(),
        serviceProvider.GetRequiredService<TimeProvider>());
});
builder.Services.AddSingleton<IStageRunner>(_ => new FakeStageRunner(JobStage.REPAIRING, StageRole.Implementer, JobState.VERIFYING, "Repair completed."));
builder.Services.AddSingleton<IStageRunner>(_ => new FakeStageRunner(JobStage.REVIEWING, StageRole.Reviewer, JobState.READY_FOR_PR, "Reviewer approved."));
builder.Services.AddSingleton<JobEngine>();
builder.Services.AddSingleton<RingmasterCli>();

using IHost host = builder.Build();

RingmasterCli cli = host.Services.GetRequiredService<RingmasterCli>();
return cli.CreateRootCommand().Parse(args).Invoke();
