namespace REBUSS.Pure.Services.ContextWindow;

/// <summary>
/// Singleton state that resolves the effective per-tool-result token cap
/// imposed by the MCP host gateway.
///
/// Resolution order (first non-null wins):
/// <list type="number">
///   <item>Explicit <c>ContextWindow:GatewayMaxTokens</c> value from configuration.</item>
///   <item>Autodetected value derived from the MCP <c>initialize</c> handshake's
///         <c>clientInfo.Name</c> via <see cref="HostGatewayDetector"/>.</item>
///   <item><c>null</c> — no cap; the resolver falls through to model registry / default.</item>
/// </list>
///
/// Autodetection runs lazily on first access (after the host has completed the
/// MCP <c>initialize</c> handshake). Once the user has set the value explicitly
/// in configuration, autodetection is skipped entirely.
/// </summary>
public interface IGatewayBudgetState
{
    /// <summary>
    /// Returns the effective gateway cap in tokens, or <c>null</c> when no cap applies.
    /// </summary>
    int? GetEffectiveCap();
}
