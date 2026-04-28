using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using REBUSS.Pure.Core.Models;
using REBUSS.Pure.Core.Models.CopilotReview;
using REBUSS.Pure.Core.Models.Pagination;
using REBUSS.Pure.Core.Services.CopilotReview;
using REBUSS.Pure.Properties;
using System.Text;

namespace REBUSS.Pure.Services.CopilotReview;

/// <summary>
/// Executes the per-page Copilot review pass for an <see cref="AgentReviewJob"/>:
/// builds each page's enriched-content payload, dispatches batches of pages bounded
/// by <see cref="CopilotReviewOptions.MaxConcurrentPages"/>, applies a 3-attempt
/// retry per page, and atomically increments
/// <see cref="AgentReviewJob.CompletedPages"/> as each page finishes (success or
/// final failure). The batched fan-out exists because the GitHub Copilot backend
/// rate-limits larger fan-outs and silently re-queues the overflow — capping the
/// in-flight count at <c>MaxConcurrentPages</c> avoids that double wall-clock penalty.
/// Cancellation propagates as <see cref="OperationCanceledException"/> without
/// incrementing the counter; the orchestrator-level catch handles that state.
/// </summary>
internal sealed class PageReviewExecutor
{
    /// <summary>
    /// Bounded retry count per page (Clarification Q1 / research.md Decision 3).
    /// No backoff — retries fire immediately. On exhaustion, returns a
    /// <see cref="AgentPageReviewResult.Failure(int, IReadOnlyList{string}, string, int)"/>
    /// carrying the file paths that were on this page so the IDE agent can surface
    /// them for manual follow-up.
    /// </summary>
    private const int MaxAttemptsPerPage = 3;

    private readonly IAgentPageReviewer _pageReviewer;
    private readonly IOptions<CopilotReviewOptions> _options;
    private readonly ILogger<PageReviewExecutor> _logger;

    public PageReviewExecutor(
        IAgentPageReviewer pageReviewer,
        IOptions<CopilotReviewOptions> options,
        ILogger<PageReviewExecutor> logger)
    {
        _pageReviewer = pageReviewer;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Reviews every page of <paramref name="allocation"/> in batches of
    /// <see cref="CopilotReviewOptions.MaxConcurrentPages"/> (clamped to ≥ 1). Each
    /// batch fans out via <see cref="Task.Run(Func{Task})"/>, awaited as a unit
    /// before the next batch starts; the <c>CopilotRequestThrottle</c> serializes
    /// outgoing SDK calls within the batch. Returns a slot-aligned array — slot
    /// <c>i</c> holds the result for the page at <c>allocation.Pages[i]</c>; each
    /// slot is written by exactly one task (no contention, no lock needed).
    /// </summary>
    public async Task<AgentPageReviewResult[]> ExecuteAllAsync(
        AgentReviewJob job,
        PageAllocation allocation,
        IEnrichmentResult enrichment,
        CancellationToken ct)
    {
        var pageResults = new AgentPageReviewResult[allocation.TotalPages];
        var batchSize = Math.Max(1, _options.Value.MaxConcurrentPages);

        for (var batchStart = 0; batchStart < allocation.TotalPages; batchStart += batchSize)
        {
            var batchEnd = Math.Min(batchStart + batchSize, allocation.TotalPages);
            var batchTasks = new Task[batchEnd - batchStart];
            for (var pageIdx = batchStart; pageIdx < batchEnd; pageIdx++)
            {
                var idx = pageIdx;
                var pageSlice = allocation.Pages[idx];
                var pageNumber = pageSlice.PageNumber;
                var (enrichedContent, filePaths) = BuildPageInput(pageSlice, enrichment);
                batchTasks[idx - batchStart] = Task.Run(async () =>
                {
                    pageResults[idx] = await ReviewPageAndTrackAsync(
                        job, pageNumber, enrichedContent, filePaths, ct);
                }, ct);
            }
            await Task.WhenAll(batchTasks);
        }

        return pageResults;
    }

    /// <summary>
    /// Wraps <see cref="ReviewPageWithRetryAsync"/> and atomically increments the job's
    /// completed-page counter when the page finishes (whether it succeeded or failed after
    /// exhausting retries). Cancellation (<see cref="OperationCanceledException"/>) is
    /// re-thrown without incrementing — the orchestrator-level catch handles that state.
    /// </summary>
    private async Task<AgentPageReviewResult> ReviewPageAndTrackAsync(
        AgentReviewJob job,
        int pageNumber,
        string enrichedContent,
        IReadOnlyList<string> filePathsOnPage,
        CancellationToken ct)
    {
        var result = await ReviewPageWithRetryAsync(job, pageNumber, enrichedContent, filePathsOnPage, ct);
        Interlocked.Increment(ref job.CompletedPages);
        return result;
    }

    private async Task<AgentPageReviewResult> ReviewPageWithRetryAsync(
        AgentReviewJob job,
        int pageNumber,
        string enrichedContent,
        IReadOnlyList<string> filePathsOnPage,
        CancellationToken ct)
    {
        string lastError = "no attempts made";
        for (var attempt = 1; attempt <= MaxAttemptsPerPage; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            _logger.LogInformation(Resources.LogCopilotReviewPageStarted, job.ReviewKey, pageNumber, attempt);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Per-page activity is intentionally not published: pages run in parallel and
            // each concurrent task would overwrite CurrentActivity with its own "waiting"
            // message, making the progress feed flip-flop between pages. The monotonic
            // CompletedPages counter (emitted as "X/Y pages complete" by the waiter) is the
            // sole page-level progress signal. Per-attempt retries are visible in the log.
            AgentPageReviewResult result;
            try
            {
                result = await _pageReviewer.ReviewPageAsync(job.ReviewKey, pageNumber, enrichedContent, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Contract says the reviewer never throws; defence-in-depth: treat as failed attempt.
                result = AgentPageReviewResult.Failure(
                    pageNumber, Array.Empty<string>(), ex.Message, attemptsMade: attempt);
            }

            sw.Stop();

            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.ReviewText))
            {
                _logger.LogInformation(
                    Resources.LogCopilotReviewPageCompleted,
                    job.ReviewKey, pageNumber, attempt, sw.ElapsedMilliseconds);
                // Re-wrap so AttemptsMade reflects the retry that succeeded.
                return AgentPageReviewResult.Success(pageNumber, result.ReviewText!, attempt);
            }

            lastError = result.ErrorMessage ?? "empty response";
            _logger.LogInformation(
                Resources.LogCopilotReviewPageFailed,
                job.ReviewKey, pageNumber, attempt, lastError);
        }

        // All attempts exhausted — fill in the file paths (the executor is the only
        // component that knows which files were on this page) and return the failure.
        return AgentPageReviewResult.Failure(
            pageNumber, filePathsOnPage, lastError, attemptsMade: MaxAttemptsPerPage);
    }

    private static (string EnrichedContent, IReadOnlyList<string> FilePaths) BuildPageInput(
        PageSlice pageSlice, IEnrichmentResult enrichment)
    {
        var sb = new StringBuilder();
        var paths = new List<string>(pageSlice.Items.Count);
        foreach (var item in pageSlice.Items)
        {
            var path = enrichment.SortedCandidates[item.OriginalIndex].Path;
            paths.Add(path);
            if (enrichment.EnrichedByPath.TryGetValue(path, out var enrichedText))
            {
                sb.Append(enrichedText);
                sb.AppendLine();
            }
        }
        return (sb.ToString(), paths);
    }
}
