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

public sealed class JsonlNotificationSink(
    string repositoryRoot,
    AtomicFileWriter atomicFileWriter) : INotificationSink
{
    private readonly string _path = RingmasterPaths.NotificationsPath(Path.GetFullPath(repositoryRoot));
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task NotifyAsync(NotificationRecord notification, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? throw new InvalidOperationException($"Notification path '{_path}' does not have a parent directory."));
            string existing = File.Exists(_path)
                ? await File.ReadAllTextAsync(_path, cancellationToken)
                : string.Empty;
            string line = RingmasterJsonSerializer.SerializeCompact(notification) + Environment.NewLine;
            await atomicFileWriter.WriteTextAsync(_path, existing + line, cancellationToken);
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
