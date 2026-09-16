using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using REBUSS.Pure.Core.Services.CopilotReview;
using REBUSS.Pure.Services.CopilotReview;

namespace REBUSS.Pure.Tests.Services.CopilotReview;

/// <summary>
/// Unit tests for <see cref="ClaudeCliAvailabilityDetector"/> — the <c>--agent claude</c>
/// availability gate: available unless disabled by configuration, never throws in strict mode.
/// </summary>
public class ClaudeCliAvailabilityDetectorTests
{
    private static ClaudeCliAvailabilityDetector Create(bool enabled = true, bool strictMode = false) =>
        new(Options.Create(new CopilotReviewOptions { Enabled = enabled, StrictMode = strictMode, Model = "claude-sonnet-4-6" }),
            NullLogger<ClaudeCliAvailabilityDetector>.Instance);

    [Fact]
    public async Task Enabled_ReturnsAvailableOkVerdictWithConfiguredModel()
    {
        var detector = Create();

        var verdict = await detector.GetVerdictAsync();

        Assert.True(await detector.IsAvailableAsync());
        Assert.True(verdict.IsAvailable);
        Assert.Equal(CopilotAuthReason.Ok, verdict.Reason);
        Assert.Equal("claude-sonnet-4-6", verdict.ConfiguredModel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledByConfig_ReturnsFalseAndNeverThrows(bool strictMode)
    {
        var detector = Create(enabled: false, strictMode: strictMode);

        Assert.False(await detector.IsAvailableAsync());
        Assert.Equal(CopilotAuthReason.DisabledByConfig, (await detector.GetVerdictAsync()).Reason);
    }

    [Fact]
    public async Task CancelledToken_Throws()
    {
        var detector = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => detector.IsAvailableAsync(cts.Token));
    }
}
