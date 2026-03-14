using REBUSS.Pure.Services.Common.Models;

namespace REBUSS.Pure.Services.Common;

/// <summary>
/// Produces structured diff hunks for a single file given base and target content.
/// </summary>
public interface IStructuredDiffBuilder
{
    /// <summary>
    /// Produces a list of structured diff hunks for a single file.
    /// <c>null</c> content means the file did not exist at that commit (add/delete).
    /// Returns an empty list when both sides are identical.
    /// </summary>
    List<DiffHunk> Build(string filePath, string? baseContent, string? targetContent);
}
