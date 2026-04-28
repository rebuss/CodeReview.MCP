using Microsoft.Extensions.Options;
using REBUSS.Pure.Core.Models.CopilotReview;
using REBUSS.Pure.Core.Services.CopilotReview;
using REBUSS.Pure.Services.CopilotReview;

namespace REBUSS.Pure.Tests.Services.CopilotReview;

/// <summary>
/// Focused unit tests for the registry collaborator extracted from
/// <see cref="AgentReviewOrchestrator"/>. The orchestrator-level integration
/// behaviour (background body, cancellation, dispose drain) stays covered by
/// <see cref="AgentReviewOrchestratorTests"/>.
/// </summary>
public class AgentReviewJobRegistryTests
{
    private static AgentReviewJobRegistry NewRegistry(int retentionMinutes = 30)
        => new(Options.Create(new CopilotReviewOptions { JobRetentionMinutes = retentionMinutes }));

    [Fact]
    public void TryRegister_FreshKey_ReturnsNewJob_AndStoresIt()
    {
        var registry = NewRegistry();

        var job = registry.TryRegister("pr:1");

        Assert.NotNull(job);
        Assert.Equal("pr:1", job!.ReviewKey);
        Assert.Equal(AgentReviewStatus.Reviewing, job.Status);
        Assert.True(registry.TryGet("pr:1", out var stored));
        Assert.Same(job, stored);
    }

    [Fact]
    public void TryRegister_SameKey_SecondCall_ReturnsNull_Idempotent()
    {
        var registry = NewRegistry();
        var first = registry.TryRegister("pr:1");

        var second = registry.TryRegister("pr:1");

        Assert.NotNull(first);
        Assert.Null(second);
        // Original job still in place.
        Assert.True(registry.TryGet("pr:1", out var stored));
        Assert.Same(first, stored);
    }

    [Fact]
    public void TryRegister_AfterRetentionExpired_EvictsTerminalJob_AndStartsNewOne()
    {
        var registry = NewRegistry(retentionMinutes: 1);
        var first = registry.TryRegister("pr:1")!;
        // Simulate terminal completion 10 minutes ago — TTL sweep should evict it on next register.
        registry.CompleteUnderLock(first, AgentReviewStatus.Ready, result: null, errorMessage: null);
        first.CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        var second = registry.TryRegister("pr:1");

        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void TryRegister_RetentionDisabled_NeverEvicts_StaysIdempotent()
    {
        // retention <= 0 disables the sweep entirely.
        var registry = NewRegistry(retentionMinutes: 0);
        var first = registry.TryRegister("pr:1")!;
        registry.CompleteUnderLock(first, AgentReviewStatus.Ready, result: null, errorMessage: null);
        first.CompletedAt = DateTimeOffset.UtcNow.AddDays(-30);

        var second = registry.TryRegister("pr:1");

        // Even with a 30-day-old CompletedAt, the existing job is preserved → idempotent skip.
        Assert.Null(second);
    }

    [Fact]
    public void Snapshot_AfterCompleteUnderLock_ReflectsNewStatus_AndIncludesProgressFields()
    {
        var registry = NewRegistry();
        var job = registry.TryRegister("pr:1")!;
        registry.SetTotalPagesUnderLock(job, totalPages: 4);
        Interlocked.Increment(ref job.CompletedPages);
        Interlocked.Increment(ref job.CompletedPages);
        job.CurrentActivity = "two of four";

        var result = new AgentReviewResult
        {
            ReviewKey = "pr:1",
            PageReviews = Array.Empty<AgentPageReviewResult>(),
            CompletedAt = DateTimeOffset.UtcNow,
        };
        registry.CompleteUnderLock(job, AgentReviewStatus.Ready, result, errorMessage: null);

        var snapshot = registry.Snapshot("pr:1");

        Assert.NotNull(snapshot);
        Assert.Equal(AgentReviewStatus.Ready, snapshot!.Status);
        Assert.Same(result, snapshot.Result);
        Assert.Null(snapshot.ErrorMessage);
        Assert.Equal(4, snapshot.TotalPages);
        Assert.Equal(2, snapshot.CompletedPages);
        Assert.Equal("two of four", snapshot.CurrentActivity);
    }

    [Fact]
    public void Snapshot_UnknownKey_ReturnsNull()
    {
        var registry = NewRegistry();

        var snapshot = registry.Snapshot("does-not-exist");

        Assert.Null(snapshot);
    }

    [Fact]
    public void All_ReturnsLiveJobs_AfterMultipleRegistrations()
    {
        var registry = NewRegistry();
        registry.TryRegister("pr:1");
        registry.TryRegister("pr:2");
        registry.TryRegister("pr:3");

        Assert.Equal(3, registry.All.Count);
    }
}
