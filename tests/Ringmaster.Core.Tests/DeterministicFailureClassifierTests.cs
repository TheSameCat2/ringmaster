using Ringmaster.Core.Jobs;

namespace Ringmaster.Core.Tests;

public sealed class DeterministicFailureClassifierTests
{
    [Fact]
    public void ClassifyDetectsCompilerFailures()
    {
        DeterministicFailureClassifier classifier = new();

        FailureClassification classification = classifier.Classify(
            new FailureClassificationContext
            {
                Stage = JobStage.VERIFYING,
                CommandName = "compile",
                CommandFileName = "dotnet",
                CommandArguments = ["build"],
                ExitCode = 1,
                TimedOut = false,
                StdoutText = "src/Program.cs(12,5): error CS0103: The name 'missingSymbol' does not exist in the current context",
            });

        Assert.Equal(FailureCategory.RepairableCodeFailure, classification.Category);
        Assert.Equal("verify:compile:CS0103:Program.cs", classification.Signature);
        Assert.Contains("missingSymbol", classification.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassifyDetectsFailingTests()
    {
        DeterministicFailureClassifier classifier = new();

        FailureClassification classification = classifier.Classify(
            new FailureClassificationContext
            {
                Stage = JobStage.VERIFYING,
                CommandName = "tests",
                CommandFileName = "dotnet",
                CommandArguments = ["test"],
                ExitCode = 1,
                TimedOut = false,
                StdoutText = "Failed Ringmaster.Tests.RetryTests.Should_retry_on_429 [1 ms]",
            });

        Assert.Equal(FailureCategory.RepairableCodeFailure, classification.Category);
        Assert.Equal("verify:tests:Ringmaster.Tests.RetryTests.Should_retry_on_429", classification.Signature);
        Assert.Equal("Test failure in Ringmaster.Tests.RetryTests.Should_retry_on_429.", classification.Summary);
    }

    [Fact]
    public void ClassifyDetectsTransientConnectionRefused()
    {
        DeterministicFailureClassifier classifier = new();

        FailureClassification classification = classifier.Classify(
            new FailureClassificationContext
            {
                Stage = JobStage.VERIFYING,
                CommandName = "restore",
                CommandFileName = "dotnet",
                CommandArguments = ["restore"],
                ExitCode = 1,
                TimedOut = false,
                StderrText = "Unable to load the service index for source https://api.nuget.org/v3/index.json.\n  Connection refused (api.nuget.org:443)",
            });

        Assert.Equal(FailureCategory.TransientError, classification.Category);
        Assert.Equal("verify:restore:transient", classification.Signature);
        Assert.Contains("transient", classification.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClassifyDetectsTransientFileLock()
    {
        DeterministicFailureClassifier classifier = new();

        FailureClassification classification = classifier.Classify(
            new FailureClassificationContext
            {
                Stage = JobStage.VERIFYING,
                CommandName = "build",
                CommandFileName = "dotnet",
                CommandArguments = ["build"],
                ExitCode = 1,
                TimedOut = false,
                StderrText = "error MSB4018: The process cannot access the file 'obj\\project.assets.json' because it is being used by another process.",
            });

        Assert.Equal(FailureCategory.TransientError, classification.Category);
        Assert.Equal("verify:build:transient", classification.Signature);
    }

    [Fact]
    public void ClassifyTimeoutIsTransient()
    {
        DeterministicFailureClassifier classifier = new();

        FailureClassification classification = classifier.Classify(
            new FailureClassificationContext
            {
                Stage = JobStage.VERIFYING,
                CommandName = "tests",
                CommandFileName = "dotnet",
                CommandArguments = ["test"],
                ExitCode = 0,
                TimedOut = true,
            });

        Assert.Equal(FailureCategory.TransientError, classification.Category);
        Assert.Equal("verify:tests:timeout", classification.Signature);
        Assert.Contains("timed out", classification.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
