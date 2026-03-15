using System.Text.RegularExpressions;

namespace Ringmaster.Core.Jobs;

public sealed partial class DeterministicFailureClassifier : IFailureClassifier
{
    private static readonly Regex CompilerErrorRegex = CompilerErrorPattern();
    private static readonly Regex FailedTestRegex = FailedTestPattern();

    public FailureClassification Classify(FailureClassificationContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.CommandName);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.CommandFileName);

        string commandSlug = Slugify(context.CommandName);
        string stdout = context.StdoutText ?? string.Empty;
        string stderr = context.StderrText ?? string.Empty;
        string combined = string.Join(
            Environment.NewLine,
            new[] { stdout, stderr }.Where(text => !string.IsNullOrWhiteSpace(text)));

        if (context.TimedOut)
        {
            return new FailureClassification
            {
                Category = FailureCategory.ToolFailure,
                Signature = $"verify:{commandSlug}:timeout",
                Summary = $"Verification command '{context.CommandName}' timed out.",
                Highlights = TakeHighlights(combined),
            };
        }

        Match compilerMatch = CompilerErrorRegex.Match(combined);
        if (compilerMatch.Success)
        {
            string code = compilerMatch.Groups["code"].Value;
            string file = Path.GetFileName(compilerMatch.Groups["file"].Value);
            string message = compilerMatch.Groups["message"].Value.Trim();

            return new FailureClassification
            {
                Category = FailureCategory.RepairableCodeFailure,
                Signature = $"verify:{commandSlug}:{code}:{file}",
                Summary = string.IsNullOrWhiteSpace(message)
                    ? $"Compilation failed with {code} in {file}."
                    : message,
                Highlights = TakeHighlights(combined),
            };
        }

        Match failedTestMatch = FailedTestRegex.Match(combined);
        if (failedTestMatch.Success)
        {
            string testName = failedTestMatch.Groups["test"].Value.Trim();

            return new FailureClassification
            {
                Category = FailureCategory.RepairableCodeFailure,
                Signature = $"verify:{commandSlug}:{testName}",
                Summary = $"Test failure in {testName}.",
                Highlights = TakeHighlights(combined),
            };
        }

        return new FailureClassification
        {
            Category = FailureCategory.ToolFailure,
            Signature = $"verify:{commandSlug}:exit-{context.ExitCode}",
            Summary = $"Verification command '{context.CommandName}' failed with exit code {context.ExitCode}.",
            Highlights = TakeHighlights(combined),
        };
    }

    private static IReadOnlyList<string> TakeHighlights(string combined)
    {
        return combined
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(6)
            .ToArray();
    }

    private static string Slugify(string value)
    {
        char[] characters = value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        string slug = new string(characters).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "command" : slug;
    }

    [GeneratedRegex(@"(?<file>[^\s:(]+)\(\d+,\d+\):\s*error\s+(?<code>[A-Z]+\d+):\s*(?<message>.+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CompilerErrorPattern();

    [GeneratedRegex(@"^Failed\s+(?<test>[^\r\n\[]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex FailedTestPattern();
}
