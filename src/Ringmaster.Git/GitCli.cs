using Ringmaster.Infrastructure.Processes;

namespace Ringmaster.Git;

public sealed class GitCli(ExternalProcessRunner processRunner)
{
    public async Task<string> ResolveCommitAsync(string repositoryRoot, string reference, CancellationToken cancellationToken)
    {
        string stdout = await CaptureAsync(
            repositoryRoot,
            ["rev-parse", "--verify", reference],
            TimeSpan.FromMinutes(1),
            cancellationToken);

        return stdout.Trim();
    }

    public async Task<bool> BranchExistsAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken)
    {
        ExternalProcessResult result = await processRunner.RunAsync(
            new ExternalProcessSpec
            {
                FileName = "git",
                WorkingDirectory = repositoryRoot,
                Arguments = ["show-ref", "--verify", "--quiet", $"refs/heads/{branchName}"],
                Timeout = TimeSpan.FromMinutes(1),
            },
            cancellationToken);

        return result.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => throw new GitCliException("git show-ref failed.", result),
        };
    }

    public async Task<IReadOnlyList<GitWorktreeInfo>> ListWorktreesAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        string stdout = await CaptureAsync(
            repositoryRoot,
            ["worktree", "list", "--porcelain", "-z"],
            TimeSpan.FromMinutes(1),
            cancellationToken);

        List<GitWorktreeInfo> worktrees = [];
        string[] tokens = stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        GitWorktreeInfoBuilder? current = null;

        foreach (string token in tokens)
        {
            if (token.StartsWith("worktree ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    worktrees.Add(current.Build());
                }

                current = new GitWorktreeInfoBuilder
                {
                    Path = token["worktree ".Length..],
                };

                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (token.StartsWith("branch ", StringComparison.Ordinal))
            {
                current.Branch = token["branch ".Length..];
            }
            else if (token.StartsWith("HEAD ", StringComparison.Ordinal))
            {
                current.Head = token["HEAD ".Length..];
            }
            else if (token.StartsWith("locked", StringComparison.Ordinal))
            {
                current.Locked = true;
                current.LockReason = token.Length > "locked ".Length ? token["locked ".Length..] : null;
            }
        }

        if (current is not null)
        {
            worktrees.Add(current.Build());
        }

        return worktrees;
    }

    public async Task AddWorktreeAsync(
        string repositoryRoot,
        string worktreePath,
        string branchName,
        string baseReference,
        bool createBranch,
        string lockReason,
        CancellationToken cancellationToken)
    {
        List<string> arguments = ["worktree", "add", "--lock", "--reason", lockReason];

        if (createBranch)
        {
            arguments.Add("-b");
            arguments.Add(branchName);
        }

        arguments.Add(worktreePath);
        arguments.Add(createBranch ? baseReference : branchName);

        await EnsureSuccessAsync(repositoryRoot, arguments, TimeSpan.FromMinutes(2), cancellationToken);
    }

    public Task RepairWorktreeAsync(string repositoryRoot, string worktreePath, CancellationToken cancellationToken)
    {
        return EnsureSuccessAsync(
            repositoryRoot,
            ["worktree", "repair", worktreePath],
            TimeSpan.FromMinutes(1),
            cancellationToken);
    }

    public Task<string> GetHeadCommitAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        return ResolveCommitAsync(workingDirectory, "HEAD", cancellationToken);
    }

    public async Task<GitStatusInfo> CaptureStatusAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        string stdout = await CaptureAsync(
            workingDirectory,
            ["--no-optional-locks", "status", "--porcelain=v2", "-z", "--branch"],
            TimeSpan.FromMinutes(1),
            cancellationToken);
        string headCommit = await GetHeadCommitAsync(workingDirectory, cancellationToken);

        List<string> changedFiles = [];
        string[] tokens = stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        for (int index = 0; index < tokens.Length; index++)
        {
            string token = tokens[index];

            if (token.StartsWith("# ", StringComparison.Ordinal) || token.StartsWith("! ", StringComparison.Ordinal))
            {
                continue;
            }

            if (token.StartsWith("? ", StringComparison.Ordinal))
            {
                changedFiles.Add(token[2..]);
                continue;
            }

            if (token.StartsWith("1 ", StringComparison.Ordinal) || token.StartsWith("u ", StringComparison.Ordinal))
            {
                changedFiles.Add(token[(token.LastIndexOf(' ') + 1)..]);
                continue;
            }

            if (token.StartsWith("2 ", StringComparison.Ordinal))
            {
                changedFiles.Add(token[(token.LastIndexOf(' ') + 1)..]);
                index++;
            }
        }

        string[] normalized = changedFiles
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return new GitStatusInfo
        {
            HeadCommit = headCommit,
            HasUncommittedChanges = normalized.Length > 0,
            ChangedFiles = normalized,
        };
    }

    public Task<string> CaptureDiffPatchAsync(string workingDirectory, string baseCommit, CancellationToken cancellationToken)
    {
        return CaptureAsync(
            workingDirectory,
            ["diff", "--binary", baseCommit],
            TimeSpan.FromMinutes(1),
            cancellationToken);
    }

    public Task<string> CaptureDiffStatAsync(string workingDirectory, string baseCommit, CancellationToken cancellationToken)
    {
        return CaptureAsync(
            workingDirectory,
            ["diff", "--stat", baseCommit],
            TimeSpan.FromMinutes(1),
            cancellationToken);
    }

    private Task EnsureSuccessAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return CaptureAsync(workingDirectory, arguments, timeout, cancellationToken);
    }

    private async Task<string> CaptureAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ExternalProcessResult result = await processRunner.RunAsync(
            new ExternalProcessSpec
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                Arguments = arguments,
                Timeout = timeout,
            },
            cancellationToken);

        if (result.TimedOut || result.ExitCode != 0)
        {
            throw new GitCliException($"git {string.Join(' ', arguments)} failed.", result);
        }

        return result.Stdout;
    }

    private sealed class GitWorktreeInfoBuilder
    {
        public string? Path { get; set; }
        public string? Branch { get; set; }
        public string? Head { get; set; }
        public bool Locked { get; set; }
        public string? LockReason { get; set; }

        public GitWorktreeInfo Build()
        {
            return new GitWorktreeInfo
            {
                Path = Path ?? throw new InvalidOperationException("Worktree path was not present in git output."),
                Branch = Branch,
                Head = Head,
                Locked = Locked,
                LockReason = LockReason,
            };
        }
    }
}

public sealed class GitCliException(string message, ExternalProcessResult processResult) : Exception(message)
{
    public ExternalProcessResult ProcessResult { get; } = processResult;
}
