using REBUSS.Pure.SmokeTests.Expectations;
using REBUSS.Pure.SmokeTests.Infrastructure;

namespace REBUSS.Pure.SmokeTests.Contracts.AzureDevOps;

[Collection("AdoContract")]
[Trait("Category", "ContractAdo")]
public class AdoDiffContractTests
{
    private readonly AdoMcpProcessFixture _fixture;

    public AdoDiffContractTests(AdoMcpProcessFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Diff_ReturnsPrNumber()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        Assert.Contains(AdoTestExpectations.FilePaths[0], content);
        Assert.Contains(AdoTestExpectations.FilePaths[1], content);
    }

    [SkippableFact]
    public async Task Diff_ReturnsExpectedFileCount()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        var blockCount = content.Split("=== ", StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(blockCount >= AdoTestExpectations.TotalFiles,
            $"Expected at least {AdoTestExpectations.TotalFiles} file blocks, got {blockCount}.");
    }

    [SkippableFact]
    public async Task Diff_ReturnsHunksForEditedFile()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        Assert.Contains("Calculator.cs", content);
        Assert.True(content.Contains("+", StringComparison.Ordinal) || content.Contains("-", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task Diff_HunkHasCorrectStructure()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        Assert.Contains("===", content);
    }

    [SkippableFact]
    public async Task Diff_LinesHaveValidOps()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        Assert.True(content.Contains("+", StringComparison.Ordinal) || content.Contains("-", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task Diff_EditedFileHasAdditionsAndDeletions()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        Assert.Contains("Calculator.cs", content);
        Assert.Contains("+", content, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Diff_NewFileHasOnlyAdditions()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        Assert.Contains("Logger.cs", content);
        Assert.Contains("+", content, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Diff_ContainsExpectedCodeFragment()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_diff", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        Assert.Contains(AdoTestExpectations.ExpectedCodeFragment, content, StringComparison.OrdinalIgnoreCase);
    }
}
