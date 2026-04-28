using REBUSS.Pure.AzureDevOps.Api;
using REBUSS.Pure.AzureDevOps.Parsers;
using REBUSS.Pure.Core.Models;

namespace REBUSS.Pure.AzureDevOps.Providers.Diff;

/// <summary>
/// Bundle of PR data needed by the diff pipeline: metadata, the iteration's base
/// + target commit SHAs, and the parsed file-changes list. Returned by
/// <see cref="PrDataFetcher.FetchAsync"/>; consumed exclusively by
/// <see cref="AzureDevOpsDiffProvider"/>.
/// </summary>
internal readonly record struct PullRequestData(
    PullRequestMetadata Metadata,
    List<FileChange> Files,
    string BaseCommit,
    string TargetCommit);

/// <summary>
/// Fetches and parses the three Azure DevOps API responses the diff pipeline
/// needs (PR details, last iteration, iteration changes), bundling the result
/// into a <see cref="PullRequestData"/> record. Sequential by design — the
/// iteration-changes call depends on <c>iteration.Id</c> from the iterations
/// response, and parallelizing details + iterations is a future optimization
/// out of scope for the current refactor.
/// </summary>
internal sealed class PrDataFetcher
{
    private readonly IAzureDevOpsApiClient _apiClient;
    private readonly IPullRequestMetadataParser _metadataParser;
    private readonly IIterationInfoParser _iterationParser;
    private readonly IFileChangesParser _changesParser;

    public PrDataFetcher(
        IAzureDevOpsApiClient apiClient,
        IPullRequestMetadataParser metadataParser,
        IIterationInfoParser iterationParser,
        IFileChangesParser changesParser)
    {
        _apiClient = apiClient;
        _metadataParser = metadataParser;
        _iterationParser = iterationParser;
        _changesParser = changesParser;
    }

    public async Task<PullRequestData> FetchAsync(int prNumber, CancellationToken cancellationToken)
    {
        var metadata = _metadataParser.Parse(await _apiClient.GetPullRequestDetailsAsync(prNumber));
        var iteration = _iterationParser.ParseLast(await _apiClient.GetPullRequestIterationsAsync(prNumber));

        var changesJson = iteration.Id > 0
            ? await _apiClient.GetPullRequestIterationChangesAsync(prNumber, iteration.Id)
            : "{}";
        var files = _changesParser.Parse(changesJson);

        return new PullRequestData(metadata, files, iteration.BaseCommit, iteration.TargetCommit);
    }
}
