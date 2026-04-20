using REBUSS.Pure.Core.Models.CopilotReview;

namespace REBUSS.Pure.Core.Services.CopilotReview;

/// <summary>
/// Reviews a single page of enriched PR content through the configured
/// <see cref="AgentInvocation.IAgentInvoker"/> (Copilot SDK or Claude CLI) and returns
/// the outcome. Implementations MUST NEVER throw — failures are returned as
/// <see cref="AgentPageReviewResult.Failure"/> so the orchestrator's retry loop
/// (research.md Decision 3) can count attempts uniformly regardless of whether the
/// underlying agent call succeeded, returned empty, or threw.
/// </summary>
public interface IAgentPageReviewer
{
    /// <summary>
    /// Sends <paramref name="enrichedPageContent"/> plus the embedded review instruction
    /// template to the agent and collects the response. On success, returns
    /// <see cref="AgentPageReviewResult.Success"/>; on any failure (empty response,
    /// session error, thrown exception), returns
    /// <see cref="AgentPageReviewResult.Failure"/> with an empty
    /// <c>FailedFilePaths</c> list — the orchestrator fills in the file paths because
    /// only the orchestrator knows which files were on the page.
    /// </summary>
    /// <param name="reviewKey">
    /// Opaque review identifier from the orchestrator (e.g., <c>pr:42</c>, <c>local:staged:/repo</c>).
    /// Used by the optional inspection writer (feature 022) to group captured prompts and
    /// responses under a per-PR subdirectory. No other downstream consumer uses this value.
    /// </param>
    Task<AgentPageReviewResult> ReviewPageAsync(
        string reviewKey,
        int pageNumber,
        string enrichedPageContent,
        CancellationToken ct = default);
}
