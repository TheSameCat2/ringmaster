using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;

namespace Ringmaster.Core.Tests;

public sealed class RingmasterJsonSerializerTests
{
    [Fact]
    public void SerializeAndDeserializeRoundTripsQueuedJobDefinitionAndStatus()
    {
        JobDefinition definition = new()
        {
            JobId = "job-20260315-7f3c9b2a",
            Title = "Add retry handling",
            Description = "Implement bounded retries for retryable failures.",
            AcceptanceCriteria = ["Adds tests", "Does not retry non-429 client errors"],
            Constraints = new JobConstraints
            {
                AllowedPaths = ["src/**", "tests/**"],
                ForbiddenPaths = ["docs/**"],
                MaxFilesChangedSoft = 20,
            },
            Repo = new JobRepositoryTarget
            {
                BaseBranch = "master",
                VerificationProfile = "default",
            },
            Pr = new JobPullRequestOptions
            {
                AutoOpen = false,
                DraftByDefault = true,
                Labels = ["automation"],
            },
            Priority = 50,
            CreatedAtUtc = new DateTimeOffset(2026, 3, 15, 16, 45, 0, TimeSpan.Zero),
            CreatedBy = "tester",
        };

        JobStatusSnapshot status = JobStatusSnapshot.CreateInitial(definition);

        string definitionJson = RingmasterJsonSerializer.Serialize(definition);
        string statusJson = RingmasterJsonSerializer.Serialize(status);

        JobDefinition roundTrippedDefinition = RingmasterJsonSerializer.Deserialize<JobDefinition>(definitionJson);
        JobStatusSnapshot roundTrippedStatus = RingmasterJsonSerializer.Deserialize<JobStatusSnapshot>(statusJson);

        Assert.Equal(definition.JobId, roundTrippedDefinition.JobId);
        Assert.Equal(definition.Title, roundTrippedDefinition.Title);
        Assert.Equal(definition.Description, roundTrippedDefinition.Description);
        Assert.Equal(definition.AcceptanceCriteria, roundTrippedDefinition.AcceptanceCriteria);
        Assert.Equal(definition.Constraints.AllowedPaths, roundTrippedDefinition.Constraints.AllowedPaths);
        Assert.Equal(definition.Constraints.ForbiddenPaths, roundTrippedDefinition.Constraints.ForbiddenPaths);
        Assert.Equal(definition.Repo, roundTrippedDefinition.Repo);
        Assert.Equal(definition.Pr.AutoOpen, roundTrippedDefinition.Pr.AutoOpen);
        Assert.Equal(definition.Pr.DraftByDefault, roundTrippedDefinition.Pr.DraftByDefault);
        Assert.Equal(definition.Pr.Labels, roundTrippedDefinition.Pr.Labels);
        Assert.Equal(status, roundTrippedStatus);
        Assert.Contains("\"state\": \"QUEUED\"", statusJson, StringComparison.Ordinal);
    }
}
