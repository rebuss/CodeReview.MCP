using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using REBUSS.Pure.Core.Models;
using REBUSS.Pure.Core.Models.CopilotReview;
using REBUSS.Pure.Core.Models.Pagination;
using REBUSS.Pure.Core.Models.ResponsePacking;
using REBUSS.Pure.Core.Services.CopilotReview;
using REBUSS.Pure.Services.CopilotReview;
using REBUSS.Pure.Services.PrEnrichment;

namespace REBUSS.Pure.Tests.Services.CopilotReview;

/// <summary>
/// Focused unit tests for the per-page execution collaborator extracted from
/// <see cref="AgentReviewOrchestrator"/>. The orchestrator-level tests still cover the
/// integration (job registration, validation pipeline wiring, dispose drain).
/// </summary>
public class PageReviewExecutorTests
{
    private static AgentReviewJob NewJob(string reviewKey = "pr:42") => new()
    {
        ReviewKey = reviewKey,
        Completion = new TaskCompletionSource<AgentReviewResult>(
            TaskCreationOptions.RunContinuationsAsynchronously),
    };

    private static PageReviewExecutor NewExecutor(
        IAgentPageReviewer reviewer,
        int maxConcurrentPages = 6)
        => new(
            reviewer,
            Options.Create(new CopilotReviewOptions { MaxConcurrentPages = maxConcurrentPages }),
            NullLogger<PageReviewExecutor>.Instance);

    private static PageAllocation BuildAllocation(int pages, int filesPerPage = 1)
    {
        var slices = new List<PageSlice>();
        var totalItems = pages * filesPerPage;
        for (var p = 0; p < pages; p++)
        {
            var items = Enumerable.Range(p * filesPerPage, filesPerPage)
                .Select(i => new PageSliceItem(i, PackingItemStatus.Included, 500))
                .ToArray();
            slices.Add(new PageSlice(
                PageNumber: p + 1,
                StartIndex: p * filesPerPage,
                EndIndex: (p + 1) * filesPerPage,
                Items: items,
                BudgetUsed: 500 * filesPerPage,
                BudgetRemaining: 100_000));
        }
        return new PageAllocation(slices, pages, totalItems);
    }

    private static IEnrichmentResult BuildEnrichment(int fileCount)
        => new PrEnrichmentResult
        {
            PrNumber = 42,
            HeadSha = "abc",
            SortedCandidates = Enumerable.Range(0, fileCount)
                .Select(i => new PackingCandidate($"src/F{i}.cs", 500, FileCategory.Source, 35))
                .ToArray(),
            EnrichedByPath = Enumerable.Range(0, fileCount)
                .ToDictionary(i => $"src/F{i}.cs", i => $"=== src/F{i}.cs ===\nbody{i}", StringComparer.OrdinalIgnoreCase),
            Allocation = new PageAllocation(Array.Empty<PageSlice>(), 0, 0),
            SafeBudgetTokens = 128_000,
            CompletedAt = DateTimeOffset.UtcNow,
            RawFileChangesFromDiff = fileCount,
        };

    [Fact]
    public async Task ExecuteAllAsync_AllPagesSucceedFirstAttempt_ReturnsSuccessSlots_AndIncrementsCompletedPages()
    {
        var reviewer = Substitute.For<IAgentPageReviewer>();
        reviewer.ReviewPageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(AgentPageReviewResult.Success(ci.Arg<int>(), "ok", 1)));

        var executor = NewExecutor(reviewer);
        var job = NewJob();

        var results = await executor.ExecuteAllAsync(job, BuildAllocation(3), BuildEnrichment(3), CancellationToken.None);

