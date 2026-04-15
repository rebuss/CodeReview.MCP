using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using REBUSS.Pure.Core;
using REBUSS.Pure.Core.Models.Pagination;
using REBUSS.Pure.Core.Models.ResponsePacking;
using REBUSS.Pure.Core.Services.CopilotReview;
using REBUSS.Pure.Services.CopilotReview;
using REBUSS.Pure.Services.CopilotReview.Inspection;
using REBUSS.Pure.Services.CopilotReview.Validation;

namespace REBUSS.Pure.Tests.Services.CopilotReview.Validation;

/// <summary>Unit tests for <see cref="FindingValidator"/>. Feature 021.</summary>
public class FindingValidatorTests
{
    private static FindingValidator CreateValidator(
        FakeSessionFactory factory,
        ICopilotInspectionWriter? inspection = null,
        IPageAllocator? pageAllocator = null,
        ITokenEstimator? tokenEstimator = null) =>
        new(factory,
            Options.Create(new CopilotReviewOptions
            {
                Model = "claude-sonnet-4.6",
                ValidateFindings = true,
            }),
            inspection ?? Substitute.For<ICopilotInspectionWriter>(),
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
    public async Task ValidateAsync_NotCSharp_MapsToValidWithoutCopilotCall()
    {
        var factory = new FakeSessionFactory { OnSendAsync = (_, _) => throw new Exception("must not call Copilot") };

        var validator = CreateValidator(factory);
        var input = new[] { Unresolved(MakeFinding(0), ScopeResolutionFailure.NotCSharp) };

        var result = await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        var v = Assert.Single(result);
        Assert.Equal(FindingVerdict.Valid, v.Verdict);
        Assert.Equal(0, factory.CreateSessionCalls);
    }

    [Fact]
    public async Task ValidateAsync_SourceUnavailable_MapsToUncertainWithoutCopilotCall()
    {
        var factory = new FakeSessionFactory { OnSendAsync = (_, _) => throw new Exception("must not call Copilot") };

        var validator = CreateValidator(factory);
        var input = new[] { Unresolved(MakeFinding(0), ScopeResolutionFailure.SourceUnavailable) };

        var result = await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        var v = Assert.Single(result);
        Assert.Equal(FindingVerdict.Uncertain, v.Verdict);
        Assert.Equal(0, factory.CreateSessionCalls);
    }

    [Fact]
    public async Task ValidateAsync_ScopeNotFound_MapsToUncertainWithoutCopilotCall()
    {
        var factory = new FakeSessionFactory { OnSendAsync = (_, _) => throw new Exception("must not call Copilot") };

        var validator = CreateValidator(factory);
        var input = new[] { Unresolved(MakeFinding(0), ScopeResolutionFailure.ScopeNotFound) };

        var result = await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        var v = Assert.Single(result);
        Assert.Equal(FindingVerdict.Uncertain, v.Verdict);
        Assert.Equal(0, factory.CreateSessionCalls);
    }

    [Fact]
    public async Task ValidateAsync_ResolvedFindings_CallsCopilotAndParsesVerdicts()
    {
        var factory = new FakeSessionFactory();
        factory.OnSendAsync = (h, _) =>
        {
            h.PushEvent(new AssistantMessageEvent
            {
                Data = new AssistantMessageData
                {
                    MessageId = "m",
                    Content =
                        "**Finding 1: VALID** — this is a real bug\n" +
                        "**Finding 2: FALSE_POSITIVE** — misinterpreted context\n" +
                        "**Finding 3: UNCERTAIN** — needs cross-file check",
                }
            });
            h.PushEvent(new SessionIdleEvent { Data = new SessionIdleData() });
        };

        var validator = CreateValidator(factory);
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
        Assert.Equal(1, factory.CreateSessionCalls); // single page → single Copilot call
    }

    [Fact]
    public async Task ValidateAsync_TwelveFindings_AcrossThreePages_MakesThreeCalls()
    {
        var factory = new FakeSessionFactory();
        factory.OnSendAsync = (h, _) =>
        {
            // Response template: declare every finding in the page VALID.
            var content = string.Join('\n',
                Enumerable.Range(1, 10).Select(i => $"**Finding {i}: VALID** — ok"));
            h.PushEvent(new AssistantMessageEvent { Data = new AssistantMessageData { MessageId = "m", Content = content } });
            h.PushEvent(new SessionIdleEvent { Data = new SessionIdleData() });
        };

        // Allocator splits into pages of 5 → 12 findings across 3 pages (5, 5, 2).
        var validator = CreateValidator(
            factory,
            pageAllocator: FixedSizePageAllocator(itemsPerPage: 5));
        var input = Enumerable.Range(0, 12).Select(i => Resolved(MakeFinding(i))).ToArray();

        var result = await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        Assert.Equal(12, result.Count);
        Assert.Equal(3, factory.CreateSessionCalls);
    }

    [Fact]
    public async Task ValidateAsync_SessionFailure_FindingsPassThroughAsValid()
    {
        var factory = new FakeSessionFactory();
        factory.OnSendAsync = (h, _) =>
        {
            h.PushEvent(new SessionErrorEvent { Data = new SessionErrorData { ErrorType = "model", Message = "boom" } });
        };

        var validator = CreateValidator(factory);
        var input = new[] { Resolved(MakeFinding(0)), Resolved(MakeFinding(1)) };

        var result = await validator.ValidateAsync(input, "test:1", CancellationToken.None);

        // Graceful degradation: both findings kept as Valid.
        Assert.All(result, r => Assert.Equal(FindingVerdict.Valid, r.Verdict));
    }

    [Fact]
    public async Task ValidateAsync_EmptyInput_NoCopilotCall()
    {
        var factory = new FakeSessionFactory();

        var validator = CreateValidator(factory);
        var result = await validator.ValidateAsync(Array.Empty<FindingWithScope>(), "test:1", CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(0, factory.CreateSessionCalls);
    }

    [Fact]
    public async Task ValidateAsync_MixedResolvedAndUnresolved_OnlyResolvedSentToCopilot()
    {
        var factory = new FakeSessionFactory();
        int? capturedFindingsInPrompt = null;
        factory.OnSendAsync = (h, prompt) =>
        {
            // Count how many "## Finding " headers are in the prompt — should equal
            // the number of RESOLVED findings on this page.
            capturedFindingsInPrompt =
                System.Text.RegularExpressions.Regex.Matches(prompt, @"^## Finding ", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
            h.PushEvent(new AssistantMessageEvent { Data = new AssistantMessageData { MessageId = "m", Content = "**Finding 1: VALID** ok" } });
            h.PushEvent(new SessionIdleEvent { Data = new SessionIdleData() });
        };

        var validator = CreateValidator(factory);
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
        var factory = new FakeSessionFactory();
        factory.OnSendAsync = (h, _) =>
        {
            h.PushEvent(new AssistantMessageEvent
            {
                Data = new AssistantMessageData { MessageId = "m", Content = "**Finding 1: VALID** — ok" }
            });
            h.PushEvent(new SessionIdleEvent { Data = new SessionIdleData() });
        };

        var inspection = Substitute.For<ICopilotInspectionWriter>();
        inspection.WritePromptAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        inspection.WriteResponseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var validator = CreateValidator(factory, inspection: inspection);
        var input = new[] { Resolved(MakeFinding(0)) };

        await validator.ValidateAsync(input, "pr:42", CancellationToken.None);

        await inspection.Received(1).WritePromptAsync(
            "pr:42", "validation-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await inspection.Received(1).WriteResponseAsync(
            "pr:42", "validation-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidateAsync_OrdersBySeverityBeforeSendingToCopilot()
    {
        // The validator must put critical findings ahead of major and minor in the prompt
        // so the most important issues get the model's first read.
        var factory = new FakeSessionFactory();
        var capturedSeverityOrder = new List<string>();
        factory.OnSendAsync = (h, prompt) =>
        {
            // Extract severity values in the order they appear in the prompt.
            var matches = System.Text.RegularExpressions.Regex.Matches(
                prompt, @"\*\*Severity:\*\*\s*(\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in matches)
                capturedSeverityOrder.Add(m.Groups[1].Value.ToLowerInvariant());

            h.PushEvent(new AssistantMessageEvent
            {
                Data = new AssistantMessageData { MessageId = "m", Content = "**Finding 1: VALID** ok" }
            });
            h.PushEvent(new SessionIdleEvent { Data = new SessionIdleData() });
        };

        var validator = CreateValidator(factory);
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
        var factory = new FakeSessionFactory();
        factory.OnSendAsync = (h, _) =>
        {
            h.PushEvent(new AssistantMessageEvent
            {
                Data = new AssistantMessageData { MessageId = "m", Content = "**Finding 1: VALID** ok" }
            });
            h.PushEvent(new SessionIdleEvent { Data = new SessionIdleData() });
        };

        var validator = CreateValidator(
            factory,
            pageAllocator: FixedSizePageAllocator(itemsPerPage: 2));
        var input = Enumerable.Range(0, 5).Select(i => Resolved(MakeFinding(i))).ToArray();

        var observedPages = new List<(int page, int total)>();
        await validator.ValidateAsync(
            input, "test:1", CancellationToken.None,
            pageProgress: (p, t) => observedPages.Add((p, t)));

        // 5 findings / 2 per page = 3 pages (2, 2, 1).
        Assert.Equal(new[] { (1, 3), (2, 3), (3, 3) }, observedPages);
    }

    // ─── Fakes (mirror CopilotPageReviewerTests pattern) ────────────────────────

    private sealed class FakeSessionFactory : ICopilotSessionFactory
    {
        public Action<FakeSessionHandle, string>? OnSendAsync { get; set; }
        public int CreateSessionCalls { get; private set; }

        public Task<ICopilotSessionHandle> CreateSessionAsync(string model, CancellationToken ct)
        {
            CreateSessionCalls++;
            FakeSessionHandle? handle = null;
            handle = new FakeSessionHandle(prompt => OnSendAsync?.Invoke(handle!, prompt));
            return Task.FromResult<ICopilotSessionHandle>(handle);
        }
    }

    private sealed class FakeSessionHandle : ICopilotSessionHandle
    {
        private readonly List<Action<object>> _handlers = new();
        private readonly Action<string> _onSend;
        public FakeSessionHandle(Action<string> onSend) { _onSend = onSend; }

        public Task<string> SendAsync(string prompt, CancellationToken ct)
        {
            _onSend(prompt);
            return Task.FromResult("msg-id-1");
        }

        public IDisposable On(Action<object> handler)
        {
            _handlers.Add(handler);
            return new Subscription(() => _handlers.Remove(handler));
        }

        public void PushEvent(object evt)
        {
            foreach (var h in _handlers.ToList()) h(evt);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class Subscription : IDisposable
        {
            private readonly Action _onDispose;
            public Subscription(Action onDispose) { _onDispose = onDispose; }
            public void Dispose() => _onDispose();
        }
    }
}
