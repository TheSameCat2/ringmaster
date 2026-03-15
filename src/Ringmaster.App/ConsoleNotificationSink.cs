using Ringmaster.Core.Jobs;
using Spectre.Console;

namespace Ringmaster.App;

public sealed class ConsoleNotificationSink(IAnsiConsole console) : INotificationSink
{
    public Task NotifyAsync(NotificationRecord notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(notification.JobId))
        {
            console.MarkupLine($"[grey]{notification.EventType}[/] {Markup.Escape(notification.Summary)}");
        }
        else
        {
            console.MarkupLine($"[grey]{notification.EventType}[/] {Markup.Escape(notification.JobId)} {Markup.Escape(notification.Summary)}");
        }

        return Task.CompletedTask;
    }
}
