using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using REBUSS.Pure.AzureDevOps.Api;
using REBUSS.Pure.AzureDevOps.Configuration;

namespace REBUSS.Pure.AzureDevOps.Providers.Diff;

/// <summary>
/// Picks the right <see cref="IDiffSourcePair"/> implementation for an iteration
/// based on file count and <see cref="AzureDevOpsDiffOptions.ZipFallbackThreshold"/>.
/// Returns <see cref="ApiDiffSourcePair"/> when <c>fileCount &lt;= threshold</c> (or
/// <c>threshold == 0</c>); otherwise downloads + extracts both archives synchronously
/// and returns a <see cref="ZipDiffSourcePair"/> wrapping the workspace.
/// </summary>
internal sealed class DiffSourcePairFactory
{
    private readonly IAzureDevOpsApiClient _apiClient;
    private readonly AzureDevOpsRepositoryArchiveProvider _archiveProvider;
    private readonly IOptions<AzureDevOpsDiffOptions> _diffOptions;
    private readonly ILogger<DiffSourcePairFactory> _logger;

    public DiffSourcePairFactory(
        IAzureDevOpsApiClient apiClient,
        AzureDevOpsRepositoryArchiveProvider archiveProvider,
        IOptions<AzureDevOpsDiffOptions> diffOptions,
        ILogger<DiffSourcePairFactory> logger)
    {
        _apiClient = apiClient;
        _archiveProvider = archiveProvider;
        _diffOptions = diffOptions;
        _logger = logger;
    }

    public async Task<IDiffSourcePair> CreateAsync(
        int fileCount, string baseCommit, string targetCommit, CancellationToken cancellationToken)
    {
        var threshold = _diffOptions.Value.ZipFallbackThreshold;
        if (threshold > 0 && fileCount > threshold)
        {
            _logger.LogInformation(
                "Using ZIP-fallback path: {FileCount} file(s) > threshold {Threshold} — " +
                "downloading base + target archives instead of {RequestCount} per-file requests",
                fileCount, threshold, fileCount * 2);
            var workspace = await ExtractedArchiveWorkspace.CreateAsync(
                _archiveProvider, baseCommit, targetCommit, _logger, cancellationToken);
            return new ZipDiffSourcePair(workspace);
        }

        return new ApiDiffSourcePair(_apiClient, baseCommit, targetCommit);
    }
}
