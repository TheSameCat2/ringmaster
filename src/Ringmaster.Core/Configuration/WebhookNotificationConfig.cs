namespace Ringmaster.Core.Configuration;

public sealed record class WebhookNotificationConfig
{
    public required string Url { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxRetries { get; init; } = 2;
    public int RetryDelaySeconds { get; init; } = 5;
    public WebhookAuthType Authentication { get; init; } = WebhookAuthType.None;
    public string? SecretEnvironmentVariable { get; init; }
    public IReadOnlyList<string> AllowedEventTypes { get; init; } = [];
    public bool AllowLocalhost { get; init; }
    public bool AllowPrivateAddresses { get; init; }
}

public enum WebhookAuthType
{
    None,
    BearerToken,
    HmacSha256,
}
