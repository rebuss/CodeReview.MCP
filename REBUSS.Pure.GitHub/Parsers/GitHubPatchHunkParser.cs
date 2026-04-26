using REBUSS.Pure.Core.Models;
using REBUSS.Pure.Core.Shared;

namespace REBUSS.Pure.GitHub.Parsers;

/// <summary>
/// Parses a unified-diff patch string (the <c>patch</c> field returned by GitHub's
/// <c>/pulls/{n}/files</c> endpoint) into <see cref="DiffHunk"/> instances. The patch
/// already encodes the structured diff that <see cref="REBUSS.Pure.Core.Shared.IStructuredDiffBuilder"/>
/// would otherwise compute by re-diffing the full file contents — using GitHub's pre-built
/// patch eliminates two API calls per changed file.
/// </summary>
/// <remarks>
/// Thin wrapper over <see cref="UnifiedPatchParser.ParseHunks"/>. GitHub's per-file patch
/// is hunk-only (no <c>diff --git</c> framing), so the shared <c>ParseHunks</c> entry point
/// is the right primitive.
/// </remarks>
public static class GitHubPatchHunkParser
{
    /// <summary>
    /// Parses the given patch into a list of hunks. Returns an empty list when the patch is
    /// null/empty or contains no hunk headers.
    /// </summary>
    public static List<DiffHunk> Parse(string? patch) =>
        UnifiedPatchParser.ParseHunks(patch);
}
