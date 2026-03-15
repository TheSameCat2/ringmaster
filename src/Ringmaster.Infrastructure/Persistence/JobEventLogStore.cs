using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;

namespace Ringmaster.Infrastructure.Persistence;

public sealed class JobEventLogStore
{
    public async Task AppendAsync(string path, JobEventRecord jobEvent, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);

        if (directory is null)
        {
            throw new InvalidOperationException($"Cannot determine the directory for '{path}'.");
        }

        Directory.CreateDirectory(directory);

        await using FileStream stream = new(
            fullPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous);
        await using StreamWriter writer = new(stream);
        await writer.WriteLineAsync(RingmasterJsonSerializer.SerializeCompact(jobEvent));
        await writer.FlushAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<JobEventRecord>> ReadAllAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        List<JobEventRecord> events = [];

        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using StreamReader reader = new(stream);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            events.Add(RingmasterJsonSerializer.Deserialize<JobEventRecord>(line));
        }

        return events;
    }
}
