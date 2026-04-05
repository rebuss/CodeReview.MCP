using System.Text;
using System.Text.RegularExpressions;

namespace REBUSS.Pure.RoslynProcessor;

/// <summary>
/// Parses formatted diff strings produced by <c>PlainTextFormatter</c>
/// and rebuilds them with context lines inserted.
/// </summary>
public static partial class DiffParser
{
    private static readonly Regex FilePathRegex = FilePathPattern();
    private static readonly Regex HunkHeaderRegex = HunkHeaderPattern();

    [GeneratedRegex(@"^=== (.+?) \(", RegexOptions.Multiline)]
    private static partial Regex FilePathPattern();

    [GeneratedRegex(@"^@@ -(\d+),(\d+) \+(\d+),(\d+) @@", RegexOptions.Multiline)]
    private static partial Regex HunkHeaderPattern();

    /// <summary>Extracts the file path from the diff header line.</summary>
    public static string? TryParseFilePath(string diff)
    {
        if (string.IsNullOrEmpty(diff))
            return null;

        var match = FilePathRegex.Match(diff);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Parses all hunk headers from the diff string.</summary>
    public static List<ParsedHunk> ParseHunks(string diff)
    {
        var hunks = new List<ParsedHunk>();
        var matches = HunkHeaderRegex.Matches(diff);

        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var endOffset = i + 1 < matches.Count ? matches[i + 1].Index : diff.Length;

            hunks.Add(new ParsedHunk
            {
                OldStart = int.Parse(m.Groups[1].Value),
                OldCount = int.Parse(m.Groups[2].Value),
                NewStart = int.Parse(m.Groups[3].Value),
                NewCount = int.Parse(m.Groups[4].Value),
                HeaderLine = m.Value,
                StartOffset = m.Index,
                EndOffset = endOffset
            });
        }

        return hunks;
    }

    /// <summary>
    /// Rebuilds the diff string with context lines inserted around each hunk
    /// based on the <paramref name="decision"/>.
    /// </summary>
    public static string RebuildDiffWithContext(
        string diff,
        string[] sourceLines,
        List<ParsedHunk> hunks,
        ContextDecision decision)
    {
        if (decision == ContextDecision.None || hunks.Count == 0 || sourceLines.Length == 0)
            return diff;

        int contextLines = decision == ContextDecision.Full ? 10 : 3;

        var sb = new StringBuilder();

        // Copy the header line (everything before the first hunk)
        if (hunks.Count > 0 && hunks[0].StartOffset > 0)
            sb.Append(diff[..hunks[0].StartOffset]);

        for (int i = 0; i < hunks.Count; i++)
        {
            var hunk = hunks[i];

            // Calculate context range
            int beforeStart = Math.Max(0, hunk.OldStart - 1 - contextLines);
            int beforeEnd = Math.Max(0, hunk.OldStart - 1);
            int afterStart = Math.Min(sourceLines.Length, hunk.NewStart - 1 + hunk.NewCount);
            int afterEnd = Math.Min(sourceLines.Length, afterStart + contextLines);

            int leadingContextCount = beforeEnd - beforeStart;
            int trailingContextCount = afterEnd - afterStart;

            // Updated hunk header with expanded counts
            int newOldStart = hunk.OldStart - leadingContextCount;
            int newOldCount = hunk.OldCount + leadingContextCount + trailingContextCount;
            int newNewStart = hunk.NewStart - leadingContextCount;
            int newNewCount = hunk.NewCount + leadingContextCount + trailingContextCount;

            sb.AppendLine($"@@ -{newOldStart},{newOldCount} +{newNewStart},{newNewCount} @@");

            // Leading context lines
            for (int j = beforeStart; j < beforeEnd; j++)
                sb.AppendLine($" {sourceLines[j]}");

            // Original hunk lines (everything between header and next hunk/end)
            var hunkBody = diff[(hunk.StartOffset + hunk.HeaderLine.Length)..hunk.EndOffset];
            // Skip the newline after the header
            if (hunkBody.StartsWith('\n'))
                hunkBody = hunkBody[1..];
            else if (hunkBody.StartsWith("\r\n"))
                hunkBody = hunkBody[2..];
            sb.Append(hunkBody);

            // Ensure we end with a newline before trailing context
            if (!hunkBody.EndsWith('\n'))
                sb.AppendLine();

            // Trailing context lines
            for (int j = afterStart; j < afterEnd; j++)
                sb.AppendLine($" {sourceLines[j]}");
        }

        return sb.ToString().TrimEnd();
    }
}

/// <summary>Represents a parsed hunk header from a unified diff.</summary>
public class ParsedHunk
{
    public int OldStart { get; set; }
    public int OldCount { get; set; }
    public int NewStart { get; set; }
    public int NewCount { get; set; }
    public string HeaderLine { get; set; } = string.Empty;
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
}
