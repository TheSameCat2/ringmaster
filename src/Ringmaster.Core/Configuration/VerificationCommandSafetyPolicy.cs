namespace Ringmaster.Core.Configuration;

public static class VerificationCommandSafetyPolicy
{
    public const string UnsafeOverrideEnvironmentVariableName = "RINGMASTER_ALLOW_UNSAFE_VERIFICATION_COMMANDS";

    private static readonly HashSet<string> AllowedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet",
    };

    private static readonly HashSet<string> AllowedDotnetVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "build",
        "test",
        "restore",
        "pack",
        "publish",
    };

    public static bool TryValidate(VerificationCommandDefinition command, out string reason)
    {
        if (IsUnsafeOverrideEnabled())
        {
            reason = string.Empty;
            return true;
        }

        if (!AllowedExecutables.Contains(command.FileName))
        {
            reason = $"Verification command '{command.Name}' uses '{command.FileName}', but only '{string.Join("', '", AllowedExecutables)}' is allowed.";
            return false;
        }

        if (command.Arguments.Count == 0 || !AllowedDotnetVerbs.Contains(command.Arguments[0]))
        {
            reason = $"Verification command '{command.Name}' must start with one of: {string.Join(", ", AllowedDotnetVerbs.OrderBy(verb => verb, StringComparer.OrdinalIgnoreCase))}.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsUnsafeOverrideEnabled()
    {
        string? value = Environment.GetEnvironmentVariable(UnsafeOverrideEnvironmentVariableName);
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
