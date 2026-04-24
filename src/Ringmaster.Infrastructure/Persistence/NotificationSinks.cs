using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;

namespace Ringmaster.Infrastructure.Persistence;

public sealed class CompositeNotificationSink(IEnumerable<INotificationSink> sinks) : INotificationSink
{
    public async Task NotifyAsync(NotificationRecord notification, CancellationToken cancellationToken)
    {
        foreach (INotificationSink sink in sinks)
        {
            await sink.NotifyAsync(notification, cancellationToken);
        }
    }
}

public sealed class JsonlNotificationSink(string repositoryRoot) : INotificationSink
{
    private readonly string _path = RingmasterPaths.NotificationsPath(Path.GetFullPath(repositoryRoot));
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task NotifyAsync(NotificationRecord notification, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (directory is null)
            {
                throw new InvalidOperationException($"Notification path '{_path}' does not have a parent directory.");
            }

            Directory.CreateDirectory(directory);

            await using FileStream stream = new(
                _path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.Asynchronous);
            await using StreamWriter writer = new(stream);
            await writer.WriteLineAsync(RingmasterJsonSerializer.SerializeCompact(notification));
            await writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class WebhookPlaceholderNotificationSink : INotificationSink
{
    public Task NotifyAsync(NotificationRecord notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