        Assert.Equal(3, results.Length);
        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.Equal(3, Volatile.Read(ref job.CompletedPages));
        // Re-wrapped Success → AttemptsMade == 1 because the first attempt won.
        Assert.All(results, r => Assert.Equal(1, r.AttemptsMade));
    }

    [Fact]
    public async Task ExecuteAllAsync_TransientFailureThenSuccess_AttemptsMadeReflectsRetryThatWon()
    {
        // Page 1 fails on attempt 1, succeeds on attempt 2.
        var reviewer = Substitute.For<IAgentPageReviewer>();
        // Interlocked because the NSubstitute lambda is invoked from the executor's
        // Parallel.ForEachAsync continuations — even though only page 1's branch reads/writes
        // this counter, async-chain continuations can resume on different thread-pool threads
        // and a future refactor that rewires the counter across pages would silently race.
        var page1Calls = 0;
        reviewer.ReviewPageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                if (ci.Arg<int>() == 1)
                {
                    var attempt = Interlocked.Increment(ref page1Calls);
                    return Task.FromResult(attempt == 1
                        ? AgentPageReviewResult.Failure(1, Array.Empty<string>(), "transient", attemptsMade: 1)
                        : AgentPageReviewResult.Success(1, "ok-retry", attemptsMade: 1));
                }
                return Task.FromResult(AgentPageReviewResult.Success(ci.Arg<int>(), "ok", attemptsMade: 1));
            });

        var executor = NewExecutor(reviewer);
        var job = NewJob();

        var results = await executor.ExecuteAllAsync(job, BuildAllocation(2), BuildEnrichment(2), CancellationToken.None);

        var page1 = Assert.Single(results, r => r.PageNumber == 1);
        Assert.True(page1.Succeeded);
        Assert.Equal(2, page1.AttemptsMade); // Won on second attempt.
        Assert.Equal(2, Volatile.Read(ref page1Calls));
    }

    [Fact]
    public async Task ExecuteAllAsync_AllAttemptsExhausted_FailureCarriesFilePaths_AndAttemptsMade3()
    {
        var reviewer = Substitute.For<IAgentPageReviewer>();
        reviewer.ReviewPageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(
                AgentPageReviewResult.Failure(ci.Arg<int>(), Array.Empty<string>(), "boom", attemptsMade: 1)));

        var executor = NewExecutor(reviewer);
        var job = NewJob();

        var results = await executor.ExecuteAllAsync(job, BuildAllocation(1), BuildEnrichment(1), CancellationToken.None);

        var only = Assert.Single(results);
        Assert.False(only.Succeeded);
        Assert.Equal(3, only.AttemptsMade);
        // Executor fills in the file paths (the IAgentPageReviewer fake returns empty paths).
        Assert.Single(only.FailedFilePaths);
        Assert.Equal("src/F0.cs", only.FailedFilePaths[0]);
        Assert.Equal(1, Volatile.Read(ref job.CompletedPages));
    }

    [Fact]
    public async Task ExecuteAllAsync_BatchSize2_With5Pages_DispatchesIn3Batches_AllSlotsFilled()
    {
        var reviewer = Substitute.For<IAgentPageReviewer>();
        reviewer.ReviewPageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(AgentPageReviewResult.Success(ci.Arg<int>(), $"ok-{ci.Arg<int>()}", 1)));

        var executor = NewExecutor(reviewer, maxConcurrentPages: 2);
        var job = NewJob();

        var results = await executor.ExecuteAllAsync(job, BuildAllocation(5), BuildEnrichment(5), CancellationToken.None);

        Assert.Equal(5, results.Length);
        Assert.All(results, r => Assert.True(r.Succeeded));
        // Slot integrity: each result lands in the slot whose PageNumber == slot+1.
        for (var i = 0; i < results.Length; i++)
            Assert.Equal(i + 1, results[i].PageNumber);
    }

    [Fact]
    public async Task ExecuteAllAsync_EmptyAllocation_ReturnsEmptyArray_NoSdkCalls()
    {
        var reviewer = Substitute.For<IAgentPageReviewer>();
        var executor = NewExecutor(reviewer);
        var job = NewJob();

        var results = await executor.ExecuteAllAsync(job, BuildAllocation(0), BuildEnrichment(0), CancellationToken.None);

        Assert.Empty(results);
        Assert.Equal(0, Volatile.Read(ref job.CompletedPages));
        await reviewer.DidNotReceive().ReviewPageAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAllAsync_ReviewerThrows_TreatsAsFailedAttempt_RetriesUpTo3Times()
    {
        var calls = 0;
        var reviewer = Substitute.For<IAgentPageReviewer>();
        reviewer.ReviewPageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<AgentPageReviewResult>>(_ =>
            {
                calls++;
                throw new InvalidOperationException("contract violation");
            });

        var executor = NewExecutor(reviewer);
        var job = NewJob();

        var results = await executor.ExecuteAllAsync(job, BuildAllocation(1), BuildEnrichment(1), CancellationToken.None);

        var only = Assert.Single(results);
        Assert.False(only.Succeeded);
        Assert.Equal(3, only.AttemptsMade);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ExecuteAllAsync_CancelledBeforeStart_PropagatesOperationCanceledException()
    {
        var reviewer = Substitute.For<IAgentPageReviewer>();
        reviewer.ReviewPageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(AgentPageReviewResult.Success(ci.Arg<int>(), "ok", 1)));

        var executor = NewExecutor(reviewer);
        var job = NewJob();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            executor.ExecuteAllAsync(job, BuildAllocation(2), BuildEnrichment(2), cts.Token));
    }
}
