using REBUSS.Pure.AzureDevOps.Api;

namespace REBUSS.Pure.AzureDevOps.Providers.Diff;

/// <summary>
/// Per-file content fetched via Azure DevOps API. Two parallel
/// <see cref="IAzureDevOpsApiClient.GetFileContentAtCommitAsync"/> calls per file —
/// the throttling discussion in <see cref="IDiffSourcePair"/> applies. Stateless;
/// <see cref="DisposeAsync"/> is a no-op.
/// </summary>
internal sealed class ApiDiffSourcePair : IDiffSourcePair
{
    private readonly IAzureDevOpsApiClient _apiClient;
    private readonly string _baseCommit;
    private readonly string _targetCommit;

    public ApiDiffSourcePair(IAzureDevOpsApiClient apiClient, string baseCommit, string targetCommit)
    {
        _apiClient = apiClient;
        _baseCommit = baseCommit;
        _targetCommit = targetCommit;
    }

    public async Task<(string? baseContent, string? targetContent)> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var baseTask = _apiClient.GetFileContentAtCommitAsync(_baseCommit, path);
        var targetTask = _apiClient.GetFileContentAtCommitAsync(_targetCommit, path);
        await Task.WhenAll(baseTask, targetTask);
        return (await baseTask, await targetTask);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
