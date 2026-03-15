using System.Text;
using Ringmaster.Core;
using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;

namespace Ringmaster.App;

public sealed class RunLogService(IJobRepository jobRepository)
{
    public async Task<RunLogSelection> SelectAsync(
        string jobId,
        string? runId,
        CancellationToken cancellationToken)
    {
        StoredJob job = await jobRepository.GetAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException($"Job '{jobId}' was not found.");

        string runsRoot = Path.Combine(job.JobDirectoryPath, "runs");
        if (!Directory.Exists(runsRoot))
        {
            throw new InvalidOperationException($"Job '{jobId}' does not have any recorded runs.");
        }

        string selectedRunId = string.IsNullOrWhiteSpace(runId)
            ? Directory.EnumerateDirectories(runsRoot)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderByDescending(name => name, StringComparer.Ordinal)
                .FirstOrDefault()
                ?? throw new InvalidOperationException($"Job '{jobId}' does not have any recorded runs.")
            : runId.Trim();

        string runDirectoryPath = Path.Combine(runsRoot, selectedRunId);
        if (!Directory.Exists(runDirectoryPath))
        {
            throw new InvalidOperationException($"Run '{selectedRunId}' was not found for job '{jobId}'.");
        }

        JobRunRecord? run = await LoadRunRecordAsync(runDirectoryPath, cancellationToken);
        string relativeLogPath = SelectLogFile(runDirectoryPath, run);
        string logPath = Path.IsPathRooted(relativeLogPath)
            ? relativeLogPath
            : Path.Combine(runDirectoryPath, relativeLogPath);

        return new RunLogSelection
        {
            JobId = jobId,
            RunId = selectedRunId,
            LogPath = logPath,
            RelativeLogPath = Path.GetRelativePath(runDirectoryPath, logPath),
        };
    }

    public Task<string> ReadAsync(RunLogSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return File.ReadAllTextAsync(selection.LogPath, cancellationToken);
    }

    public async Task FollowAsync(
        RunLogSelection selection,
        Func<string, CancellationToken, Task> onChunk,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(onChunk);

        long position = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(selection.LogPath))
            {
                await using FileStream stream = new(
                    selection.LogPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                if (stream.Length < position)
                {
                    position = 0;
                }

                stream.Seek(position, SeekOrigin.Begin);
                using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                string chunk = await reader.ReadToEndAsync(cancellationToken);
                if (chunk.Length > 0)
                {
                    await onChunk(chunk, cancellationToken);
                    position = stream.Position;
                }
            }

            await Task.Delay(pollInterval, cancellationToken);
        }
    }

    private static async Task<JobRunRecord?> LoadRunRecordAsync(string runDirectoryPath, CancellationToken cancellationToken)
    {
        string runPath = Path.Combine(runDirectoryPath, "run.json");
        if (!File.Exists(runPath))
        {
            return null;
        }

        string runJson = await File.ReadAllTextAsync(runPath, cancellationToken);
        JobRunRecord run = RingmasterJsonSerializer.Deserialize<JobRunRecord>(runJson);
        SchemaVersionSupport.NormalizeForRead("Job run record", run.SchemaVersion);
        return run;
    }

    private static string SelectLogFile(string runDirectoryPath, JobRunRecord? run)
    {
        foreach (string candidate in EnumerateCandidates(run))
        {
            string candidatePath = Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(runDirectoryPath, candidate);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        string? fallback = Directory.EnumerateFiles(runDirectoryPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(Path.GetFileName(path), "run.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => FallbackPriority(path))
            .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (fallback is not null)
        {
            return fallback;
        }

        throw new InvalidOperationException(
            $"Run '{Path.GetFileName(runDirectoryPath)}' does not contain a readable log artifact.");
    }

    private static IEnumerable<string> EnumerateCandidates(JobRunRecord? run)
    {
        if (run is null)
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(run.Artifacts.EventLog))
        {
            yield return run.Artifacts.EventLog;
        }

        if (!string.IsNullOrWhiteSpace(run.Artifacts.Stderr))
        {
            yield return run.Artifacts.Stderr;
        }

        if (!string.IsNullOrWhiteSpace(run.Artifacts.FinalOutput))
        {
            yield return run.Artifacts.FinalOutput;
        }

        if (!string.IsNullOrWhiteSpace(run.Artifacts.Prompt))
        {
            yield return run.Artifacts.Prompt;
        }

        if (!string.IsNullOrWhiteSpace(run.Artifacts.Schema))
        {
            yield return run.Artifacts.Schema;
        }
    }

    private static int FallbackPriority(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.ToLowerInvariant() switch
        {
            ".log" => 0,
            ".jsonl" => 1,
            ".txt" => 2,
            ".json" => 3,
            _ => 4,
        };
    }
}

public sealed record class RunLogSelection
{
    public required string JobId { get; init; }
    public required string RunId { get; init; }
    public required string LogPath { get; init; }
    public required string RelativeLogPath { get; init; }
}
