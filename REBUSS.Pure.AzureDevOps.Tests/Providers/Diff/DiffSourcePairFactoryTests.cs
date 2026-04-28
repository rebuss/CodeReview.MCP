using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using REBUSS.Pure.AzureDevOps.Api;
using REBUSS.Pure.AzureDevOps.Configuration;
using REBUSS.Pure.AzureDevOps.Providers;
using REBUSS.Pure.AzureDevOps.Providers.Diff;

namespace REBUSS.Pure.AzureDevOps.Tests.Providers.Diff;

/// <summary>
/// Focused dispatch tests for <see cref="DiffSourcePairFactory.CreateAsync"/>. The
/// ZIP path's full extract/cleanup lifecycle is covered by the integration tests in
/// <c>AzureDevOpsDiffProviderTests</c> (real ZIP archives via
/// <c>System.IO.Compression.ZipArchive</c>); the cases here exercise the threshold
/// dispatch contract using fake counts so we don't have to spin up real archives.
/// </summary>
public class DiffSourcePairFactoryTests
{
    private static DiffSourcePairFactory NewFactory(int threshold)
    {
        var apiClient = Substitute.For<IAzureDevOpsApiClient>();
        var archiveProvider = new AzureDevOpsRepositoryArchiveProvider(apiClient);
        var options = Options.Create(new AzureDevOpsDiffOptions { ZipFallbackThreshold = threshold });
        return new DiffSourcePairFactory(
            apiClient, archiveProvider, options, NullLogger<DiffSourcePairFactory>.Instance);
    }

    [Fact]
    public async Task CreateAsync_FileCountAtThreshold_PicksApiPath()
    {
        // Boundary: count == threshold → API path (the > check is strict).
        var factory = NewFactory(threshold: 30);

        await using var pair = await factory.CreateAsync(
            fileCount: 30, baseCommit: "base", targetCommit: "target", CancellationToken.None);

        Assert.IsType<ApiDiffSourcePair>(pair);
    }

    [Fact]
    public async Task CreateAsync_FileCountUnderThreshold_PicksApiPath()
    {
        var factory = NewFactory(threshold: 30);

        await using var pair = await factory.CreateAsync(
            fileCount: 5, baseCommit: "base", targetCommit: "target", CancellationToken.None);

        Assert.IsType<ApiDiffSourcePair>(pair);
    }

    [Fact]
    public async Task CreateAsync_ThresholdZero_AlwaysPicksApiPath_RegardlessOfCount()
    {
        // threshold == 0 disables the ZIP path entirely (per AzureDevOpsDiffOptions docs).
        var factory = NewFactory(threshold: 0);

        await using var pair = await factory.CreateAsync(
            fileCount: 9999, baseCommit: "base", targetCommit: "target", CancellationToken.None);

        Assert.IsType<ApiDiffSourcePair>(pair);
    }

    // The ZIP-path branch (count > threshold > 0) is exercised end-to-end by the
    // ZIP-fallback heuristic integration tests in AzureDevOpsDiffProviderTests, which
    // construct real ZIP archives via System.IO.Compression.ZipArchive. Reproducing
    // that here would duplicate the integration coverage with no extra signal.
}
