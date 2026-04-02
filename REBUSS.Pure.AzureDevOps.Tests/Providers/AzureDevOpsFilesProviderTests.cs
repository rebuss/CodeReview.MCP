using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using REBUSS.Pure.AzureDevOps.Api;
using REBUSS.Pure.Core.Models;
using REBUSS.Pure.Core.Shared;
using REBUSS.Pure.AzureDevOps.Parsers;
using REBUSS.Pure.AzureDevOps.Providers;

namespace REBUSS.Pure.AzureDevOps.Tests.Providers;

public class AzureDevOpsFilesProviderTests
{
    private readonly IAzureDevOpsApiClient _apiClient = Substitute.For<IAzureDevOpsApiClient>();
    private readonly AzureDevOpsFilesProvider _provider;

    private const string IterationsJson = """
        {
            "value": [
                {
                    "id": 1,
                    "sourceRefCommit": { "commitId": "head111" },
                    "commonRefCommit": { "commitId": "base222" }
                },
                {
                    "id": 2,
                    "sourceRefCommit": { "commitId": "head333" },
                    "commonRefCommit": { "commitId": "base444" }
                }
            ]
        }
        """;

    public AzureDevOpsFilesProviderTests()
    {
        var fileChangesParser = new FileChangesParser(NullLogger<FileChangesParser>.Instance);
        var iterationInfoParser = new IterationInfoParser(NullLogger<IterationInfoParser>.Instance);
        var fileClassifier = new FileClassifier();

        _provider = new AzureDevOpsFilesProvider(
            _apiClient, fileChangesParser, iterationInfoParser, fileClassifier,
            NullLogger<AzureDevOpsFilesProvider>.Instance);
    }

    [Fact]
    public async Task GetFilesAsync_MapsFileInfoCorrectly()
    {
        const string changesJson = """
            {
                "changeEntries": [
                    { "changeType": "edit", "item": { "path": "/src/Service.cs" } }
                ]
            }
            """;

        _apiClient.GetPullRequestIterationsAsync(42).Returns(IterationsJson);
        _apiClient.GetPullRequestIterationChangesAsync(42, 2).Returns(changesJson);

        var result = await _provider.GetFilesAsync(42);

        Assert.Single(result.Files);
        var file = result.Files[0];
        Assert.Equal("src/Service.cs", file.Path);
        Assert.Equal("modified", file.Status);
        // Azure DevOps iteration-changes API does not return line counts — always zero
        Assert.Equal(0, file.Additions);
        Assert.Equal(0, file.Deletions);
        Assert.Equal(0, file.Changes);
        Assert.Equal(".cs", file.Extension);
        Assert.False(file.IsBinary);
        Assert.False(file.IsGenerated);
        Assert.False(file.IsTestFile);
        Assert.Equal("high", file.ReviewPriority);
    }

    [Fact]
    public async Task GetFilesAsync_MapsStatusCorrectly()
    {
        const string changesJson = """
            {
                "changeEntries": [
                    { "changeType": "add",    "item": { "path": "/a.cs" } },
                    { "changeType": "edit",   "item": { "path": "/b.cs" } },
                    { "changeType": "delete", "item": { "path": "/c.cs" } },
                    { "changeType": "rename", "item": { "path": "/d.cs" } }
                ]
            }
            """;

        _apiClient.GetPullRequestIterationsAsync(1).Returns(IterationsJson);
        _apiClient.GetPullRequestIterationChangesAsync(1, 2).Returns(changesJson);

        var result = await _provider.GetFilesAsync(1);

        Assert.Equal("added", result.Files[0].Status);
        Assert.Equal("modified", result.Files[1].Status);
        Assert.Equal("removed", result.Files[2].Status);
        Assert.Equal("renamed", result.Files[3].Status);
    }

