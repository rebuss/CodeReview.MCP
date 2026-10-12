using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using REBUSS.Pure.Core.Services.CopilotReview;
using REBUSS.Pure.Properties;

namespace REBUSS.Pure.Services.CopilotReview;

/// <summary>
/// <see cref="ICopilotAvailabilityDetector"/> for <c>--agent claude</c>. The operator chose
/// Claude explicitly, so the review layer is available unless disabled by configuration —
/// the Copilot SDK is never started. Environment problems (e.g. <c>claude</c> not on PATH,
/// not logged in) are not probed here; they surface per page from
/// <c>ClaudeCliAgentInvoker</c> as failed page reviews.
/// </summary>
internal sealed class ClaudeCliAvailabilityDetector : ICopilotAvailabilityDetector
{
    private readonly IOptions<CopilotReviewOptions> _options;
    private readonly ILogger<ClaudeCliAvailabilityDetector> _logger;
    private readonly Lazy<CopilotVerdict> _verdict;

    public ClaudeCliAvailabilityDetector(
        IOptions<CopilotReviewOptions> options,
        ILogger<ClaudeCliAvailabilityDetector> logger)
    {
        _options = options;
        _logger = logger;
        _verdict = new Lazy<CopilotVerdict>(BuildVerdict);
    }

    /// <inheritdoc />
    /// <remarks>Never throws: the only unavailable verdict is <see cref="CopilotAuthReason.DisabledByConfig"/>, which strict mode ignores (FR-016).</remarks>
    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_verdict.Value.IsAvailable);
    }

    /// <inheritdoc />
    public Task<CopilotVerdict> GetVerdictAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_verdict.Value);
    }

    private CopilotVerdict BuildVerdict()
    {
        var options = _options.Value;
        var reason = options.Enabled ? CopilotAuthReason.Ok : CopilotAuthReason.DisabledByConfig;
        if (!options.Enabled)
            _logger.LogInformation(Resources.LogCopilotNotAvailable, "disabled by configuration");

        return new CopilotVerdict(
            IsAvailable: options.Enabled,
            Reason: reason,
            TokenSource: CopilotTokenSource.None,
            ConfiguredModel: options.Model,
            EntitledModels: Array.Empty<string>(),
            Login: null,
            Host: null,
            Remediation: string.Empty);
    }
}
