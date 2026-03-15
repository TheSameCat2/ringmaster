using System.Diagnostics;
using Ringmaster.Core.Configuration;
using Ringmaster.Core.Serialization;

namespace Ringmaster.IntegrationTests.Testing;

internal sealed class TemporaryGitRepository : IDisposable
{
    private bool _disposed;

    public TemporaryGitRepository()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ringmaster-git-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }
    public string TrackedFilePath => System.IO.Path.Combine(Path, "README.md");

    public async Task InitializeAsync(bool includeRepoConfig = true, string? repoConfigJson = null, CancellationToken cancellationToken = default)
    {
        await RunGitAsync(["init", "--initial-branch=master"], cancellationToken);
        await RunGitAsync(["config", "user.name", "Ringmaster Tests"], cancellationToken);
        await RunGitAsync(["config", "user.email", "ringmaster@example.com"], cancellationToken);

        await File.WriteAllTextAsync(TrackedFilePath, "# Sample Repo" + Environment.NewLine, cancellationToken);

        if (includeRepoConfig)
        {
            string json = repoConfigJson ?? CreateDefaultRepoConfigJson();
            await File.WriteAllTextAsync(System.IO.Path.Combine(Path, "ringmaster.json"), json, cancellationToken);
        }

        await RunGitAsync(["add", "."], cancellationToken);
        await RunGitAsync(["commit", "-m", "Initial commit"], cancellationToken);
    }

    public async Task AppendToTrackedFileAsync(string text, CancellationToken cancellationToken = default)
    {
        await File.AppendAllTextAsync(TrackedFilePath, text, cancellationToken);
    }

    public async Task<string> CaptureGitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        return await RunProcessAsync("git", arguments, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        CleanupLinkedWorktrees();
        DeleteDirectoryRobustly(GetWorktreeRoot());
        DeleteDirectoryRobustly(Path);
    }

    public static string CreateDefaultRepoConfigJson(params VerificationCommandDefinition[] commands)
    {
        VerificationCommandDefinition[] effectiveCommands = commands.Length == 0
            ?
            [
                new VerificationCommandDefinition
                {
                    Name = "verify",
                    FileName = "dotnet",
                    Arguments = ["--version"],
                    TimeoutSeconds = 60,
                },
            ]
            : commands;

        RingmasterRepoConfig config = new()
        {
            BaseBranch = "master",
            VerificationProfiles = new Dictionary<string, VerificationProfileDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = new()
                {
                    Commands = effectiveCommands,
                },
            },
        };

        return RingmasterJsonSerializer.Serialize(config);
    }

    private Task RunGitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        return RunProcessAsync("git", arguments, cancellationToken);
    }

    private async Task<string> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            WorkingDirectory = Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new()
        {
            StartInfo = startInfo,
        };

        process.Start();
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Command '{fileName} {string.Join(' ', arguments)}' failed with exit code {process.ExitCode}: {stderr}");
        }

        return stdout;
    }

    private void CleanupLinkedWorktrees()
    {
        if (!Directory.Exists(System.IO.Path.Combine(Path, ".git")))
        {
            return;
        }

        try
        {
            foreach (string worktreePath in ListWorktreePaths().Where(worktreePath =>
                         !string.Equals(
                             System.IO.Path.GetFullPath(worktreePath),
                             System.IO.Path.GetFullPath(Path),
                             StringComparison.Ordinal)))
            {
                RunProcess("git", ["worktree", "remove", "--force", "--force", worktreePath], throwOnFailure: false);
            }

            RunProcess("git", ["worktree", "prune"], throwOnFailure: false);
        }
        catch
        {
            // Best-effort cleanup for test repositories.
        }
    }

    private string GetWorktreeRoot()
    {
        // Keep this in sync with GitWorktreeManager.GetWorktreeRoot.
        string repoRoot = System.IO.Path.GetFullPath(Path);
        string repoName = new DirectoryInfo(repoRoot).Name;
        string repoParent = Directory.GetParent(repoRoot)?.FullName
            ?? throw new InvalidOperationException($"Repository root '{repoRoot}' does not have a parent directory.");
        return System.IO.Path.Combine(repoParent, ".ringmaster-worktrees", repoName);
    }

    private IReadOnlyList<string> ListWorktreePaths()
    {
        string stdout = RunProcess("git", ["worktree", "list", "--porcelain", "-z"]);
        string[] tokens = stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        List<string> paths = [];

        foreach (string token in tokens)
        {
            if (token.StartsWith("worktree ", StringComparison.Ordinal))
            {
                paths.Add(token["worktree ".Length..]);
            }
        }

        return paths;
    }

    private string RunProcess(string fileName, IReadOnlyList<string> arguments, bool throwOnFailure = true)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            WorkingDirectory = Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new()
        {
            StartInfo = startInfo,
        };

        process.Start();
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);
        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();

        if (throwOnFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Command '{fileName} {string.Join(' ', arguments)}' failed with exit code {process.ExitCode}: {stderr}");
        }

        return stdout;
    }

    private static void DeleteDirectoryRobustly(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Exception? lastError = null;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastError = exception;
                Thread.Sleep(TimeSpan.FromMilliseconds(50 * (attempt + 1)));
            }
        }

        throw lastError ?? new IOException($"Failed to delete '{path}'.");
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (string filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(filePath, File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);
        }

        foreach (string directoryPath in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                     .OrderByDescending(directoryPath => directoryPath.Length))
        {
            FileAttributes attributes = File.GetAttributes(directoryPath);
            File.SetAttributes(directoryPath, attributes & ~FileAttributes.ReadOnly);
        }

        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
    }
}
