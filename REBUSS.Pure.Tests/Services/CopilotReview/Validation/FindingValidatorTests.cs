using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using REBUSS.Pure.Core;
using REBUSS.Pure.Core.Models.Pagination;
using REBUSS.Pure.Core.Models.ResponsePacking;
using REBUSS.Pure.Services.CopilotReview;
using REBUSS.Pure.Services.CopilotReview.Inspection;
using REBUSS.Pure.Services.CopilotReview.Validation;
using REBUSS.Pure.Tests.Services.AgentInvocation;

namespace REBUSS.Pure.Tests.Services.CopilotReview.Validation;

/// <summary>
/// Unit tests for <see cref="FindingValidator"/> after the <c>IAgentInvoker</c>
/// refactor. The validator's prior event-driven SDK session collapses into a
/// single <c>InvokeAsync(prompt, model, ct)</c> call per page, so tests script
/// responses on a <see cref="FakeAgentInvoker"/> instead of emitting
/// <c>AssistantMessageEvent</c>/<c>SessionIdleEvent</c>/<c>SessionErrorEvent</c>.
/// Feature 021.
/// </summary>
public class FindingValidatorTests
{
    private static FindingValidator CreateValidator(
        FakeAgentInvoker invoker,
        IAgentInspectionWriter? inspection = null,
        IPageAllocator? pageAllocator = null,
        ITokenEstimator? tokenEstimator = null) =>
        new(invoker,
            Options.Create(new CopilotReviewOptions
            {
                Model = "claude-sonnet-4.6",
                ValidateFindings = true,
            }),
            inspection ?? Substitute.For<IAgentInspectionWriter>(),
            pageAllocator ?? SinglePageAllocator(),
            tokenEstimator ?? FixedTokenEstimator(estimatePerCall: 1),
            NullLogger<FindingValidator>.Instance);

    /// <summary>Allocator that puts every candidate on a single page (the common test case).</summary>
    private static IPageAllocator SinglePageAllocator()
    {
        var allocator = Substitute.For<IPageAllocator>();
        allocator.Allocate(Arg.Any<IReadOnlyList<PackingCandidate>>(), Arg.Any<int>())
            .Returns(ci =>
            {
                var candidates = ci.Arg<IReadOnlyList<PackingCandidate>>();
                if (candidates.Count == 0)
                    return new PageAllocation(Array.Empty<PageSlice>(), 0, 0);
                var items = Enumerable.Range(0, candidates.Count)
                    .Select(i => new PageSliceItem(i, PackingItemStatus.Included, candidates[i].EstimatedTokens))
                    .ToArray();
                return new PageAllocation(
                    new[] { new PageSlice(1, 0, candidates.Count, items, 0, 0) },
                    1,
                    candidates.Count);
            });
        return allocator;
    }

    /// <summary>Allocator that splits candidates into fixed-size pages — for multi-page tests.</summary>
    private static IPageAllocator FixedSizePageAllocator(int itemsPerPage)
    {
        var allocator = Substitute.For<IPageAllocator>();
        allocator.Allocate(Arg.Any<IReadOnlyList<PackingCandidate>>(), Arg.Any<int>())
            .Returns(ci =>
            {
                var candidates = ci.Arg<IReadOnlyList<PackingCandidate>>();
                var pages = new List<PageSlice>();
                if (candidates.Count == 0)
                    return new PageAllocation(pages, 0, 0);
                var pageNum = 1;
                for (var start = 0; start < candidates.Count; start += itemsPerPage)
                {
                    var end = Math.Min(start + itemsPerPage, candidates.Count);
                    var items = Enumerable.Range(start, end - start)
                        .Select(i => new PageSliceItem(i, PackingItemStatus.Included, candidates[i].EstimatedTokens))
                        .ToArray();
                    pages.Add(new PageSlice(pageNum++, start, end, items, 0, 0));
                }
                return new PageAllocation(pages, pages.Count, candidates.Count);
            });
        return allocator;
    }

