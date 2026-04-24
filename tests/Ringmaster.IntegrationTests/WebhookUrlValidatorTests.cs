using Ringmaster.Core.Configuration;
using Ringmaster.Infrastructure.Persistence;

namespace Ringmaster.IntegrationTests;

public sealed class WebhookUrlValidatorTests
{
    private static WebhookUrlValidator DefaultValidator() => new(new WebhookUrlSecurityPolicy());
    private static WebhookUrlValidator PermissiveValidator() => new(new WebhookUrlSecurityPolicy
    {
        AllowLocalhost = true,
        AllowPrivateAddresses = true,
    });

    [Theory]
    [InlineData("https://example.com/webhooks/incoming/abc123")]
    [InlineData("https://discord.com/api/webhooks/123456789/abcdef")]
    [InlineData("http://example.com/webhook")]
    [InlineData("https://ci.example.com/hooks/ringmaster")]
    [InlineData("https://1.1.1.1/webhook")]
    public void PublicUrls_AreAllowed(string url)
    {
        DefaultValidator().Validate(url);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    [InlineData("")]
    public void InvalidUrls_AreRejected(string url)
    {
        Assert.Throws<InvalidOperationException>(() => DefaultValidator().Validate(url));
    }

    [Theory]
    [InlineData("ftp://example.com/webhook")]
    [InlineData("file:///etc/passwd")]
    [InlineData("mailto:test@example.com")]
    [InlineData("gopher://example.com")]
    public void NonHttpSchemes_AreRejected(string url)
    {
        Assert.Throws<InvalidOperationException>(() => DefaultValidator().Validate(url));
    }

    [Theory]
    [InlineData("http://localhost/webhook")]
    [InlineData("https://localhost:8080/webhook")]
    [InlineData("http://LOCALHOST/webhook")]
    public void Localhost_IsRejectedByDefault(string url)
    {
        Assert.Throws<InvalidOperationException>(() => DefaultValidator().Validate(url));
    }

    [Fact]
    public void Localhost_IsAllowedWhenPolicyPermits()
    {
        PermissiveValidator().Validate("http://localhost/webhook");
    }

    [Theory]
    [InlineData("http://127.0.0.1/webhook")]
    [InlineData("http://127.255.255.255/webhook")]
    public void LoopbackIpv4_IsRejectedByDefault(string url)
    {
        Assert.Throws<InvalidOperationException>(() => DefaultValidator().Validate(url));
    }

    [Fact]
    public void LoopbackIpv4_IsAllowedWhenPolicyPermits()
    {
        PermissiveValidator().Validate("http://127.0.0.1/webhook");
    }

    [Theory]
    [InlineData("http://[::1]/webhook")]
    public void LoopbackIpv6_IsRejectedByDefault(string url)
    {
        Assert.Throws<InvalidOperationException>(() => DefaultValidator().Validate(url));
    }

    [Fact]
    public void LoopbackIpv6_IsAllowedWhenPolicyPermits()
    {
        PermissiveValidator().Validate("http://[::1]/webhook");
    }

    [Theory]
    [InlineData("http://10.0.0.1/webhook")]
    [InlineData("http://172.16.0.1/webhook")]
    [InlineData("http://172.31.255.255/webhook")]
    [InlineData("http://192.168.1.1/webhook")]
    [InlineData("http://100.64.0.1/webhook")]
    [InlineData("http://100.127.255.255/webhook")]
    public void PrivateIpv4_IsRejectedByDefault(string url)
    {
        Assert.Throws<InvalidOperationException>(() => DefaultValidator().Validate(url));
    }

    [Theory]
    [InlineData("http://10.0.0.1/webhook")]
    [InlineData("http://192.168.1.1/webhook")]
    public void PrivateIpv4_IsAllowedWhenPolicyPermits(string url)
    {
        PermissiveValidator().Validate(url);
    }

    [Theory]
    [InlineData("http://169.254.0.1/webhook")]
    [InlineData("http://169.254.169.254/webhook")]
    [InlineData("http://169.254.255.255/webhook")]
    public void LinkLocal_IsAlwaysRejected(string url)
    {
        Assert.Throws<InvalidOperationException>(() => DefaultValidator().Validate(url));
        Assert.Throws<InvalidOperationException>(() => PermissiveValidator().Validate(url));
    }

    [Theory]
    [InlineData("http://224.0.0.1/webhook")]
    [InlineData("http://239.255.255.255/webhook")]
    [InlineData("http://240.0.0.1/webhook")]
    [InlineData("http://255.255.255.255/webhook")]
    [InlineData("http://0.0.0.0/webhook")]
    [InlineData("http://0.1.2.3/webhook")]
    public void MulticastBroadcastZero_IsAlwaysRejected(string url)
    {
        Assert.Throws<InvalidOperationException>(() => DefaultValidator().Validate(url));
        Assert.Throws<InvalidOperationException>(() => PermissiveValidator().Validate(url));
    }

    [Theory]
    [InlineData("http://[fe80::1]/webhook")]
    [InlineData("http://[ff02::1]/webhook")]
    public void LinkLocalMulticastIpv6_IsAlwaysRejected(string url)
    {
        Assert.Throws<InvalidOperationException>(() => DefaultValidator().Validate(url));
        Assert.Throws<InvalidOperationException>(() => PermissiveValidator().Validate(url));
    }

    [Theory]
    [InlineData("http://[fc00::1]/webhook")]
    [InlineData("http://[fd00::1]/webhook")]
    public void UniqueLocalIpv6_IsRejectedByDefault(string url)
    {
        Assert.Throws<InvalidOperationException>(() => DefaultValidator().Validate(url));
    }

    [Theory]
    [InlineData("http://[fc00::1]/webhook")]
    public void UniqueLocalIpv6_IsAllowedWhenPolicyPermits(string url)
    {
        PermissiveValidator().Validate(url);
    }

    [Fact]
    public void UserInfo_IsRedactedInErrorMessage()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => DefaultValidator().Validate("ftp://user:secret@example.com/webhook"));
        Assert.DoesNotContain("secret", exception.Message);
    }
}
