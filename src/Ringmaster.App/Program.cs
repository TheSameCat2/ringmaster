using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ringmaster.App;
using Ringmaster.App.CommandLine;
using Ringmaster.Codex;
using Ringmaster.Core;
using Ringmaster.Core.Configuration;
using Ringmaster.Core.Jobs;
using Ringmaster.Git;
using Ringmaster.GitHub;
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
builder.Services.AddSingleton<ILeaseManager>(serviceProvider =>
{
    RingmasterApplicationContext applicationContext = serviceProvider.GetRequiredService<RingmasterApplicationContext>();
    return new FileLeaseManager(
        applicationContext.RepositoryRoot,
        serviceProvider.GetRequiredService<AtomicFileWriter>(),
        serviceProvider.GetRequiredService<TimeProvider>());
});
builder.Services.AddSingleton<IQueueSelector, LocalFilesystemQueueSelector>();
builder.Services.AddSingleton<GitCli>();
builder.Services.AddSingleton<GitWorktreeManager>();
builder.Services.AddSingleton<IPullRequestProvider, GitHubPullRequestProvider>();
builder.Services.AddSingleton<IPullRequestService, PullRequestService>();
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
builder.Services.AddSingleton<IFailureClassifier, DeterministicFailureClassifier>();
builder.Services.AddSingleton(new RepairLoopPolicy());
builder.Services.AddSingleton<RepairLoopPolicyEvaluator>();
builder.Services.AddSingleton<PullRequestDraftBuilder>();
builder.Services.AddSingleton<DoctorService>(serviceProvider =>
{
    RingmasterApplicationContext applicationContext = serviceProvider.GetRequiredService<RingmasterApplicationContext>();
    return new DoctorService(
        applicationContext.RepositoryRoot,
        serviceProvider.GetRequiredService<IExternalProcessRunner>(),
        serviceProvider.GetRequiredService<RingmasterRepoConfigLoader>(),
        serviceProvider.GetRequiredService<GitWorktreeManager>());
});
builder.Services.AddSingleton<RepositoryInitializationService>(serviceProvider =>
{
    RingmasterApplicationContext applicationContext = serviceProvider.GetRequiredService<RingmasterApplicationContext>();
    return new RepositoryInitializationService(
        applicationContext.RepositoryRoot,
        serviceProvider.GetRequiredService<AtomicFileWriter>());
});
builder.Services.AddSingleton<JobOperatorService>();
builder.Services.AddSingleton<RunLogService>();
builder.Services.AddSingleton<StatusDisplayService>();
builder.Services.AddSingleton<CleanupService>(serviceProvider =>
{
    RingmasterApplicationContext applicationContext = serviceProvider.GetRequiredService<RingmasterApplicationContext>();
    return new CleanupService(
        applicationContext.RepositoryRoot,
        serviceProvider.GetRequiredService<IJobRepository>(),
        serviceProvider.GetRequiredService<ILeaseManager>(),
        serviceProvider.GetRequiredService<GitCli>(),
        serviceProvider.GetRequiredService<TimeProvider>());
});
builder.Services.AddSingleton<ConsoleNotificationSink>();
builder.Services.AddSingleton<JsonlNotificationSink>(serviceProvider =>
{
    RingmasterApplicationContext applicationContext = serviceProvider.GetRequiredService<RingmasterApplicationContext>();
    return new JsonlNotificationSink(applicationContext.RepositoryRoot);
});
builder.Services.AddSingleton<INotificationSink>(serviceProvider =>
{
    List<INotificationSink> sinks =
    [
        serviceProvider.GetRequiredService<ConsoleNotificationSink>(),
        serviceProvider.GetRequiredService<JsonlNotificationSink>(),
    ];

    RingmasterApplicationContext applicationContext = serviceProvider.GetRequiredService<RingmasterApplicationContext>();
    RingmasterRepoConfigLoader configLoader = serviceProvider.GetRequiredService<RingmasterRepoConfigLoader>();
    string configPath = Path.Combine(applicationContext.RepositoryRoot, ProductInfo.RepoConfigFileName);

    if (File.Exists(configPath))
    {
        try
        {
            RingmasterRepoConfig config = configLoader.LoadAsync(applicationContext.RepositoryRoot, CancellationToken.None).GetAwaiter().GetResult();
            if (config.Webhook is not null)
            {
                var validator = new WebhookUrlValidator(new WebhookUrlSecurityPolicy
                {
                    AllowLocalhost = config.Webhook.AllowLocalhost,
                    AllowPrivateAddresses = config.Webhook.AllowPrivateAddresses,
                });
                sinks.Add(new WebhookNotificationSink(config.Webhook, validator));
            }
        }
        catch
        {
            // If the repository config is unreadable or the webhook URL is invalid,
            // do not crash startup. The operator can fix the config and restart.
        }
    }

    return new CompositeNotificationSink(sinks);
});
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
        serviceProvider.GetRequiredService<TimeProvider>(),
        serviceProvider.GetRequiredService<IFailureClassifier>(),
        serviceProvider.GetRequiredService<RepairLoopPolicyEvaluator>());
});
builder.Services.AddSingleton<IStageRunner>(serviceProvider =>
{
    return new RepairingStageRunner(
        serviceProvider.GetRequiredService<IAgentRunner>(),
        serviceProvider.GetRequiredService<CodexPromptBuilder>(),
        serviceProvider.GetRequiredService<AtomicFileWriter>(),
        serviceProvider.GetRequiredService<RepairLoopPolicy>());
});
builder.Services.AddSingleton<IStageRunner>(serviceProvider =>
{
    return new ReviewingStageRunner(
        serviceProvider.GetRequiredService<IAgentRunner>(),
        serviceProvider.GetRequiredService<CodexPromptBuilder>(),
        serviceProvider.GetRequiredService<AtomicFileWriter>(),
        serviceProvider.GetRequiredService<PullRequestDraftBuilder>());
});
builder.Services.AddSingleton<JobEngine>();
builder.Services.AddSingleton<QueueProcessor>(serviceProvider =>
{
    return new QueueProcessor(
        serviceProvider.GetRequiredService<IQueueSelector>(),
        serviceProvider.GetRequiredService<ILeaseManager>(),
        serviceProvider.GetRequiredService<INotificationSink>(),
        serviceProvider.GetRequiredService<JobEngine>(),
        serviceProvider.GetRequiredService<TimeProvider>(),
        serviceProvider.GetRequiredService<IPullRequestService>());
});
builder.Services.AddSingleton<RingmasterCli>();

using IHost host = builder.Build();

RingmasterCli cli = host.Services.GetRequiredService<RingmasterCli>();
return cli.CreateRootCommand().Parse(args).Invoke();
