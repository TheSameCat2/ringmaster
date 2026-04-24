namespace Ringmaster.Core.Configuration;

public sealed record class RingmasterRepoConfig
{
    public int SchemaVersion { get; init; } = ProductInfo.SchemaVersion;
    public string BaseBranch { get; init; } = "master";
    public WebhookNotificationConfig? Webhook { get; init; }
    public IReadOnlyDictionary<string, VerificationProfileDefinition> VerificationProfiles { get; init; }
        = new Dictionary<string, VerificationProfileDefinition>(StringComparer.OrdinalIgnoreCase);
}

public sealed record class VerificationProfileDefinition
{
    public IReadOnlyList<VerificationCommandDefinition> Commands { get; init; } = [];
}

public sealed record class VerificationCommandDefinition
{
    public required string Name { get; init; }
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public int TimeoutSeconds { get; init; } = 900;
}
