using Ringmaster.Core;

namespace Ringmaster.Core.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void ProductConstantsMatchThePlannedPhaseZeroNaming()
    {
        Assert.Equal("Ringmaster", ProductInfo.Name);
        Assert.Equal("ringmaster.json", ProductInfo.RepoConfigFileName);
        Assert.Equal(".ringmaster", ProductInfo.RuntimeDirectoryName);
    }
}
