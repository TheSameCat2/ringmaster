using Ringmaster.Core;
using Ringmaster.Git;
using Ringmaster.Infrastructure.Configuration;
using Ringmaster.Infrastructure.Processes;

namespace Ringmaster.App;

public sealed class DoctorService(
    string repositoryRoot,
    IExternalProcessRunner processRunner,
    RingmasterRepoConfigLoader repoConfigLoader,
    GitWorktreeManager worktreeManager)
{
    private readonly string _repositoryRoot = Path.GetFullPath(repositoryRoot);

    public async Task<DoctorReport> RunAsync(CancellationToken cancellationToken)
    {
        List<DoctorCheckResult> checks =
        [
            await CheckCommandAsync("git", ["--version"], "git availability", cancellationToken),
            await CheckCommandAsync("codex", ["--version"], "codex availability", cancellationToken),
            await CheckCommandAsync("codex", ["login", "status"], "codex auth", cancellationToken),
            await CheckCommandAsync("gh", ["--version"], "gh availability", cancellationToken),
            await CheckCommandAsync("gh", ["auth", "status"], "gh auth", cancellationToken),
            await CheckRepoConfigAsync(cancellationToken),
            CheckWritablePath(worktreeManager.GetWorktreeRoot(_repositoryRoot), "worktree root writable"),
            CheckWritablePath(Path.Combine(_repositoryRoot, ProductInfo.RuntimeDirectoryName, "runtime"), "runtime folder writable"),
            CheckWritablePath(Path.Combine(_repositoryRoot, ProductInfo.RuntimeDirectoryName, "jobs"), "jobs folder writable"),
        ];

        return new DoctorReport
        {
            RepositoryRoot = _repositoryRoot,
            Checks = checks,
        };
    }

    private async Task<DoctorCheckResult> CheckCommandAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            ExternalProcessResult result = await processRunner.RunAsync(
                new ExternalProcessSpec
                {
                    FileName = fileName,
                    WorkingDirectory = _repositoryRoot,
                    Arguments = arguments,
                    Timeout = TimeSpan.FromSeconds(30),
                },
                cancellationToken);

            bool succeeded = !result.TimedOut && result.ExitCode == 0;
            string detail = string.IsNullOrWhiteSpace(result.Stdout)
                ? result.Stderr.Trim()
                : result.Stdout.Trim();

            return new DoctorCheckResult
            {
                Name = name,
                Succeeded = succeeded,
                Summary = succeeded
                    ? "ok"
                    : $"exit code {result.ExitCode}",
                Detail = detail,
            };
        }
        catch (Exception exception)
        {
            return new DoctorCheckResult
            {
                Name = name,
                Succeeded = false,
                Summary = exception.GetType().Name,
                Detail = exception.Message,
            };
        }
    }

    private async Task<DoctorCheckResult> CheckRepoConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            await repoConfigLoader.LoadAsync(_repositoryRoot, cancellationToken);
            return new DoctorCheckResult
            {
                Name = "repo config validity",
                Succeeded = true,
                Summary = "ok",
                Detail = ProductInfo.RepoConfigFileName,
            };
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException)
        {
            return new DoctorCheckResult
            {
                Name = "repo config validity",
                Succeeded = false,
                Summary = exception.GetType().Name,
                Detail = exception.Message,
            };
        }
    }

    private static DoctorCheckResult CheckWritablePath(string path, string name)
    {
        try
        {
            Directory.CreateDirectory(path);
            string probePath = Path.Combine(path, $".doctor-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);

            return new DoctorCheckResult
            {
                Name = name,
                Succeeded = true,
                Summary = "ok",
                Detail = path,
            };
        }
        catch (Exception exception)
        {
            return new DoctorCheckResult
            {
                Name = name,
                Succeeded = false,
                Summary = exception.GetType().Name,
                Detail = exception.Message,
            };
        }
    }
}

public sealed record class DoctorReport
{
    public required string RepositoryRoot { get; init; }
    public IReadOnlyList<DoctorCheckResult> Checks { get; init; } = [];
    public bool Succeeded => Checks.All(check => check.Succeeded);
}

public sealed record class DoctorCheckResult
{
    public required string Name { get; init; }
    public bool Succeeded { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