    private static ITokenEstimator FixedTokenEstimator(int estimatePerCall)
    {
        var estimator = Substitute.For<ITokenEstimator>();
        estimator.EstimateTokenCount(Arg.Any<string>()).Returns(estimatePerCall);
        return estimator;
    }

    private static ParsedFinding MakeFinding(int index, string severity = "major") => new()
    {
        Index = index,
        FilePath = "src/A.cs",
        LineNumber = 10,
        Severity = severity,
        Description = $"issue {index}",
        OriginalText = $"**[{severity}]** `src/A.cs` (line 10): issue {index}",
    };

    private static FindingWithScope Resolved(ParsedFinding finding) => new()
    {
        Finding = finding,
        ScopeSource = "public void Bar() { }",
        ScopeName = "Foo.Bar()",
        ResolutionFailure = ScopeResolutionFailure.None,
    };

    private static FindingWithScope Unresolved(ParsedFinding finding, ScopeResolutionFailure failure) => new()
    {
        Finding = finding,
        ScopeSource = "",
        ScopeName = "",
        ResolutionFailure = failure,
    };

    [Fact]
    public async Task ValidateAsync_NotCSharp_MapsToValidWithoutAgentCall()
    {
        var invoker = new FakeAgentInvoker
        {
            OnInvoke = (_, _, _) => throw new Exception("must not call agent"),
        };

        var validator = CreateValidator(invoker);
        var input = new[] { Unresolved(MakeFinding(0), ScopeResolutionFailure.NotCSharp) };

        var result = await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        var v = Assert.Single(result);
        Assert.Equal(FindingVerdict.Valid, v.Verdict);
        Assert.Equal(0, invoker.InvocationCount);
    }

    [Fact]
    public async Task ValidateAsync_SourceUnavailable_MapsToUncertainWithoutAgentCall()
    {
        var invoker = new FakeAgentInvoker
        {
            OnInvoke = (_, _, _) => throw new Exception("must not call agent"),
        };

        var validator = CreateValidator(invoker);
        var input = new[] { Unresolved(MakeFinding(0), ScopeResolutionFailure.SourceUnavailable) };

        var result = await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        var v = Assert.Single(result);
        Assert.Equal(FindingVerdict.Uncertain, v.Verdict);
        Assert.Equal(0, invoker.InvocationCount);
    }

    // NOTE: Dead-path safeguard. `FindingScopeResolver` no longer emits
    // `ScopeResolutionFailure.ScopeNotFound` for `.cs` findings (the whole-file
    // fallback always yields `ResolutionFailure.None`, see Architecture.md §6b).
    // The enum value is kept because the validator's pre-filter must stay resilient
    // if any future producer reintroduces this state — unmapped failure values
    // would fall into the Copilot-bound branch and waste agent calls. This test
    // pins the defensive mapping in place.
    [Fact]
    public async Task ValidateAsync_ScopeNotFound_MapsToUncertainWithoutAgentCall()
    {
        var invoker = new FakeAgentInvoker
        {
            OnInvoke = (_, _, _) => throw new Exception("must not call agent"),
        };

        var validator = CreateValidator(invoker);
        var input = new[] { Unresolved(MakeFinding(0), ScopeResolutionFailure.ScopeNotFound) };

        var result = await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        var v = Assert.Single(result);
        Assert.Equal(FindingVerdict.Uncertain, v.Verdict);
        Assert.Equal(0, invoker.InvocationCount);
    }

    [Fact]
    public async Task ValidateAsync_ResolvedFindings_CallsAgentAndParsesVerdicts()
    {
        var invoker = new FakeAgentInvoker();
        invoker.Enqueue(
            "**Finding 1: VALID** — this is a real bug\n" +
            "**Finding 2: FALSE_POSITIVE** — misinterpreted context\n" +
            "**Finding 3: UNCERTAIN** — needs cross-file check");

        var validator = CreateValidator(invoker);
        var input = new[]
        {
            Resolved(MakeFinding(0)),
            Resolved(MakeFinding(1)),
            Resolved(MakeFinding(2)),
        };

        var result = await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal(FindingVerdict.Valid, result[0].Verdict);
        Assert.Equal(FindingVerdict.FalsePositive, result[1].Verdict);
        Assert.Equal(FindingVerdict.Uncertain, result[2].Verdict);
        Assert.Equal(1, invoker.InvocationCount); // single page → single agent call
    }

