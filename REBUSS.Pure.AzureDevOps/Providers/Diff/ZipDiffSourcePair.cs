namespace REBUSS.Pure.AzureDevOps.Providers.Diff;

/// <summary>
/// Per-file content read from an <see cref="ExtractedArchiveWorkspace"/>. Both
/// archives have already been downloaded + extracted before this instance is
/// returned by the factory, so <see cref="ReadAsync"/> only does a path-resolve
/// and a synchronous file read. Owns the workspace's lifetime — <see cref="DisposeAsync"/>
/// removes the temp directory.
/// </summary>
internal sealed class ZipDiffSourcePair : IDiffSourcePair
{
    private readonly ExtractedArchiveWorkspace _workspace;

    public ZipDiffSourcePair(ExtractedArchiveWorkspace workspace)
    {
        _workspace = workspace;
    }

    public Task<(string? baseContent, string? targetContent)> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var basePath = ExtractedArchiveWorkspace.TryResolveFilePath(_workspace.BaseDir, path);
        var targetPath = ExtractedArchiveWorkspace.TryResolveFilePath(_workspace.TargetDir, path);

        var baseContent = basePath is not null ? File.ReadAllText(basePath) : null;
        var targetContent = targetPath is not null ? File.ReadAllText(targetPath) : null;
        return Task.FromResult<(string?, string?)>((baseContent, targetContent));
    }

    public ValueTask DisposeAsync() => _workspace.DisposeAsync();
}
