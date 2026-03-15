using Ringmaster.Core.Configuration;

namespace Ringmaster.Core.Tests;

public sealed class VerificationCommandSafetyPolicyTests
{
    [Fact]
    public void TryValidate_AllowsDotnetBuildCommand()
    {
        VerificationCommandDefinition command = new()
        {
            Name = "build",
            FileName = "dotnet",
            Arguments = ["build", "Ringmaster.sln"],
        };

        bool valid = VerificationCommandSafetyPolicy.TryValidate(command, out string reason);

        Assert.True(valid);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void TryValidate_RejectsNonDotnetExecutable()
    {
        VerificationCommandDefinition command = new()
        {
            Name = "script",
            FileName = "sh",
            Arguments = ["./verify.sh"],
        };

        bool valid = VerificationCommandSafetyPolicy.TryValidate(command, out string reason);

        Assert.False(valid);
        Assert.Contains("only", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidate_RejectsDotnetExecVerb()
    {
        VerificationCommandDefinition command = new()
        {
            Name = "exec",
            FileName = "dotnet",
            Arguments = ["exec", "evil.dll"],
        };

        bool valid = VerificationCommandSafetyPolicy.TryValidate(command, out string reason);

        Assert.False(valid);
        Assert.Contains("must start", reason, StringComparison.OrdinalIgnoreCase);
    }
}
