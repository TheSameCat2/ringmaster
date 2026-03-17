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

    private static readonly HashSet<string> AllowedDotnetStandaloneArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "--version",
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

        if (command.Arguments.Count == 1 && AllowedDotnetStandaloneArguments.Contains(command.Arguments[0]))
        {
            reason = string.Empty;
            return true;
        }

        if (command.Arguments.Count == 0 || !AllowedDotnetVerbs.Contains(command.Arguments[0]))
        {
            reason = $"Verification command '{command.Name}' must start with one of: {string.Join(", ", AllowedDotnetVerbs.OrderBy(verb => verb, StringComparer.OrdinalIgnoreCase))}, or use one of: {string.Join(", ", AllowedDotnetStandaloneArguments.OrderBy(argument => argument, StringComparer.OrdinalIgnoreCase))}.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsUnsafeOverrideEnabled()
    {
        string? value = Environment.GetEnvironmentVariable(UnsafeOverrideEnvironmentVariableName);
        return value == "1" || (bool.TryParse(value, out bool enabled) && enabled);
    }
}