    [Fact]
    public async Task ValidateAsync_Result_PreservesInputOrderRegardlessOfInternalSeveritySort()
    {
        // Pin the load-bearing ordering contract: result[i] corresponds to input[i] even
        // though the validator internally severity-orders findings (major < critical) for
        // the agent prompt, and parses verdicts by the "Finding {n}:" index in the
        // response. A refactor that returns results in severity order (instead of input
        // order) would silently misalign AgentReviewOrchestrator's per-page slicing.
        var invoker = new FakeAgentInvoker();
        // Prompt order in severity: critical first, then major, then minor.
        // Findings at input index 0=minor, 1=critical, 2=major → prompt order 1,2,0.
        invoker.Enqueue(
            "**Finding 1: VALID** — critical bug\n" +          // input[1]
            "**Finding 2: UNCERTAIN** — major unclear\n" +     // input[2]
            "**Finding 3: FALSE_POSITIVE** — minor bogus");    // input[0]

        var validator = CreateValidator(invoker);
        var input = new[]
        {
            Resolved(MakeFinding(0, severity: "minor")),
            Resolved(MakeFinding(1, severity: "critical")),
            Resolved(MakeFinding(2, severity: "major")),
        };

        var result = await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        // Size matches input length.
        Assert.Equal(input.Length, result.Count);
        // No null slots.
        Assert.All(result, v => Assert.NotNull(v));
        // result[i] is the verdict for input[i] — NOT for prompt-position i.
        Assert.Equal(FindingVerdict.FalsePositive, result[0].Verdict); // minor
        Assert.Equal(FindingVerdict.Valid, result[1].Verdict);          // critical
        Assert.Equal(FindingVerdict.Uncertain, result[2].Verdict);      // major
        // Finding identity preserved.
        Assert.Equal(input[0].Finding.Index, result[0].Finding.Index);
        Assert.Equal(input[1].Finding.Index, result[1].Finding.Index);
        Assert.Equal(input[2].Finding.Index, result[2].Finding.Index);
    }

    [Fact]
    public async Task ValidateAsync_AgentReturnsMultiLineText_ParsesAllVerdicts()
    {
        // Pre-refactor this exercised phased-output AssistantMessageEvent accumulation.
        // Post-refactor the invoker returns the accumulated string, but the validator's
        // regex-based verdict parsing must still handle multi-line content correctly.
        var invoker = new FakeAgentInvoker();
        invoker.Enqueue(
            "**Finding 1: VALID** — part A\n" +
            "**Finding 2: FALSE_POSITIVE** — part B");

        var validator = CreateValidator(invoker);
        var input = new[] { Resolved(MakeFinding(0)), Resolved(MakeFinding(1)) };

        var result = await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(FindingVerdict.Valid, result[0].Verdict);
        Assert.Equal(FindingVerdict.FalsePositive, result[1].Verdict);
    }

