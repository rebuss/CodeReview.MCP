using REBUSS.Pure.Core.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace REBUSS.Pure.Core.Shared;

/// <summary>
/// Parses unified-diff (<c>git diff -p</c>) output. Two entry points:
/// <list type="bullet">
///   <item><see cref="ParseMultiFile"/> — full multi-file output starting with <c>diff --git</c> headers.</item>
///   <item><see cref="ParseHunks"/> — single-file hunk-only patch (e.g. GitHub's per-file <c>patch</c> field).</item>
/// </list>
/// Used by the local self-review pipeline to derive <see cref="FileChange"/> directly from
/// <c>git diff</c> output, replacing the older base-vs-target re-diff path that was prone
/// to silent failures under concurrent git invocations.
/// </summary>
public static class UnifiedPatchParser
{
    private static readonly Regex HunkHeaderRegex =
        new(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@", RegexOptions.Compiled);

    // Greedy on the b-side so paths containing ` b/` (rare) still resolve correctly:
    // `a/<a-path> b/<b-path>` — for the unchanged-path case (non-rename) `<a> == <b>`,
    // so worst case the a-side group includes part of the b-path; we only consume the
    // b-side group as the fallback path source.
    private static readonly Regex DiffGitHeaderRegex =
        new(@"^diff --git a/(.+) b/(.+)$", RegexOptions.Compiled);

    /// <summary>
    /// Parses the full multi-file unified-diff output of <c>git diff -p</c> into a list of
    /// <see cref="FileChange"/>. Sets <see cref="FileChange.ChangeType"/> from
    /// <c>new file</c>/<c>deleted file</c>/<c>rename from</c> markers, and
    /// <see cref="FileChange.SkipReason"/> to <c>"binary file"</c> when the patch is a
    /// <c>Binary files ... differ</c> stub. <see cref="FileChange.Additions"/> and
    /// <see cref="FileChange.Deletions"/> are populated from hunk lines.
    /// </summary>
    public static List<FileChange> ParseMultiFile(string? patch)
    {
        var files = new List<FileChange>();
        if (string.IsNullOrEmpty(patch))
            return files;

        var lines = patch.Replace("\r\n", "\n").Split('\n');

        FileChange? current = null;
        DiffHunk? currentHunk = null;
        string? oldPath = null;
        string? newPath = null;
        string? renameTo = null;
        string? headerPath = null;  // fallback: extracted from `diff --git a/X b/X` header

        void Finalize()
        {
            if (current is null) return;

            // Path resolution priority:
            //   1. rename to (when present)
            //   2. b/path from `+++ b/<path>` (newPath)
            //   3. a/path from `--- a/<path>` (oldPath, deletion case)
            //   4. b-side of the `diff --git` header (binary-file case — git omits +++/---)
            current.Path = renameTo ?? newPath ?? oldPath ?? headerPath ?? string.Empty;

            // ChangeType inference when no explicit marker was emitted by git
            // (e.g. when --- /dev/null appears without an explicit "new file mode").
            if (string.Equals(current.ChangeType, "edit", StringComparison.Ordinal))
            {
                if (oldPath is null && newPath is not null) current.ChangeType = "add";
                else if (newPath is null && oldPath is not null) current.ChangeType = "delete";
            }

            current.Additions = current.Hunks.Sum(h => h.Lines.Count(l => l.Op == '+'));
            current.Deletions = current.Hunks.Sum(h => h.Lines.Count(l => l.Op == '-'));

            files.Add(current);

            current = null;
            currentHunk = null;
            oldPath = null;
            newPath = null;
            renameTo = null;
            headerPath = null;
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                Finalize();
                current = new FileChange { ChangeType = "edit" };
                var headerMatch = DiffGitHeaderRegex.Match(line);
                if (headerMatch.Success)
                    headerPath = headerMatch.Groups[2].Value;
                continue;
            }

            if (current is null) continue;

            // Header lines (before the first hunk for the current file).
            if (currentHunk is null)
            {
                if (line.StartsWith("--- ", StringComparison.Ordinal))
                {
                    oldPath = ExtractPathFromHeader(line.AsSpan(4), 'a');
                    continue;
                }
                if (line.StartsWith("+++ ", StringComparison.Ordinal))
                {
                    newPath = ExtractPathFromHeader(line.AsSpan(4), 'b');
                    continue;
                }
                if (line.StartsWith("new file mode ", StringComparison.Ordinal))
                {
                    current.ChangeType = "add";
                    continue;
                }
                if (line.StartsWith("deleted file mode ", StringComparison.Ordinal))
                {
                    current.ChangeType = "delete";
                    continue;
                }
                if (line.StartsWith("rename from ", StringComparison.Ordinal))
                {
                    current.ChangeType = "rename";
                    continue;
                }
                if (line.StartsWith("rename to ", StringComparison.Ordinal))
                {
                    renameTo = line["rename to ".Length..];
                    continue;
                }
                if (line.StartsWith("Binary files ", StringComparison.Ordinal)
                    && line.EndsWith(" differ", StringComparison.Ordinal))
                {
                    current.SkipReason = "binary file";
                    continue;
                }
                // index/similarity/old mode/new mode/copy from/copy to → ignore.
            }

            var hunkMatch = HunkHeaderRegex.Match(line);
            if (hunkMatch.Success)
            {
                currentHunk = BuildHunk(hunkMatch);
                current.Hunks.Add(currentHunk);
                continue;
            }

            if (currentHunk is null) continue;

            AppendHunkLine(currentHunk, line);
        }

        Finalize();
        return files;
    }

