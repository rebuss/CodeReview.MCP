using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using REBUSS.Pure.Core.Models.CopilotReview;
using REBUSS.Pure.Services.CopilotReview;

namespace REBUSS.Pure.Tests.Services.CopilotReview;

/// <summary>
/// Focused unit tests for <see cref="FindingValidationPipeline"/> — the predicate
/// and the disabled-mode no-op. The full 4-phase happy path is exercised via
/// production DI in the integration tests under <see cref="AgentReviewOrchestratorTests"/>;
/// the validator and scope-resolver collaborators are <c>sealed</c> and pull in
/// heavyweight sub-dependencies (<c>IAgentInvoker</c>, <c>ITokenEstimator</c>,
/// <c>IFindingSourceProviderSelector</c>) that are not worth standing up here.
/// </summary>
public class FindingValidationPipelineTests
{
    private static FindingValidationPipeline NewPipeline(
        bool validateFindings,
        bool validatorNull = true,
        bool scopeResolverNull = true)
    {
        var options = Options.Create(new CopilotReviewOptions { ValidateFindings = validateFindings });
        // Production-equivalent ctor: nulls are valid (DI may not wire them, FR-021 US4).
        return new FindingValidationPipeline(
            options,
            NullLogger<FindingValidationPipeline>.Instance,
            validator: validatorNull ? null : throw new NotSupportedException("Construct real validator if needed."),
            scopeResolver: scopeResolverNull ? null : throw new NotSupportedException("Construct real resolver if needed."));
    }

    [Fact]
    public void IsEnabled_FlagOff_ReturnsFalse()
    {
        var pipeline = NewPipeline(validateFindings: false);

        Assert.False(pipeline.IsEnabled);
    }

    [Fact]
    public void IsEnabled_FlagOn_ValidatorNull_ReturnsFalse()
    {
        // Both deps null — flag alone is not enough.
        var pipeline = NewPipeline(validateFindings: true);

        Assert.False(pipeline.IsEnabled);
    }

    [Fact]
    public async Task RunAsync_WhenIsEnabledFalse_LeavesPageResultsUnchanged()
    {
        var pipeline = NewPipeline(validateFindings: false);
        var job = new AgentReviewJob
        {
            ReviewKey = "pr:42",
            Completion = new TaskCompletionSource<AgentReviewResult>(),
        };
        var pageResults = new[]
        {
            AgentPageReviewResult.Success(1, "**critical** finding (line 10)\nbody-1", attemptsMade: 1),
            AgentPageReviewResult.Success(2, "**major** finding (line 20)\nbody-2", attemptsMade: 1),
        };

        // No exception, no mutation — RunAsync short-circuits on IsEnabled == false.
        await pipeline.RunAsync(job, pageResults, CancellationToken.None);

        Assert.Equal("**critical** finding (line 10)\nbody-1", pageResults[0].ReviewText);
        Assert.Equal("**major** finding (line 20)\nbody-2", pageResults[1].ReviewText);
    }

    [Fact]
    public async Task RunAsync_WhenIsEnabledFalse_DoesNotTouchJobActivity()
    {
        var pipeline = NewPipeline(validateFindings: false);
        var job = new AgentReviewJob
        {
            ReviewKey = "pr:42",
            Completion = new TaskCompletionSource<AgentReviewResult>(),
            CurrentActivity = "untouched",
        };
        var pageResults = new[]
        {
            AgentPageReviewResult.Success(1, "**critical** finding (line 10)\nbody-1", attemptsMade: 1),
        };

        await pipeline.RunAsync(job, pageResults, CancellationToken.None);

        // CurrentActivity is only mutated by phase 2/3; the disabled-mode short-circuit
        // must not write to it.
        Assert.Equal("untouched", job.CurrentActivity);
    }
}