    [Fact]
    public async Task ValidateAsync_TwelveFindings_AcrossThreePages_MakesThreeCalls()
    {
        // Each call gets a distinct per-page verdict and emits exactly as many
        // "Finding N:" entries as that page truly contains. Previously the mock
        // produced 10 VALID entries for every call — the over-count silently
        // flowed through ParseVerdicts' `n > pageBatch.Count` guard, so a bug that
        // routed page-2's response to page-3's findings (or vice versa) would not
        // have failed the test. Verdicts are now specific per call so per-finding
        // routing is verified.
        //
        // Routing assertion is bound to a stable per-finding marker in the prompt
        // (each finding's description renders as `**Issue:** issue {index}`, see
        // MakeFinding) rather than to call-order. If a future refactor of
        // FindingValidator ever dispatches pages concurrently the slot-to-page
        // mapping would otherwise become non-deterministic and silently flake;
        // here, page identity is derived from prompt content, not call sequence.
        // The discriminators (`issue 0`, `issue 5`, `issue 10`) are chosen so
        // none is a substring of another — `issue 10` does not contain `issue 0`
        // or `issue 5` and vice versa — so each marker matches exactly one page.
        var pageContracts = new (string Marker, string Verdict, int Size)[]
        {
            ("**Issue:** issue 0",  "VALID",          5), // page 1: findings 0..4
            ("**Issue:** issue 5",  "FALSE_POSITIVE", 5), // page 2: findings 5..9
            ("**Issue:** issue 10", "UNCERTAIN",      2), // page 3: findings 10..11
        };

        var invoker = new FakeAgentInvoker
        {
            OnInvoke = (prompt, _, _) =>
            {
                var contract = Array.Find(pageContracts, c => prompt.Contains(c.Marker));
                if (contract == default)
                    throw new InvalidOperationException(
                        "Fake invoker received a prompt with no recognized page marker.");
                var content = string.Join('\n',
                    Enumerable.Range(1, contract.Size).Select(
                        i => $"**Finding {i}: {contract.Verdict}** — {contract.Marker}"));
                return Task.FromResult(content);
            }
        };

        // Allocator splits into pages of 5 → 12 findings across 3 pages (5, 5, 2).
        var validator = CreateValidator(
            invoker,
            pageAllocator: FixedSizePageAllocator(itemsPerPage: 5));
        var input = Enumerable.Range(0, 12).Select(i => Resolved(MakeFinding(i))).ToArray();

        var result = await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        Assert.Equal(12, result.Count);
        Assert.Equal(3, invoker.InvocationCount);

        // Input indices 0..4 → page 1 (VALID), 5..9 → page 2 (FALSE_POSITIVE),
        // 10..11 → page 3 (UNCERTAIN). Stable-by-severity ordering preserves
        // input order because every finding has the same default "major" severity.
        for (var i = 0; i < 5; i++)
            Assert.Equal(FindingVerdict.Valid, result[i].Verdict);
        for (var i = 5; i < 10; i++)
            Assert.Equal(FindingVerdict.FalsePositive, result[i].Verdict);
        for (var i = 10; i < 12; i++)
            Assert.Equal(FindingVerdict.Uncertain, result[i].Verdict);
    }

    [Fact]
    public async Task ValidateAsync_AgentThrows_FindingsPassThroughAsValid()
    {
        // FR-012 graceful degradation: when the agent call fails, every finding on
        // that page must be kept as Valid with a reason.
        var invoker = new FakeAgentInvoker();
        invoker.EnqueueException(new InvalidOperationException("boom"));

        var validator = CreateValidator(invoker);
        var input = new[] { Resolved(MakeFinding(0)), Resolved(MakeFinding(1)) };

        var result = await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        // Graceful degradation: both findings kept as Valid.
        Assert.All(result, r => Assert.Equal(FindingVerdict.Valid, r.Verdict));
    }

