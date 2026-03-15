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
}
