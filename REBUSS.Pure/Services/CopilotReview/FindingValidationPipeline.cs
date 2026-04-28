using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using REBUSS.Pure.Core.Models.CopilotReview;
using REBUSS.Pure.Services.CopilotReview.Validation;

namespace REBUSS.Pure.Services.CopilotReview;

/// <summary>
/// Feature 021 — consolidated post-review validation across all successfully reviewed
/// pages. Parses findings from every succeeded page, resolves enclosing scopes,
/// validates them via a single Copilot pass (internally batched by
/// <see cref="FindingValidator"/>), and rebuilds each page's
/// <see cref="AgentPageReviewResult.ReviewText"/> with false-positives removed and
/// uncertain findings tagged. Mutates <c>pageResults[idx]</c> in place. The pipeline
/// is opt-in: <see cref="IsEnabled"/> short-circuits the call when
/// <see cref="CopilotReviewOptions.ValidateFindings"/> is false or either of the
/// optional <see cref="FindingValidator"/> / <see cref="FindingScopeResolver"/>
/// dependencies is unavailable. Graceful degradation (FR-012) is the orchestrator's
/// responsibility — the pipeline propagates exceptions; the caller catches them and
/// logs a warning while preserving the original review results.
/// </summary>
internal sealed class FindingValidationPipeline
{
    private readonly IOptions<CopilotReviewOptions> _options;
    private readonly ILogger<FindingValidationPipeline> _logger;
    private readonly FindingValidator? _validator;
    private readonly FindingScopeResolver? _scopeResolver;

    public FindingValidationPipeline(
        IOptions<CopilotReviewOptions> options,
        ILogger<FindingValidationPipeline> logger,
        FindingValidator? validator = null,
        FindingScopeResolver? scopeResolver = null)
    {
        _options = options;
        _logger = logger;
        _validator = validator;
        _scopeResolver = scopeResolver;
    }

    /// <summary>
    /// True when the configured flag is on AND both optional dependencies were
    /// resolved by DI. The flag is read on every access (Principle V — deferred
    /// resolution) so a runtime config change takes effect without restart.
    /// </summary>
    public bool IsEnabled =>
        _options.Value.ValidateFindings && _validator is not null && _scopeResolver is not null;

