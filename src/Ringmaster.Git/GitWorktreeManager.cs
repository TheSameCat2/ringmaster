using System.Text;
using Ringmaster.Core.Jobs;

namespace Ringmaster.Git;

public sealed class GitWorktreeManager(GitCli gitCli)
{
    public async Task<PreparedWorktree> PrepareAsync(
        string repositoryRoot,
        string jobId,
        string title,
        string baseBranch,
        CancellationToken cancellationToken)
    {
        string branchName = CreateBranchName(jobId, title);
        string worktreePath = CreateWorktreePath(repositoryRoot, jobId);
        string baseCommit = await gitCli.ResolveCommitAsync(repositoryRoot, baseBranch, cancellationToken);
        IReadOnlyList<GitWorktreeInfo> worktrees = await TryListWorktreesAsync(repositoryRoot, worktreePath, cancellationToken);
        string expectedBranch = $"refs/heads/{branchName}";
        GitWorktreeInfo? existing = worktrees.FirstOrDefault(worktree =>
            string.Equals(Path.GetFullPath(worktree.Path), Path.GetFullPath(worktreePath), StringComparison.Ordinal))
            ?? worktrees.FirstOrDefault(worktree => string.Equals(worktree.Branch, expectedBranch, StringComparison.Ordinal));

        if (existing is null)
        {
            bool branchExists = await gitCli.BranchExistsAsync(repositoryRoot, branchName, cancellationToken);
            string? parentDirectory = Path.GetDirectoryName(worktreePath);
            if (parentDirectory is null)
            {
                throw new InvalidOperationException($"Unable to determine the parent directory for '{worktreePath}'.");
            }

            Directory.CreateDirectory(parentDirectory);

            await gitCli.AddWorktreeAsync(
                repositoryRoot,
                worktreePath,
                branchName,
                baseBranch,
                createBranch: !branchExists,
                lockReason: $"ringmaster job {jobId}",
                cancellationToken);
        }
        else
        {
            worktreePath = existing.Path;

            if (!Directory.Exists(worktreePath))
            {
                await gitCli.RepairWorktreeAsync(repositoryRoot, worktreePath, cancellationToken);
            }
        }

        string headCommit = await gitCli.GetHeadCommitAsync(worktreePath, cancellationToken);

        return new PreparedWorktree
        {
            RepoRoot = repositoryRoot,
            BaseBranch = baseBranch,
            BaseCommit = baseCommit,
            JobBranch = branchName,
            WorktreePath = worktreePath,
            HeadCommit = headCommit,
        };
    }

    public async Task<JobGitSnapshot> CaptureSnapshotAsync(PreparedWorktree preparedWorktree, CancellationToken cancellationToken)
    {
        GitStatusInfo status = await gitCli.CaptureStatusAsync(preparedWorktree.WorktreePath, cancellationToken);

        return new JobGitSnapshot
        {
            RepoRoot = preparedWorktree.RepoRoot,
            BaseBranch = preparedWorktree.BaseBranch,
            BaseCommit = preparedWorktree.BaseCommit,
            JobBranch = preparedWorktree.JobBranch,
            WorktreePath = preparedWorktree.WorktreePath,
            HeadCommit = status.HeadCommit,
            HasUncommittedChanges = status.HasUncommittedChanges,
            ChangedFiles = status.ChangedFiles,
        };
    }

    public string CreateBranchName(string jobId, string title)
    {
        string shortId = ExtractShortId(jobId);
        string slug = Slugify(title);
        return string.IsNullOrWhiteSpace(slug)
            ? $"ringmaster/j-{shortId}"
            : $"ringmaster/j-{shortId}-{slug}";
    }

    public string CreateWorktreePath(string repositoryRoot, string jobId)
    {
        string repoRoot = Path.GetFullPath(repositoryRoot);
        string repoName = new DirectoryInfo(repoRoot).Name;
        string repoParent = Directory.GetParent(repoRoot)?.FullName
            ?? throw new InvalidOperationException($"Repository root '{repoRoot}' does not have a parent directory.");

        return Path.Combine(repoParent, ".ringmaster-worktrees", repoName, $"j-{ExtractShortId(jobId)}");
    }

    private async Task<IReadOnlyList<GitWorktreeInfo>> TryListWorktreesAsync(
        string repositoryRoot,
        string expectedWorktreePath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await gitCli.ListWorktreesAsync(repositoryRoot, cancellationToken);
        }
        catch (GitCliException) when (Directory.Exists(expectedWorktreePath))
        {
            await gitCli.RepairWorktreeAsync(repositoryRoot, expectedWorktreePath, cancellationToken);
            return await gitCli.ListWorktreesAsync(repositoryRoot, cancellationToken);
        }
    }

    private static string ExtractShortId(string jobId)
    {
        string lastSegment = jobId.Split('-', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
            ?? jobId;
        return lastSegment.Length <= 8 ? lastSegment.ToLowerInvariant() : lastSegment[..8].ToLowerInvariant();
    }

    private static string Slugify(string title)
    {
        StringBuilder builder = new();
        bool previousWasDash = false;

        foreach (char character in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasDash = false;
            }
            else if (!previousWasDash)
            {
                builder.Append('-');
                previousWasDash = true;
            }
        }

        string slug = builder.ToString().Trim('-');
        if (slug.Length <= 24)
        {
            return slug;
        }

        return slug[..24].Trim('-');
    }
}
