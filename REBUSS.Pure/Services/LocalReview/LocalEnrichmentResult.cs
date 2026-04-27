using REBUSS.Pure.Core.Models;
using REBUSS.Pure.Core.Models.Pagination;
using REBUSS.Pure.Core.Models.ResponsePacking;

namespace REBUSS.Pure.Services.LocalReview;

/// <summary>
/// Successful payload of a completed local enrichment job. Implements
/// <see cref="IEnrichmentResult"/> so the Copilot review orchestrator
/// can consume it identically to <c>PrEnrichmentResult</c>.
/// </summary>
public sealed record LocalEnrichmentResult : IEnrichmentResult
{
    public required string RepositoryRoot { get; init; }
    public required string CurrentBranch { get; init; }
    public required string Scope { get; init; }

    /// <inheritdoc />
    public required IReadOnlyList<PackingCandidate> SortedCandidates { get; init; }

    /// <inheritdoc />
    public required IReadOnlyDictionary<string, string> EnrichedByPath { get; init; }

    public required PageAllocation Allocation { get; init; }
    public required int SafeBudgetTokens { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Number of changed files reported by <c>git diff --name-status</c>. Used by the
    /// content tool handler's contradiction guard: when this is &gt; 0 but
    /// <see cref="RawFileChangesFromDiff"/> is 0, git itself gave us an empty diff for
    /// files it just enumerated as modified — a transient git failure that must surface
    /// as an error rather than as a successful "no changes" review.
    /// </summary>
    public required int RawChangedFileCount { get; init; }

    /// <summary>
    /// Number of files parsed from the <c>git diff -p</c> output before classifier-based
    /// skip filtering. See <see cref="RawChangedFileCount"/> for the contradiction-guard
    /// rationale. Note: this can legitimately exceed <see cref="SortedCandidates"/> length
    /// when files are dropped as binary/generated/full-rewrite.
    /// </summary>
    public required int RawFileChangesFromDiff { get; init; }
}
