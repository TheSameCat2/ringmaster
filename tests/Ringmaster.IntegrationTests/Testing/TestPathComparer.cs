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
        return Normalize(text).Contains(Normalize(path), StringComparison.Ordinal);
    }
}