    [Fact]
    public async Task GetFilesAsync_BuildsSummaryCorrectly()
    {
        const string changesJson = """
            {
                "changeEntries": [
                    { "changeType": "edit", "item": { "path": "/src/App.cs" } },
                    { "changeType": "edit", "item": { "path": "/tests/AppTests.cs" } },
                    { "changeType": "edit", "item": { "path": "/appsettings.json" } },
                    { "changeType": "edit", "item": { "path": "/docs/readme.md" } },
                    { "changeType": "add",  "item": { "path": "/lib/tool.dll" } },
                    { "changeType": "edit", "item": { "path": "/obj/Debug/net8.0/out.cs" } }
                ]
            }
            """;

        _apiClient.GetPullRequestIterationsAsync(10).Returns(IterationsJson);
        _apiClient.GetPullRequestIterationChangesAsync(10, 2).Returns(changesJson);

        var result = await _provider.GetFilesAsync(10);

        Assert.Equal(6, result.Files.Count);
        Assert.Equal(1, result.Summary.SourceFiles);
        Assert.Equal(1, result.Summary.TestFiles);
        Assert.Equal(1, result.Summary.ConfigFiles);
        Assert.Equal(1, result.Summary.DocsFiles);
        Assert.Equal(1, result.Summary.BinaryFiles);
        Assert.Equal(1, result.Summary.GeneratedFiles);
        Assert.Equal(1, result.Summary.HighPriorityFiles);
    }

    [Fact]
    public async Task GetFilesAsync_HandlesEmptyFileList()
    {
        const string changesJson = """{ "changeEntries": [] }""";

        _apiClient.GetPullRequestIterationsAsync(5).Returns(IterationsJson);
        _apiClient.GetPullRequestIterationChangesAsync(5, 2).Returns(changesJson);

        var result = await _provider.GetFilesAsync(5);

        Assert.Empty(result.Files);
        Assert.Equal(0, result.Summary.SourceFiles);
        Assert.Equal(0, result.Summary.HighPriorityFiles);
    }

    [Fact]
    public async Task GetFilesAsync_HandlesFileWithZeroLineCounts()
    {
        const string changesJson = """
            {
                "changeEntries": [
                    { "changeType": "edit", "item": { "path": "/src/Empty.cs" } }
                ]
            }
            """;

        _apiClient.GetPullRequestIterationsAsync(6).Returns(IterationsJson);
        _apiClient.GetPullRequestIterationChangesAsync(6, 2).Returns(changesJson);

        var result = await _provider.GetFilesAsync(6);

        var file = Assert.Single(result.Files);
        // Azure DevOps iteration-changes API does not return line counts — always zero
        Assert.Equal(0, file.Additions);
        Assert.Equal(0, file.Deletions);
        Assert.Equal(0, file.Changes);
    }

    [Fact]
    public async Task GetFilesAsync_StripsLeadingSlashFromPath()
    {
        const string changesJson = """
            {
                "changeEntries": [
                    { "changeType": "edit", "item": { "path": "/src/A.cs" } }
                ]
            }
            """;

        _apiClient.GetPullRequestIterationsAsync(7).Returns(IterationsJson);
        _apiClient.GetPullRequestIterationChangesAsync(7, 2).Returns(changesJson);

        var result = await _provider.GetFilesAsync(7);

        Assert.Equal("src/A.cs", result.Files[0].Path);
    }

    [Fact]
    public async Task GetFilesAsync_UsesLastIteration()
    {
        const string changesJson = """
            {
                "changeEntries": [
                    { "changeType": "edit", "item": { "path": "/src/A.cs" } }
                ]
            }
            """;

        _apiClient.GetPullRequestIterationsAsync(99).Returns(IterationsJson);
        _apiClient.GetPullRequestIterationChangesAsync(99, 2).Returns(changesJson);

        await _provider.GetFilesAsync(99);

        // Verify it used iteration ID 2 (the last one), not 1
        await _apiClient.Received(1).GetPullRequestIterationChangesAsync(99, 2);
        await _apiClient.DidNotReceive().GetPullRequestIterationChangesAsync(99, 1);
    }

    [Fact]
    public async Task GetFilesAsync_NoIterations_ReturnsEmptyResult()
    {
        const string emptyIterationsJson = """{ "value": [] }""";

        _apiClient.GetPullRequestIterationsAsync(8).Returns(emptyIterationsJson);

        var result = await _provider.GetFilesAsync(8);

        Assert.Empty(result.Files);
        Assert.NotNull(result.Summary);
    }
}
