using System.Net;
using Ringmaster.Core.Configuration;

namespace Ringmaster.Infrastructure.Persistence;

public sealed class WebhookUrlValidator(WebhookUrlSecurityPolicy policy)
{
    public void Validate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException($"Webhook URL is not a valid absolute URI: '{Redact(url)}'.");
        }

        if (!policy.AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Webhook URL scheme '{uri.Scheme}' is not allowed. Allowed schemes: {string.Join(", ", policy.AllowedSchemes)}.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException("Webhook URL host is required.");
        }

        if (IsLocalhostHost(uri.Host))
        {
            if (!policy.AllowLocalhost)
            {
                throw new InvalidOperationException("Webhook localhost URLs are not allowed by security policy.");
            }
        }

        if (IPAddress.TryParse(uri.Host, out IPAddress? ip))
        {
            if (IsBlockedIp(ip))
            {
                throw new InvalidOperationException($"Webhook IP address '{ip}' is not allowed by security policy.");
            }
        }
    }

    private static bool IsLocalhostHost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsBlockedIp(IPAddress ip)
    {
        if (ip.Equals(IPAddress.Any)
            || ip.Equals(IPAddress.IPv6Any)
            || ip.Equals(IPAddress.Broadcast))
        {
            return true;
        }

        byte[] bytes = ip.GetAddressBytes();

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            // Loopback: 127.0.0.0/8
            if (bytes[0] == 127)
            {
                return !policy.AllowLocalhost;
            }

            // Link-local: 169.254.0.0/16 (includes AWS metadata 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            // 0.0.0.0/8
            if (bytes[0] == 0)
            {
                return true;
            }

            // Multicast: 224.0.0.0/4
            if (bytes[0] >= 224 && bytes[0] <= 239)
            {
                return true;
            }

            // Reserved / broadcast: 240.0.0.0/4 and 255.255.255.255
            if (bytes[0] >= 240)
            {
                return true;
            }

            if (!policy.AllowPrivateAddresses)
            {
                // 10.0.0.0/8
                if (bytes[0] == 10)
                {
                    return true;
                }

                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                {
                    return true;
                }

                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168)
                {
                    return true;
                }

                // 100.64.0.0/10 (CGNAT / shared address space)
                if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                {
                    return true;
                }
            }
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            // Loopback ::1
            if (bytes.Length == 16)
            {
                bool isLoopback = bytes[15] == 1;
                for (int i = 0; i < 15 && isLoopback; i++)
                {
                    if (bytes[i] != 0)
                    {
                        isLoopback = false;
                    }
                }

                if (isLoopback)
                {
                    return !policy.AllowLocalhost;
                }

                // Link-local fe80::/10
                if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
                {
                    return true;
                }

                // Multicast ff00::/8
                if (bytes[0] == 0xFF)
                {
                    return true;
                }

                if (!policy.AllowPrivateAddresses)
                {
                    // Unique local fc00::/7
                    if ((bytes[0] & 0xFE) == 0xFC)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static string Redact(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && !string.IsNullOrEmpty(uri.UserInfo))
        {
            return url.Replace(uri.UserInfo, "***", StringComparison.Ordinal);
        }

        return url;
    }
}
