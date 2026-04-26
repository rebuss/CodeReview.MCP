namespace REBUSS.Pure.Services.LocalReview
{
    /// <summary>
    /// Provides low-level access to local git repository data needed for self-review.
    /// All operations are scoped to a specific repository root directory.
    /// </summary>
    public interface ILocalGitClient
    {
        /// <summary>
        /// Returns a list of changed files for the given <paramref name="scope"/>.
        /// Each entry describes the status and path of one changed file.
        /// </summary>
        Task<IReadOnlyList<LocalFileStatus>> GetChangedFilesAsync(
            string repositoryRoot,
            LocalReviewScope scope,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the content of a file at a specific git ref (commit SHA, branch, HEAD, etc.).
        /// Returns <c>null</c> when the file does not exist at that ref (e.g. new file).
        /// </summary>
        Task<string?> GetFileContentAtRefAsync(
            string repositoryRoot,
            string filePath,
            string gitRef,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the full unified-diff (<c>git diff -p</c>) for the given <paramref name="scope"/>
        /// in a single git invocation. Caller parses the returned string with
        /// <see cref="REBUSS.Pure.Core.Shared.UnifiedPatchParser.ParseMultiFile"/>.
        /// </summary>
        /// <remarks>
        /// This is the authoritative, low-contention path for local self-review diffs.
        /// One process per scope replaces the prior <c>2 × file count</c> parallel
        /// <c>git show</c> invocations, eliminating the IDE-side concurrency hazard
        /// (lock contention with VS Code's Git extension; AV process-startup penalty).
        /// </remarks>
        Task<string> GetUnifiedDiffAsync(
            string repositoryRoot,
            LocalReviewScope scope,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the name of the currently checked-out branch,
        /// or <c>null</c> when in detached HEAD state.
        /// </summary>
        Task<string?> GetCurrentBranchAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The status and path of a locally changed file.
    /// </summary>
    public sealed record LocalFileStatus(
        /// <summary>Git status code: 'A'=added, 'M'=modified, 'D'=deleted, 'R'=renamed, '?'=untracked.</summary>
        char Status,
        string Path,
        /// <summary>Original path before rename; <c>null</c> when not a rename.</summary>
        string? OriginalPath = null);
}
