namespace REBUSS.Pure.AzureDevOps.Providers.Diff;

/// <summary>
/// Strategy seam for fetching the (base, target) content pair for a single file
/// during diff construction. Two implementations are provided:
/// <list type="bullet">
///   <item><see cref="ApiDiffSourcePair"/> — issues two parallel <c>GetFileContentAtCommitAsync</c>
///         calls per file. Default for PRs at or under <c>ZipFallbackThreshold</c>.</item>
///   <item><see cref="ZipDiffSourcePair"/> — reads from extracted base/target archives
///         on disk via <see cref="ExtractedArchiveWorkspace.TryResolveFilePath"/>. Used for
///         large refactors where per-file API calls would trigger Azure DevOps TSTU
///         throttling.</item>
/// </list>
/// The factory <see cref="DiffSourcePairFactory"/> picks the right implementation up
/// front based on file count and constructs it with the iteration's base+target SHAs;
/// <see cref="AzureDevOpsDiffProvider"/> then runs a single unified parallel loop over
/// every file. <see cref="IAsyncDisposable"/> covers the ZIP variant's temp-directory
/// cleanup; the API variant's <c>DisposeAsync</c> is a no-op.
/// </summary>
internal interface IDiffSourcePair : IAsyncDisposable
{
    Task<(string? baseContent, string? targetContent)> ReadAsync(string path, CancellationToken cancellationToken);
}