    /// <summary>
    /// Parses a single-file hunk-only patch (no <c>diff --git</c>/<c>---</c>/<c>+++</c>
    /// headers — only <c>@@</c> hunk headers and body lines). This is the format GitHub's
    /// REST API returns in the per-file <c>patch</c> field.
    /// </summary>
    public static List<DiffHunk> ParseHunks(string? patch)
    {
        var hunks = new List<DiffHunk>();
        if (string.IsNullOrEmpty(patch))
            return hunks;

        var lines = patch.Replace("\r\n", "\n").Split('\n');
        DiffHunk? current = null;

        foreach (var line in lines)
        {
            var headerMatch = HunkHeaderRegex.Match(line);
            if (headerMatch.Success)
            {
                current = BuildHunk(headerMatch);
                hunks.Add(current);
                continue;
            }

            if (current is null) continue;
            AppendHunkLine(current, line);
        }

        return hunks;
    }

    // --- helpers --------------------------------------------------------------

    private static DiffHunk BuildHunk(Match headerMatch) => new()
    {
        OldStart = ParseInt(headerMatch.Groups[1].Value),
        OldCount = headerMatch.Groups[2].Success ? ParseInt(headerMatch.Groups[2].Value) : 1,
        NewStart = ParseInt(headerMatch.Groups[3].Value),
        NewCount = headerMatch.Groups[4].Success ? ParseInt(headerMatch.Groups[4].Value) : 1,
        Lines = new List<DiffLine>()
    };

    private static void AppendHunkLine(DiffHunk hunk, string line)
    {
        if (line.Length == 0)
        {
            // A real blank context line is encoded as `" "` (length 1) in unified diffs.
            // An empty element here comes from Split('\n') on a trailing or doubled newline;
            // emitting a synthetic context line would shift alignment and corrupt the hunk.
            return;
        }

        var op = line[0];
        switch (op)
        {
            case '+':
            case '-':
            case ' ':
                hunk.Lines.Add(new DiffLine { Op = op, Text = line[1..] });
                break;
            case '\\':
                // "\ No newline at end of file" — ignore.
                break;
            default:
                // Unknown prefix — treat as context to avoid losing data.
                hunk.Lines.Add(new DiffLine { Op = ' ', Text = line });
                break;
        }
    }

    private static string? ExtractPathFromHeader(ReadOnlySpan<char> headerValue, char leading)
    {
        if (headerValue.SequenceEqual("/dev/null".AsSpan()))
            return null;

        // Drop optional trailing tab+timestamp that some git implementations emit.
        var tabIdx = headerValue.IndexOf('\t');
        if (tabIdx >= 0)
            headerValue = headerValue[..tabIdx];

        if (headerValue.Length >= 2 && headerValue[0] == leading && headerValue[1] == '/')
            return headerValue[2..].ToString();

        return headerValue.ToString();
    }

    private static int ParseInt(string s) =>
        int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
}