    /// <summary>
    /// Runs the 4-phase pipeline (parse → resolve scopes → validate → map back) over
    /// <paramref name="pageResults"/>. Mutates the array in place: each page's
    /// <c>ReviewText</c> is rebuilt with false-positives filtered out. No-op when
    /// <see cref="IsEnabled"/> is false, when no findings parsed, or when the parsed
    /// total exceeds <see cref="CopilotReviewOptions.MaxValidatableFindings"/> (in
    /// which case a Warning log surfaces the bypass).
    /// </summary>
    public async Task RunAsync(AgentReviewJob job, AgentPageReviewResult[] pageResults, CancellationToken ct)
    {
        if (!IsEnabled)
            return;

        // Phase 1: parse findings from all successful pages, tracking page origin.
        var pageData = new List<(int PageIndex, IReadOnlyList<ParsedFinding> Findings, string Remainder)>();
        var allFindings = new List<ParsedFinding>();

        for (var i = 0; i < pageResults.Length; i++)
        {
            var page = pageResults[i];
            if (!page.Succeeded || string.IsNullOrWhiteSpace(page.ReviewText))
                continue;

            var (findings, remainder) = FindingParser.Parse(page.ReviewText!);
            if (findings.Count == 0)
                continue;

            pageData.Add((i, findings, remainder));
            allFindings.AddRange(findings);
        }

        if (allFindings.Count == 0)
            return;

        // Over-threshold → skip validation entirely (FR-015). Likely a systemic issue;
        // individual finding validation isn't the right tool. Logged at Warning because
        // this is graceful degradation with user-visible impact — the review bypasses
        // false-positive filtering for the entire PR — and operators should see it.
        var maxValidatable = _options.Value.MaxValidatableFindings;
        if (maxValidatable > 0 && allFindings.Count > maxValidatable)
        {
            _logger.LogWarning(
                "Skipping validation for '{ReviewKey}': {Count} total findings exceed MaxValidatableFindings={Max}",
                job.ReviewKey, allFindings.Count, maxValidatable);
            return;
        }

        // Phase 2: resolve enclosing scopes for all findings at once.
        job.CurrentActivity = $"Resolving {allFindings.Count} scopes for validation";
        var withScopes = await _scopeResolver!.ResolveAsync(
            allFindings, job.ReviewKey, _options.Value.MaxScopeLines, ct).ConfigureAwait(false);

        // Phase 3: validate all findings (internal pagination by token budget).
        // The pageProgress callback surfaces intra-validation progress so the IDE
        // notification advances per validation page rather than sitting on a single message.
        job.CurrentActivity = $"Validating {allFindings.Count} findings";
        var validated = await _validator!.ValidateAsync(
            withScopes, job.ReviewKey, ct,
            pageProgress: (pageNumber, totalPages) =>
            {
                job.CurrentActivity = totalPages > 1
                    ? $"Validating findings: page {pageNumber}/{totalPages}"
                    : $"Validating {allFindings.Count} findings";
            }).ConfigureAwait(false);

        LogValidationSummary(job.ReviewKey, withScopes, validated);

        // Phase 4: map validated findings back to their originating pages and rebuild ReviewText.
        var offset = 0;
        foreach (var (pageIndex, findings, remainder) in pageData)
        {
            var pageValidated = new List<ValidatedFinding>(findings.Count);
            for (var j = 0; j < findings.Count; j++)
                pageValidated.Add(validated[offset + j]);
            offset += findings.Count;

            var filteredText = FindingFilterer.Apply(remainder, pageValidated);
            pageResults[pageIndex] = AgentPageReviewResult.Success(
                pageResults[pageIndex].PageNumber, filteredText,
                attemptsMade: pageResults[pageIndex].AttemptsMade);
        }
    }

    /// <summary>
    /// Emits a single Information-level summary of the validation pipeline so silent
    /// drops (e.g. archive at the wrong commit producing SourceUnavailable for every
    /// added file) are visible in the log instead of requiring inspection-file archaeology.
    /// </summary>
    private void LogValidationSummary(
        string reviewKey,
        IReadOnlyList<FindingWithScope> withScopes,
        IReadOnlyList<ValidatedFinding> validated)
    {
        int notCSharp = 0, sourceUnavailable = 0, scopeNotFound = 0, resolved = 0;
        foreach (var w in withScopes)
        {
            switch (w.ResolutionFailure)
            {
                case ScopeResolutionFailure.NotCSharp: notCSharp++; break;
                case ScopeResolutionFailure.SourceUnavailable: sourceUnavailable++; break;
                case ScopeResolutionFailure.ScopeNotFound: scopeNotFound++; break;
                case ScopeResolutionFailure.None: resolved++; break;
            }
        }

        int valid = 0, falsePositive = 0, uncertain = 0;
        foreach (var v in validated)
        {
            switch (v.Verdict)
            {
                case FindingVerdict.Valid: valid++; break;
                case FindingVerdict.FalsePositive: falsePositive++; break;
                case FindingVerdict.Uncertain: uncertain++; break;
            }
        }

        _logger.LogInformation(
            "Finding validation summary for '{ReviewKey}': parsed={Parsed} " +
            "(scope: not-csharp={NotCs}, source-unavailable={SrcUnavail}, scope-not-found={ScopeMiss}, resolved={Resolved}) " +
            "→ verdicts: valid={Valid}, false-positive={FalsePositive}, uncertain={Uncertain}",
            reviewKey, validated.Count,
            notCSharp, sourceUnavailable, scopeNotFound, resolved,
            valid, falsePositive, uncertain);
    }
}
