using System.Text.RegularExpressions;

namespace Ringmaster.Core.Jobs;

public sealed partial class DeterministicFailureClassifier : IFailureClassifier
{
    private static readonly Regex CompilerErrorRegex = CompilerErrorPattern();
    private static readonly Regex FailedTestRegex = FailedTestPattern();
    private static readonly Regex TransientErrorRegex = TransientErrorPattern();

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
                Category = FailureCategory.TransientError,
                Signature = $"verify:{commandSlug}:timeout",
                Summary = $"Verification command '{context.CommandName}' timed out (transient).",
                Highlights = TakeHighlights(combined),
            };
        }

        if (TransientErrorRegex.IsMatch(combined))
        {
            return new FailureClassification
            {
                Category = FailureCategory.TransientError,
                Signature = $"verify:{commandSlug}:transient",
                Summary = $"Verification command '{context.CommandName}' encountered a transient infrastructure error.",
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

    [GeneratedRegex(@"(connection\s+(refused|timed\s*out|reset)|no\s+route\s+to\s+host|network\s+is\s+unreachable|temporary\s+failure\s+in\s+name\s+resolution|resource\s+temporarily\s+unavailable|the\s+process\s+cannot\s+access\s+the\s+file.*another\s+process|lock\s+file\s+could\s+not\s+be\s+created)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TransientErrorPattern();
}
