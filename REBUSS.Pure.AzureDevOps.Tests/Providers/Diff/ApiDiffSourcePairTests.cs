using NSubstitute;
using REBUSS.Pure.AzureDevOps.Api;
using REBUSS.Pure.AzureDevOps.Providers.Diff;

namespace REBUSS.Pure.AzureDevOps.Tests.Providers.Diff;

/// <summary>
/// Focused unit tests for <see cref="ApiDiffSourcePair"/> — the per-file content
/// fetch via two parallel <see cref="IAzureDevOpsApiClient.GetFileContentAtCommitAsync"/>
/// calls.
/// </summary>
public class ApiDiffSourcePairTests
{
    [Fact]
    public async Task ReadAsync_ReturnsBaseAndTargetContent_FromTwoCommitFetches()
    {
        var apiClient = Substitute.For<IAzureDevOpsApiClient>();
        apiClient.GetFileContentAtCommitAsync("base-sha", "src/File.cs").Returns("base content");
        apiClient.GetFileContentAtCommitAsync("target-sha", "src/File.cs").Returns("target content");

        var pair = new ApiDiffSourcePair(apiClient, "base-sha", "target-sha");
        var (baseContent, targetContent) = await pair.ReadAsync("src/File.cs", CancellationToken.None);

        Assert.Equal("base content", baseContent);
        Assert.Equal("target content", targetContent);
    }

    [Fact]
    public async Task ReadAsync_PassesPathThroughVerbatim()
    {
        // The pair must not normalize/transform the path — the diff caller already
        // uses its own NormalizePath upstream; double-normalization would break.
        var apiClient = Substitute.For<IAzureDevOpsApiClient>();
        apiClient.GetFileContentAtCommitAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns("any");

        var pair = new ApiDiffSourcePair(apiClient, "base", "target");
        await pair.ReadAsync("/leading/slash/Path.cs", CancellationToken.None);

        await apiClient.Received(1).GetFileContentAtCommitAsync("base", "/leading/slash/Path.cs");
        await apiClient.Received(1).GetFileContentAtCommitAsync("target", "/leading/slash/Path.cs");
    }

    [Fact]
    public async Task ReadAsync_NullContent_PassesThrough()
    {
        // For added/deleted files, one side returns null; the pair should pass it through.
        var apiClient = Substitute.For<IAzureDevOpsApiClient>();
        apiClient.GetFileContentAtCommitAsync("base-sha", "src/Added.cs").Returns((string?)null);
        apiClient.GetFileContentAtCommitAsync("target-sha", "src/Added.cs").Returns("new file content");

        var pair = new ApiDiffSourcePair(apiClient, "base-sha", "target-sha");
        var (baseContent, targetContent) = await pair.ReadAsync("src/Added.cs", CancellationToken.None);

        Assert.Null(baseContent);
        Assert.Equal("new file content", targetContent);
    }

    [Fact]
    public async Task DisposeAsync_IsNoOp_AndCanBeCalledMultipleTimes()
    {
        var apiClient = Substitute.For<IAzureDevOpsApiClient>();
        var pair = new ApiDiffSourcePair(apiClient, "base", "target");

        await pair.DisposeAsync();
        await pair.DisposeAsync();
        await pair.DisposeAsync();

        // Stateless dispose — test passes when no exception is thrown.
    }
}
