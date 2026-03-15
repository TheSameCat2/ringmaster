using Ringmaster.Core;
using Ringmaster.Core.Configuration;
using Ringmaster.Infrastructure.Persistence;

namespace Ringmaster.App;

public sealed class RepositoryInitializationService(
    string repositoryRoot,
    AtomicFileWriter atomicFileWriter)
{
    private readonly string _repositoryRoot = Path.GetFullPath(repositoryRoot);

    public async Task<RepositoryInitializationResult> InitializeAsync(
        RepositoryInitializationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        string provider = string.IsNullOrWhiteSpace(options.PullRequestProvider)
            ? "github"
            : options.PullRequestProvider.Trim();
        if (!string.Equals(provider, "github", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Pull request provider '{provider}' is not supported. Ringmaster currently supports only 'github'.");
        }

        string ringmasterRoot = Path.Combine(_repositoryRoot, ProductInfo.RuntimeDirectoryName);
        string runtimeRoot = Path.Combine(ringmasterRoot, "runtime");
        string jobsRoot = Path.Combine(ringmasterRoot, "jobs");
        Directory.CreateDirectory(ringmasterRoot);
        Directory.CreateDirectory(runtimeRoot);
        Directory.CreateDirectory(jobsRoot);

        string notificationsPath = Path.Combine(runtimeRoot, "notifications.jsonl");
        bool notificationsCreated = false;
        if (!File.Exists(notificationsPath))
        {
            await atomicFileWriter.WriteTextAsync(notificationsPath, string.Empty, cancellationToken);
            notificationsCreated = true;
        }

        bool gitIgnoreUpdated = await EnsureGitIgnoreEntryAsync(".ringmaster/", cancellationToken);

        string configPath = Path.Combine(_repositoryRoot, ProductInfo.RepoConfigFileName);
        bool configCreated = false;
        bool verificationCommandsScaffolded = false;
        string? solutionPath = null;

        if (!File.Exists(configPath))
        {
            solutionPath = FindSingleRootSolutionPath();
            RingmasterRepoConfig config = CreateInitialConfig(options.BaseBranch, solutionPath);
            verificationCommandsScaffolded = config.VerificationProfiles.TryGetValue("default", out VerificationProfileDefinition? profile)
                && profile.Commands.Count > 0;
            await atomicFileWriter.WriteJsonAsync(configPath, config, cancellationToken);
            configCreated = true;
        }

        return new RepositoryInitializationResult
        {
            RepositoryRoot = _repositoryRoot,
            RuntimeRoot = runtimeRoot,
            JobsRoot = jobsRoot,
            ConfigPath = configPath,
            ConfigCreated = configCreated,
            NotificationsCreated = notificationsCreated,
            GitIgnoreUpdated = gitIgnoreUpdated,
            PullRequestProvider = "github",
            VerificationCommandsScaffolded = verificationCommandsScaffolded,
            SolutionPath = solutionPath,
        };
    }

    private async Task<bool> EnsureGitIgnoreEntryAsync(string entry, CancellationToken cancellationToken)
    {
        string gitIgnorePath = Path.Combine(_repositoryRoot, ".gitignore");
        string existing = File.Exists(gitIgnorePath)
            ? await File.ReadAllTextAsync(gitIgnorePath, cancellationToken)
            : string.Empty;

        bool hasEntry = existing
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => string.Equals(line, entry, StringComparison.Ordinal)
                || string.Equals(line, entry.TrimEnd('/'), StringComparison.Ordinal));
        if (hasEntry)
        {
            return false;
        }

        string updated = existing;
        if (!string.IsNullOrEmpty(updated) && !updated.EndsWith('\n'))
        {
            updated += Environment.NewLine;
        }

        if (!string.IsNullOrEmpty(updated))
        {
            updated += Environment.NewLine;
        }

        updated += entry + Environment.NewLine;
        await atomicFileWriter.WriteTextAsync(gitIgnorePath, updated, cancellationToken);
        return true;
    }

    private string? FindSingleRootSolutionPath()
    {
        string[] solutions = Directory.EnumerateFiles(_repositoryRoot, "*.sln", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return solutions.Length == 1 ? solutions[0] : null;
    }

    private static RingmasterRepoConfig CreateInitialConfig(string baseBranch, string? solutionPath)
    {
        VerificationCommandDefinition[] commands = BuildInitialCommands(solutionPath);

        return new RingmasterRepoConfig
        {
            BaseBranch = string.IsNullOrWhiteSpace(baseBranch) ? "master" : baseBranch.Trim(),
            VerificationProfiles = new Dictionary<string, VerificationProfileDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = new VerificationProfileDefinition
                {
                    Commands = commands,
                },
            },
        };
    }

    private static VerificationCommandDefinition[] BuildInitialCommands(string? solutionPath)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return [];
        }

        string solutionFileName = Path.GetFileName(solutionPath);
        return
        [
            new VerificationCommandDefinition
            {
                Name = "build",
                FileName = "dotnet",
                Arguments = ["build", solutionFileName],
                TimeoutSeconds = 900,
            },
            new VerificationCommandDefinition
            {
                Name = "test",
                FileName = "dotnet",
                Arguments = ["test", solutionFileName, "--no-build"],
                TimeoutSeconds = 1200,
            },
        ];
    }
}

public sealed record class RepositoryInitializationOptions
{
    public string BaseBranch { get; init; } = "master";
    public string PullRequestProvider { get; init; } = "github";
}

public sealed record class RepositoryInitializationResult
{
    public required string RepositoryRoot { get; init; }
    public required string RuntimeRoot { get; init; }
    public required string JobsRoot { get; init; }
    public required string ConfigPath { get; init; }
    public bool ConfigCreated { get; init; }
    public bool NotificationsCreated { get; init; }
    public bool GitIgnoreUpdated { get; init; }
    public required string PullRequestProvider { get; init; }
    public bool VerificationCommandsScaffolded { get; init; }
    public string? SolutionPath { get; init; }
}
