using REBUSS.Pure.SmokeTests.Expectations;
using REBUSS.Pure.SmokeTests.Infrastructure;

namespace REBUSS.Pure.SmokeTests.Contracts.AzureDevOps;

[Collection("AdoContract")]
[Trait("Category", "ContractAdo")]
public class AdoFilesContractTests
{
    private readonly AdoMcpProcessFixture _fixture;

    public AdoFilesContractTests(AdoMcpProcessFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Files_ReturnsTotalFileCount()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        Assert.Contains($"({AdoTestExpectations.TotalFiles} file(s))", content);
    }

    [SkippableFact]
    public async Task Files_ReturnsExpectedPaths()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        foreach (var expected in AdoTestExpectations.FilePaths)
        {
            Assert.Contains(expected, content);
        }
    }

    [SkippableFact]
    public async Task Files_ReturnsCorrectStatuses()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        foreach (var (path, expectedStatus) in AdoTestExpectations.FileStatuses)
        {
            Assert.Contains(path, content);
            Assert.Contains(expectedStatus, content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [SkippableFact]
    public async Task Files_ReturnsAdditionsDeletions()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        Assert.Contains("+", content);
        Assert.Contains("-", content);
    }

    [SkippableFact]
    public async Task Files_ReturnsExtension()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        Assert.Contains(".cs", content, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Files_ClassifiesAsSourceFiles()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        Assert.Contains("Summary:", content);
        Assert.Contains($"{AdoTestExpectations.TotalFiles} source", content);
    }

    [SkippableFact]
    public async Task Files_ReviewPriority()
    {
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.AdoPrNumber });
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
        Skip.IfNot(TestSettings.IsAdoConfigured, TestSettings.AdoSkipReason);

        var response = await _fixture.Server.SendToolCallAsync(
            "get_pr_files", new { prNumber = TestSettings.AdoPrNumber });
        var content = response.GetToolText();

        Assert.DoesNotContain("binary", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("generated", content, StringComparison.OrdinalIgnoreCase);
    }
}
