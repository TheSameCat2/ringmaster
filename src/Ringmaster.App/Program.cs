using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ringmaster.App.CommandLine;
using Spectre.Console;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<IAnsiConsole>(_ => AnsiConsole.Console);
builder.Services.AddSingleton<RingmasterCli>();

using IHost host = builder.Build();

RingmasterCli cli = host.Services.GetRequiredService<RingmasterCli>();
return cli.CreateRootCommand().Parse(args).Invoke();
