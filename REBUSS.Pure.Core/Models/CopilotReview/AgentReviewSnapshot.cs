namespace REBUSS.Pure.Core.Models.CopilotReview;

/// <summary>
/// Read-only snapshot of a single Copilot review job's state, returned by
/// <see cref="Services.CopilotReview.IAgentReviewOrchestrator.TryGetSnapshot"/>.
/// Mirrors the <c>PrEnrichmentJobSnapshot</c> pattern.
/// </summary>
public sealed record AgentReviewSnapshot
{
    public required string ReviewKey { get; init; }
    public required AgentReviewStatus Status { get; init; }
    public AgentReviewResult? Result { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Total number of pages allocated for this review (set once the background body
    /// computes the allocation). Zero while the allocation is still pending.
    /// </summary>
    public int TotalPages { get; init; }

    /// <summary>
    /// Number of pages that have completed review (success or failure). Incremented
    /// atomically as each parallel page task finishes.
    /// </summary>
    public int CompletedPages { get; init; }

    /// <summary>
    /// Short human-readable description of the current activity (e.g. "Page 2/5: sending prompt").
    /// Updated at key points by the orchestrator so the polling loop can report intermediate
    /// progress between page completions.
    /// </summary>
    public string? CurrentActivity { get; init; }
}