    [Fact]
    public async Task ValidateAsync_EmptyInput_NoAgentCall()
    {
        var invoker = new FakeAgentInvoker();

        var validator = CreateValidator(invoker);
        var result = await validator.ValidateAsync(Array.Empty<FindingWithScope>(), "test:1", CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(0, invoker.InvocationCount);
    }

    [Fact]
    public async Task ValidateAsync_MixedResolvedAndUnresolved_OnlyResolvedSentToAgent()
    {
        int? capturedFindingsInPrompt = null;
        var invoker = new FakeAgentInvoker
        {
            OnInvoke = (prompt, _, _) =>
            {
                // Count how many "## Finding " headers are in the prompt — should equal
                // the number of RESOLVED findings on this page.
                capturedFindingsInPrompt = System.Text.RegularExpressions.Regex.Matches(
                    prompt, @"^## Finding ",
                    System.Text.RegularExpressions.RegexOptions.Multiline).Count;
                return Task.FromResult("**Finding 1: VALID** ok");
            }
        };

        var validator = CreateValidator(invoker);
        var input = new[]
        {
            Unresolved(MakeFinding(0), ScopeResolutionFailure.NotCSharp),
            Resolved(MakeFinding(1)),
            Unresolved(MakeFinding(2), ScopeResolutionFailure.ScopeNotFound),
        };

        _ = await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        Assert.Equal(1, capturedFindingsInPrompt);
    }

    [Fact]
    public void CopilotReviewOptions_ValidateFindings_DefaultsToTrue()
    {
        var options = new CopilotReviewOptions();
        Assert.True(options.ValidateFindings);
    }

    [Fact]
    public async Task ValidateAsync_ResolvedFindings_WritesPromptAndResponseToInspection()
    {
        var invoker = new FakeAgentInvoker();
        invoker.Enqueue("**Finding 1: VALID** — ok");

        var inspection = Substitute.For<IAgentInspectionWriter>();
        inspection.WritePromptAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        inspection.WriteResponseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var validator = CreateValidator(invoker, inspection: inspection);
        var input = new[] { Resolved(MakeFinding(0)) };

        await validator.ValidateAsync(input, "pr:42", CancellationToken.None);

        await inspection.Received(1).WritePromptAsync(
            "pr:42", "validation-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await inspection.Received(1).WriteResponseAsync(
            "pr:42", "validation-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidateAsync_OrdersBySeverityBeforeSendingToAgent()
    {
        // The validator must put critical findings ahead of major and minor in the prompt
        // so the most important issues get the model's first read.
        var capturedSeverityOrder = new List<string>();
        var invoker = new FakeAgentInvoker
        {
            OnInvoke = (prompt, _, _) =>
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    prompt, @"\*\*Severity:\*\*\s*(\w+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match m in matches)
                    capturedSeverityOrder.Add(m.Groups[1].Value.ToLowerInvariant());

                return Task.FromResult("**Finding 1: VALID** ok");
            }
        };

        var validator = CreateValidator(invoker);
        var input = new[]
        {
            Resolved(MakeFinding(0, severity: "minor")),
            Resolved(MakeFinding(1, severity: "critical")),
            Resolved(MakeFinding(2, severity: "major")),
        };

        await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        Assert.Equal(new[] { "critical", "major", "minor" }, capturedSeverityOrder);
    }

    [Fact]
    public async Task ValidateAsync_PageProgressCallback_FiresForEachValidationPage()
    {
        var invoker = new FakeAgentInvoker
        {
            OnInvoke = (_, _, _) => Task.FromResult("**Finding 1: VALID** ok"),
        };

        var validator = CreateValidator(
            invoker,
            pageAllocator: FixedSizePageAllocator(itemsPerPage: 2));
        var input = Enumerable.Range(0, 5).Select(i => Resolved(MakeFinding(i))).ToArray();

        var observedPages = new List<(int page, int total)>();
        await validator.ValidateAsync(
            input, "test:1", CancellationToken.None,
            pageProgress: (p, t) => observedPages.Add((p, t)));

        // 5 findings / 2 per page = 3 pages (2, 2, 1).
        Assert.Equal(new[] { (1, 3), (2, 3), (3, 3) }, observedPages);
    }

    [Fact]
    public async Task ValidateAsync_ForwardsConfiguredModelToInvoker()
    {
        // Ensures the model from CopilotReviewOptions reaches the invoker.
        var invoker = new FakeAgentInvoker();
        invoker.Enqueue("**Finding 1: VALID** ok");

        var validator = CreateValidator(invoker);
        var input = new[] { Resolved(MakeFinding(0)) };

        await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        Assert.Equal("claude-sonnet-4.6", invoker.LastModel);
    }
}
