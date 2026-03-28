using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using REBUSS.Pure.GitHub.Api;
using REBUSS.Pure.GitHub.Parsers;
using REBUSS.Pure.GitHub.Providers;
using REBUSS.Pure.Core.Shared;

namespace REBUSS.Pure.GitHub.Tests.Providers;

public class GitHubFilesProviderTests
{
    private readonly IGitHubApiClient _apiClient = Substitute.For<IGitHubApiClient>();
    private readonly GitHubFilesProvider _provider;

    public GitHubFilesProviderTests()
    {
        var changesParser = new GitHubFileChangesParser(NullLogger<GitHubFileChangesParser>.Instance);
        var fileClassifier = new FileClassifier();

        _provider = new GitHubFilesProvider(
            _apiClient, changesParser, fileClassifier,
            NullLogger<GitHubFilesProvider>.Instance);
    }

    [Fact]
    public async Task GetFilesAsync_ReturnsClassifiedFiles()
    {
        const string filesJson = """
            [
                { "filename": "src/App.cs", "status": "modified", "additions": 5, "deletions": 2 },
                { "filename": "tests/AppTests.cs", "status": "added", "additions": 10, "deletions": 0 }
            ]
            """;

        _apiClient.GetPullRequestFilesAsync(10).Returns(filesJson);

        var result = await _provider.GetFilesAsync(10);

        Assert.NotNull(result);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal("src/App.cs", result.Files[0].Path);
        Assert.Equal("modified", result.Files[0].Status);
        Assert.Equal(5, result.Files[0].Additions);
        Assert.Equal(2, result.Files[0].Deletions);
        Assert.Equal(7, result.Files[0].Changes);
        Assert.Equal("tests/AppTests.cs", result.Files[1].Path);
        Assert.Equal("added", result.Files[1].Status);
        Assert.Equal(10, result.Files[1].Additions);
    }

    [Fact]
    public async Task GetFilesAsync_IncludesSummary()
    {
        const string filesJson = """
            [
                { "filename": "src/Service.cs", "status": "modified", "additions": 3, "deletions": 1 }
            ]
            """;

        _apiClient.GetPullRequestFilesAsync(10).Returns(filesJson);

        var result = await _provider.GetFilesAsync(10);

        Assert.NotNull(result.Summary);
        Assert.Equal(1, result.Summary.SourceFiles);
    }

    [Fact]
    public async Task GetFilesAsync_EmptyFilesList_ReturnsEmptyResult()
    {
        _apiClient.GetPullRequestFilesAsync(10).Returns("[]");

        var result = await _provider.GetFilesAsync(10);

        Assert.Empty(result.Files);
        Assert.Equal(0, result.Summary.SourceFiles);
        Assert.Equal(0, result.Summary.HighPriorityFiles);
    }

    [Fact]
    public async Task GetFilesAsync_LineCountsFlowThrough()
    {
        const string filesJson = """
            [
                { "filename": "src/A.cs", "status": "modified", "additions": 15, "deletions": 8 }
            ]
            """;

        _apiClient.GetPullRequestFilesAsync(1).Returns(filesJson);

        var result = await _provider.GetFilesAsync(1);

        var file = Assert.Single(result.Files);
        Assert.Equal(15, file.Additions);
        Assert.Equal(8, file.Deletions);
        Assert.Equal(23, file.Changes);
    }

    [Fact]
    public async Task GetFilesAsync_MissingLineCountsDefaultToZero()
    {
        const string filesJson = """
            [
                { "filename": "src/B.cs", "status": "added" }
            ]
            """;

        _apiClient.GetPullRequestFilesAsync(2).Returns(filesJson);

        var result = await _provider.GetFilesAsync(2);

        var file = Assert.Single(result.Files);
        Assert.Equal(0, file.Additions);
        Assert.Equal(0, file.Deletions);
        Assert.Equal(0, file.Changes);
    }

    [Fact]
    public async Task GetFilesAsync_BuildsSummaryCorrectly()
    {
        const string filesJson = """
            [
                { "filename": "src/App.cs", "status": "modified", "additions": 2 },
                { "filename": "tests/AppTests.cs", "status": "added", "additions": 1 },
                { "filename": "appsettings.json", "status": "modified" },
                { "filename": "docs/readme.md", "status": "modified" },
                { "filename": "lib/tool.dll", "status": "added" },
                { "filename": "GlobalUsings.g.cs", "status": "modified" }
            ]
            """;

        _apiClient.GetPullRequestFilesAsync(10).Returns(filesJson);

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
}
