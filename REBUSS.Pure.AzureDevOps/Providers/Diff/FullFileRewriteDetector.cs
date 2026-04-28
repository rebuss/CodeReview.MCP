using REBUSS.Pure.Core.Models;

namespace REBUSS.Pure.AzureDevOps.Providers.Diff;

/// <summary>
/// Detects a full-file rewrite — both contents are non-trivial but every line in
/// the file was changed (deletions == old line count and additions == new line count).
/// Files matching this pattern are skipped from the structured diff because the
/// rebuilt hunks would be a wall of red+green lines that carry no signal beyond
/// "the file was rewritten".
/// </summary>
internal static class FullFileRewriteDetector
{
    private const int FullRewriteMinLineCount = 10;

    public static bool IsFullFileRewrite(string? baseContent, string? targetContent, IReadOnlyList<DiffHunk> hunks)
    {
        if (string.IsNullOrEmpty(baseContent) || string.IsNullOrEmpty(targetContent))
            return false;

        if (hunks.Count == 0)
            return false;

        var oldLineCount = baseContent.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Length;
        var newLineCount = targetContent.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Length;

        if (oldLineCount < FullRewriteMinLineCount && newLineCount < FullRewriteMinLineCount)
            return false;

        var allLines = hunks.SelectMany(h => h.Lines);
        var deletions = allLines.Count(l => l.Op == '-');
        var additions = allLines.Count(l => l.Op == '+');

        return deletions == oldLineCount && additions == newLineCount;
    }
}
