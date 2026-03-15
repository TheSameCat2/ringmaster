using System.Text;
using Ringmaster.Core.Serialization;

namespace Ringmaster.Infrastructure.Persistence;

public sealed class AtomicFileWriter
{
    public async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await WriteTextAsync(path, RingmasterJsonSerializer.Serialize(value), cancellationToken);
    }

    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);

        if (directory is null)
        {
            throw new InvalidOperationException($"Cannot determine the directory for '{path}'.");
        }

        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
