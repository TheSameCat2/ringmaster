using System.Diagnostics;
using Ringmaster.Core.Configuration;
using Ringmaster.Core.Serialization;

namespace Ringmaster.IntegrationTests.Testing;

internal sealed class TemporaryGitRepository : IDisposable
{
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
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
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
        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Command '{fileName} {string.Join(' ', arguments)}' failed with exit code {process.ExitCode}: {stderr}");
        }

        return stdout;
    }
}
