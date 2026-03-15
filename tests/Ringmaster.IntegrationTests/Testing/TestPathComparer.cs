namespace Ringmaster.IntegrationTests.Testing;

internal static class TestPathComparer
{
    public static string Normalize(string path)
    {
        string normalized = path.Replace('\\', '/');

        if (OperatingSystem.IsMacOS() && normalized.StartsWith("/private/", StringComparison.Ordinal))
        {
            normalized = normalized["/private".Length..];
        }

        return normalized.TrimEnd('/');
    }

    public static bool ContainsPath(string text, string path)
    {
        string normalizedText = Normalize(text)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
        return normalizedText.Contains(Normalize(path), StringComparison.Ordinal);
    }
}
