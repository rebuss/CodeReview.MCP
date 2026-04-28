using REBUSS.Pure.Core.Models.CopilotReview;

namespace REBUSS.Pure.Services.CopilotReview;

/// <summary>
/// Internal per-review job state — mutable but guarded by the surrounding lock for
/// terminal-state writes (<c>Status</c>, <c>Result</c>, <c>ErrorMessage</c>, <c>CompletedAt</c>,
/// <c>TotalPages</c>) and by <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>
/// for concurrent add/lookup. Per-page progress fields (<see cref="CompletedPages"/>,
/// <see cref="CurrentActivity"/>) use lock-free primitives (<c>Interlocked</c> / <c>volatile</c>)
/// because parallel page tasks update them without taking the lock.
/// Narrow exception to Principle VI (see plan.md Constitution Check VI).
/// </summary>
internal sealed class AgentReviewJob
{
    public required string ReviewKey { get; init; }
    public AgentReviewStatus Status { get; set; } = AgentReviewStatus.Reviewing;
    public AgentReviewResult? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public required TaskCompletionSource<AgentReviewResult> Completion { get; init; }

    /// <summary>
    /// Handle for the fire-and-forget background body. Set once inside the registry's
    /// creation lock; awaited on shutdown (<see cref="AgentReviewOrchestrator.DisposeAsync"/>)
    /// so the process does not terminate before graceful-cancellation finishes.
    /// </summary>
    public Task? BackgroundTask { get; set; }

    /// <summary>Set under the registry lock once the page allocation is computed.</summary>
    public int TotalPages { get; set; }

    /// <summary>Atomically incremented via <see cref="Interlocked.Increment(ref int)"/> as each page finishes.</summary>
    public int CompletedPages;

    /// <summary>Short status message updated at key points for progress reporting.</summary>
    public volatile string? CurrentActivity;

    /// <summary>
    /// Timestamp of when the job reached a terminal state (Ready / Failed). Used by the
    /// TTL sweep to evict stale entries. <c>null</c> while the job is still in progress.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
