using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace REBUSS.Pure.Services.ContextWindow;

/// <inheritdoc cref="IGatewayBudgetState"/>
public sealed class GatewayBudgetState : IGatewayBudgetState
{
    private readonly IOptions<ContextWindowOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GatewayBudgetState> _logger;
    private readonly bool _configExplicit;

    private readonly object _lock = new();
    private bool _autodetectAttempted;
    private int? _autodetectedCap;

    public GatewayBudgetState(
        IOptions<ContextWindowOptions> options,
        IServiceProvider serviceProvider,
        ILogger<GatewayBudgetState> logger,
        bool configExplicit)
    {
        _options = options;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configExplicit = configExplicit;
    }

    public int? GetEffectiveCap()
    {
        // 1. Explicit config value always wins (even if 0/null — user opted out).
        if (_configExplicit)
            return _options.Value.GatewayMaxTokens;

        // 2. Lazy autodetect from MCP clientInfo (only available after `initialize`).
        if (_autodetectAttempted)
            return _autodetectedCap;

        lock (_lock)
        {
            if (_autodetectAttempted)
                return _autodetectedCap;

            string? clientName = null;
            try
            {
                var server = _serviceProvider.GetService<McpServer>();
                clientName = server?.ClientInfo?.Name;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve IMcpServer for gateway autodetect");
            }

            _autodetectedCap = HostGatewayDetector.Detect(clientName);
            _autodetectAttempted = true;

            _logger.LogInformation(
                "Gateway autodetect: clientInfo.Name={ClientName}, effective cap={Cap} tokens",
                clientName ?? "<unknown>", _autodetectedCap);

            return _autodetectedCap;
        }
    }
}
