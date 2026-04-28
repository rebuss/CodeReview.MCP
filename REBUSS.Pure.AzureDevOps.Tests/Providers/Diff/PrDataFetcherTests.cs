using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using REBUSS.Pure.AzureDevOps.Api;
using REBUSS.Pure.AzureDevOps.Parsers;
using REBUSS.Pure.AzureDevOps.Providers.Diff;

namespace REBUSS.Pure.AzureDevOps.Tests.Providers.Diff;

/// <summary>
/// Focused unit tests for <see cref="PrDataFetcher"/>. Verifies the orchestration
/// of the three sequential ADO API calls and the parser plumbing into the
/// <see cref="PullRequestData"/> record. Real parsers are used (they're internal,
/// stateless, and cheap) so the tests double as a contract check that the JSON
/// fixtures still drive the parsers correctly after any future ADO API changes.
/// </summary>
public class PrDataFetcherTests
{
    private const string PrDetailsJson = """
        {
            "pullRequestId": 42,
            "title": "Refactor diff provider",
            "status": "active",
            "sourceRefName": "refs/heads/feature/x",
            "targetRefName": "refs/heads/main",
            "lastMergeSourceCommit": { "commitId": "aaa111" },
            "lastMergeTargetCommit": { "commitId": "bbb222" }
        }
        """;

    private const string IterationsJson = """
        {
            "value": [
                { "id": 1, "sourceRefCommit": { "commitId": "src1" }, "targetRefCommit": { "commitId": "tgt1" } },
                { "id": 2, "sourceRefCommit": { "commitId": "aaa111" }, "targetRefCommit": { "commitId": "bbb222" } }
            ]
        }
        """;

    private const string ChangesJson = """
        {
            "changeEntries": [
                { "changeType": "edit", "item": { "path": "/src/File.cs" } },
                { "changeType": "delete", "item": { "path": "/src/Removed.cs" } }
            ]
        }
        """;

    private static PrDataFetcher NewFetcher(IAzureDevOpsApiClient apiClient) => new(
        apiClient,
        new PullRequestMetadataParser(NullLogger<PullRequestMetadataParser>.Instance),
        new IterationInfoParser(NullLogger<IterationInfoParser>.Instance),
        new FileChangesParser(NullLogger<FileChangesParser>.Instance));

    [Fact]
    public async Task FetchAsync_HappyPath_BundlesMetadataIterationCommitsAndParsedFiles()
    {
        var apiClient = Substitute.For<IAzureDevOpsApiClient>();
        apiClient.GetPullRequestDetailsAsync(42).Returns(PrDetailsJson);
        apiClient.GetPullRequestIterationsAsync(42).Returns(IterationsJson);
        apiClient.GetPullRequestIterationChangesAsync(42, 2).Returns(ChangesJson);

        var data = await NewFetcher(apiClient).FetchAsync(42, CancellationToken.None);

        Assert.Equal("Refactor diff provider", data.Metadata.Title);
        // Iteration parser picks the last entry — id=2 in our fixture.
        // ADO's iteration semantic: targetRefCommit is the merge base; sourceRefCommit is
        // the new tip — `IterationInfoParser` maps them to BaseCommit / TargetCommit.
        Assert.Equal("bbb222", data.BaseCommit);
        Assert.Equal("aaa111", data.TargetCommit);
        Assert.Equal(2, data.Files.Count);
        // FileChangesParser strips the leading slash from item.path.
        Assert.Equal("src/File.cs", data.Files[0].Path);
        Assert.Equal("delete", data.Files[1].ChangeType);
    }

    [Fact]
    public async Task FetchAsync_ZeroIterationId_SkipsChangesEndpoint_ReturnsEmptyFileList()
    {
        // When ParseLast returns Id == 0 (e.g. no iterations) the changes call must be
        // bypassed — otherwise we'd issue a doomed request and return an empty parser result anyway.
        var apiClient = Substitute.For<IAzureDevOpsApiClient>();
        apiClient.GetPullRequestDetailsAsync(42).Returns(PrDetailsJson);
        apiClient.GetPullRequestIterationsAsync(42).Returns("""{ "value": [] }""");

        var data = await NewFetcher(apiClient).FetchAsync(42, CancellationToken.None);

        Assert.Empty(data.Files);
        await apiClient.DidNotReceive().GetPullRequestIterationChangesAsync(
            Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task FetchAsync_CallsEndpoints_InTheExpectedOrder()
    {
        // Sequential by design — see PrDataFetcher remarks. Verify the call sequence so
        // a future "optimization" that reorders/parallelizes them is a deliberate decision.
        var apiClient = Substitute.For<IAzureDevOpsApiClient>();
        apiClient.GetPullRequestDetailsAsync(42).Returns(PrDetailsJson);
        apiClient.GetPullRequestIterationsAsync(42).Returns(IterationsJson);
        apiClient.GetPullRequestIterationChangesAsync(42, 2).Returns(ChangesJson);

        _ = await NewFetcher(apiClient).FetchAsync(42, CancellationToken.None);

        Received.InOrder(() =>
        {
            apiClient.GetPullRequestDetailsAsync(42);
            apiClient.GetPullRequestIterationsAsync(42);
            apiClient.GetPullRequestIterationChangesAsync(42, 2);
        });
    }

    [Fact]
    public async Task FetchAsync_PropagatesApiException_FromAnyEndpoint()
    {
        var apiClient = Substitute.For<IAzureDevOpsApiClient>();
        apiClient.GetPullRequestDetailsAsync(42).Returns(PrDetailsJson);
        apiClient.GetPullRequestIterationsAsync(42)
            .Returns<Task<string>>(_ => throw new HttpRequestException("simulated 500"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            NewFetcher(apiClient).FetchAsync(42, CancellationToken.None));
    }
}
