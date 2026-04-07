using REBUSS.Pure.Services.ContextWindow;

namespace REBUSS.Pure.Tests.Services.ContextWindow;

public class HostGatewayDetectorTests
{
    [Theory]
    [InlineData("claude-code", 25_000)]
    [InlineData("Claude Code", 25_000)]
    [InlineData("CLAUDE-CODE", 25_000)]
    [InlineData("claudecode", 25_000)]
    [InlineData("claude-ai", 25_000)]
    [InlineData("claude.ai", 25_000)]
    [InlineData("cursor", 24_000)]
    [InlineData("Cursor IDE", 24_000)]
    [InlineData("CURSOR", 24_000)]
    [InlineData("codex", 20_000)]
    [InlineData("openai-codex", 20_000)]
    [InlineData("unknown-host", 20_000)]
    [InlineData("vscode", 20_000)]
    [InlineData("", 20_000)]
    [InlineData("   ", 20_000)]
    [InlineData(null, 20_000)]
    public void Detect_MapsClientNameToExpectedTokenCap(string? clientName, int expected)
    {
        Assert.Equal(expected, HostGatewayDetector.Detect(clientName));
    }

    [Fact]
    public void Detect_DefaultGatewayTokens_IsTwentyThousand()
    {
        // Sanity: the safe fallback used by GatewayBudgetState when clientInfo is missing.
        Assert.Equal(20_000, HostGatewayDetector.DefaultGatewayTokens);
    }
}
