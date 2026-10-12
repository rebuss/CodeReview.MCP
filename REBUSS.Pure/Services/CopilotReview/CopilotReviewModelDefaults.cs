using Microsoft.Extensions.Options;

namespace REBUSS.Pure.Services.CopilotReview;

/// <summary>
/// Fills <see cref="CopilotReviewOptions.Model"/> with the agent-specific default
/// (<see cref="CopilotReviewOptions.DefaultModelFor"/>) when neither <c>--model</c> nor
/// configuration supplied a value. Runs lazily on first <c>IOptions.Value</c> access,
/// so explicitly configured models always win.
/// </summary>
internal sealed class CopilotReviewModelDefaults : IPostConfigureOptions<CopilotReviewOptions>
{
    private readonly string? _agent;

    public CopilotReviewModelDefaults(string? agent) => _agent = agent;

    public void PostConfigure(string? name, CopilotReviewOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Model))
            options.Model = CopilotReviewOptions.DefaultModelFor(_agent);
    }
}
