using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;
using Ringmaster.Git;

namespace Ringmaster.Codex;

public sealed class PullRequestDraftBuilder(Ringmaster.Infrastructure.Persistence.AtomicFileWriter atomicFileWriter)
{
    public async Task WriteAsync(
        StoredJob job,
        ReviewerAgentOutput review,
        ReviewRisk? risk,
        CancellationToken cancellationToken)
    {
        string jobDirectoryPath = job.JobDirectoryPath;
        string notesPath = Path.Combine(jobDirectoryPath, "NOTES.md");
        string verificationSummaryPath = Path.Combine(jobDirectoryPath, "artifacts", "verification-summary.json");
        string changedFilesPath = Path.Combine(jobDirectoryPath, "artifacts", "changed-files.json");

        string notesText = File.Exists(notesPath)
            ? await File.ReadAllTextAsync(notesPath, cancellationToken)
            : string.Empty;
        VerificationSummary? verificationSummary = null;
        if (File.Exists(verificationSummaryPath))
        {
            string verificationJson = await File.ReadAllTextAsync(verificationSummaryPath, cancellationToken);
            verificationSummary = RingmasterJsonSerializer.Deserialize<VerificationSummary>(verificationJson);
        }

        IReadOnlyList<string> changedFiles = [];
        if (File.Exists(changedFilesPath))
        {
            string changedFilesJson = await File.ReadAllTextAsync(changedFilesPath, cancellationToken);
            changedFiles = RingmasterJsonSerializer.Deserialize<IReadOnlyList<string>>(changedFilesJson);
        }

        List<string> lines =
        [
            $"# {job.Definition.Title}",
            string.Empty,
            "## Summary",
            review.Summary,
        ];

        string notesBody = StripHeading(notesText);
        if (!string.IsNullOrWhiteSpace(notesBody))
        {
            lines.Add(string.Empty);
            lines.Add("## Implementation Notes");
            lines.Add(notesBody.Trim());
        }

        if (changedFiles.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Changed Files");
            lines.AddRange(changedFiles.Select(file => $"- {file}"));
        }

        if (verificationSummary is not null)
        {
            lines.Add(string.Empty);
            lines.Add("## Verification");
            lines.AddRange(
                verificationSummary.Commands.Select(
                    command => $"- `{command.FileName} {string.Join(' ', command.Arguments)}` (exit {command.ExitCode})"));
        }

        lines.Add(string.Empty);
        lines.Add("## Review");
        lines.Add($"- Verdict: {review.Verdict}");
        if (risk is not null)
        {
            lines.Add($"- Risk: {risk}");
        }

        if (review.Findings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Findings");
            lines.AddRange(review.Findings.Select(finding => $"- {finding.Severity}: {finding.Message}"));
        }

        await atomicFileWriter.WriteTextAsync(
            Path.Combine(jobDirectoryPath, "PR.md"),
            string.Join(Environment.NewLine, lines) + Environment.NewLine,
            cancellationToken);
    }

    private static string StripHeading(string markdown)
    {
        string[] lines = markdown
            .Split(Environment.NewLine, StringSplitOptions.None)
            .SkipWhile(line => line.StartsWith('#') || string.IsNullOrWhiteSpace(line))
            .ToArray();
        return string.Join(Environment.NewLine, lines).Trim();
    }
}
