using Ringmaster.Core;
using Ringmaster.Core.Configuration;
using Ringmaster.Core.Serialization;

namespace Ringmaster.Infrastructure.Configuration;

public sealed class RingmasterRepoConfigLoader
{
    public async Task<RingmasterRepoConfig> LoadAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        string configPath = Path.Combine(repositoryRoot, ProductInfo.RepoConfigFileName);
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Repository config '{ProductInfo.RepoConfigFileName}' was not found at '{repositoryRoot}'.", configPath);
        }

        string json = await File.ReadAllTextAsync(configPath, cancellationToken);
        RingmasterRepoConfig config = RingmasterJsonSerializer.Deserialize<RingmasterRepoConfig>(json);
        return config with
        {
            SchemaVersion = SchemaVersionSupport.NormalizeForRead("Repository config", config.SchemaVersion),
        };
    }
}
