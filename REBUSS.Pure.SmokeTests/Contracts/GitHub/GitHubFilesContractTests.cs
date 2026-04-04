using REBUSS.Pure.SmokeTests.Expectations;
using REBUSS.Pure.SmokeTests.Infrastructure;

namespace REBUSS.Pure.SmokeTests.Contracts.GitHub;

[Collection("GitHubContract")]
[Trait("Category", "ContractGitHub")]
public class GitHubFilesContractTests
{
    private readonly GitHubMcpProcessFixture _fixture;

    public GitHubFilesContractTests(GitHubMcpProcessFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Files_ReturnsTotalFileCount()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        Assert.Contains($"({GitHubTestExpectations.TotalFiles} file(s))", content);
    }

    [SkippableFact]
    public async Task Files_ReturnsExpectedPaths()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        foreach (var expected in GitHubTestExpectations.FilePaths)
        {
            Assert.Contains(expected, content);
        }
    }

    [SkippableFact]
    public async Task Files_ReturnsCorrectStatuses()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        foreach (var (path, expectedStatus) in GitHubTestExpectations.FileStatuses)
        {
            Assert.Contains(path, content);
            Assert.Contains(expectedStatus, content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [SkippableFact]
    public async Task Files_ReturnsAdditionsDeletions()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        Assert.Contains("+", content);
        Assert.Contains("-", content);
    }

    [SkippableFact]
    public async Task Files_ReturnsExtension()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        Assert.Contains(".cs", content, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Files_ClassifiesAsSourceFiles()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        Assert.Contains("Summary:", content);
        Assert.Contains($"{GitHubTestExpectations.TotalFiles} source", content);
    }

    [SkippableFact]
    public async Task Files_ReviewPriority()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        Assert.True(
            content.Contains(" low", StringComparison.OrdinalIgnoreCase) ||
            content.Contains(" medium", StringComparison.OrdinalIgnoreCase) ||
            content.Contains(" high", StringComparison.OrdinalIgnoreCase),
            "Expected at least one review-priority marker in output.");
    }

    [SkippableFact]
    public async Task Files_NoBinaryOrGenerated()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        Assert.DoesNotContain("binary", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("generated", content, StringComparison.OrdinalIgnoreCase);
    }
}
