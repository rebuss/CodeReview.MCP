namespace REBUSS.Pure.Services.ContextWindow;

/// <summary>
/// Maps an MCP host's <c>clientInfo.Name</c> (received during the <c>initialize</c>
/// handshake) to the per-tool-result token cap that host is known to enforce.
///
/// This lets REBUSS.Pure paginate diff content to a size the host gateway will
/// actually accept, without requiring the user to set <c>GatewayMaxTokens</c>
/// manually for each environment.
///
/// Mapping is intentionally conservative — when in doubt, return the safe
/// fallback so a single tool-call response is unlikely to exceed any known host limit.
/// </summary>
internal static class HostGatewayDetector
{
    /// <summary>Safe fallback when the host is unknown or <paramref name="clientName"/> is empty.</summary>
    public const int DefaultGatewayTokens = 20_000;

    /// <summary>
    /// Returns the token cap for the given client name, or <see cref="DefaultGatewayTokens"/>
    /// when the host is unknown. Match is case-insensitive substring.
    /// </summary>
    public static int Detect(string? clientName)
    {
        if (string.IsNullOrWhiteSpace(clientName))
            return DefaultGatewayTokens;

        var normalized = clientName.Trim().ToLowerInvariant();

        // Claude Code (CLI / desktop) and claude.ai web both reliably handle ~25k.
        if (normalized.Contains("claude-code") || normalized.Contains("claude code") ||
            normalized.Contains("claude-ai")   || normalized.Contains("claude.ai")  ||
            normalized.Contains("claudecode"))
            return 25_000;

        // Cursor IDE — slightly more conservative.
        if (normalized.Contains("cursor"))
            return 24_000;

        // OpenAI Codex CLI — conservative, matches observed behavior.
        if (normalized.Contains("codex"))
            return 20_000;

        return DefaultGatewayTokens;
    }
}
