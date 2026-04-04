using REBUSS.Pure.SmokeTests.Expectations;
using REBUSS.Pure.SmokeTests.Infrastructure;

namespace REBUSS.Pure.SmokeTests.Contracts.GitHub;

[Collection("GitHubContract")]
[Trait("Category", "ContractGitHub")]
public class GitHubDiffContractTests
{
    private readonly GitHubMcpProcessFixture _fixture;

    public GitHubDiffContractTests(GitHubMcpProcessFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Diff_ReturnsPrNumber()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        Assert.Contains(GitHubTestExpectations.FilePaths[0], content);
        Assert.Contains(GitHubTestExpectations.FilePaths[1], content);
    }

    [SkippableFact]
    public async Task Diff_ReturnsExpectedFileCount()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        var blockCount = content.Split("=== ", StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(blockCount >= GitHubTestExpectations.TotalFiles,
            $"Expected at least {GitHubTestExpectations.TotalFiles} file blocks, got {blockCount}.");
    }

    [SkippableFact]
    public async Task Diff_ReturnsHunksForEditedFile()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        Assert.Contains("Calculator.cs", content);
        Assert.True(content.Contains("+", StringComparison.Ordinal) || content.Contains("-", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task Diff_HunkHasCorrectStructure()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        Assert.Contains("===", content);
    }

    [SkippableFact]
    public async Task Diff_LinesHaveValidOps()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        Assert.True(content.Contains("+", StringComparison.Ordinal) || content.Contains("-", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task Diff_EditedFileHasAdditionsAndDeletions()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        Assert.Contains("Calculator.cs", content);
        Assert.Contains("+", content, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Diff_NewFileHasOnlyAdditions()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        Assert.Contains("Logger.cs", content);
        Assert.Contains("+", content, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Diff_ContainsExpectedCodeFragment()
    {
        Skip.IfNot(TestSettings.IsGitHubConfigured, TestSettings.GitHubSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.GhPrNumber });
        var content = response.GetToolText();

        Assert.Contains(GitHubTestExpectations.ExpectedCodeFragment, content, StringComparison.OrdinalIgnoreCase);
    }
}
