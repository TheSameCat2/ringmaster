namespace Ringmaster.Core.Configuration;

public sealed record class WebhookUrlSecurityPolicy
{
    public bool AllowLocalhost { get; init; }
    public bool AllowPrivateAddresses { get; init; }
    public IReadOnlyList<string> AllowedSchemes { get; init; } = ["http", "https"];
}
