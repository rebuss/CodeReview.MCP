using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using REBUSS.Pure.Services.ContextWindow;

namespace REBUSS.Pure.Tests.Services.ContextWindow;

public class GatewayBudgetStateTests
{
    private static GatewayBudgetState Create(int? configValue, bool configExplicit, IServiceProvider? sp = null)
    {
        var opts = Options.Create(new ContextWindowOptions { GatewayMaxTokens = configValue });
        return new GatewayBudgetState(
            opts,
            sp ?? new EmptyServiceProvider(),
            Substitute.For<ILogger<GatewayBudgetState>>(),
            configExplicit);
    }

    [Fact]
    public void GetEffectiveCap_ConfigExplicit_ReturnsConfigValue_AndIgnoresAutodetect()
    {
        var state = Create(configValue: 50_000, configExplicit: true);

        Assert.Equal(50_000, state.GetEffectiveCap());
    }

    [Fact]
    public void GetEffectiveCap_ConfigExplicitNull_ReturnsNull_DisablingCap()
    {
        // User explicitly opted out of any cap by setting the key to empty/null.
        var state = Create(configValue: null, configExplicit: true);

        Assert.Null(state.GetEffectiveCap());
    }

    [Fact]
    public void GetEffectiveCap_NoConfig_NoMcpServer_FallsBackToSafeDefault()
    {
        var state = Create(configValue: null, configExplicit: false);

        Assert.Equal(HostGatewayDetector.DefaultGatewayTokens, state.GetEffectiveCap());
    }

    [Fact]
    public void GetEffectiveCap_IsCached_AfterFirstCall()
    {
        var sp = Substitute.For<IServiceProvider>();
        // GetService is called via extension; underlying call is GetService(Type).
        sp.GetService(Arg.Any<Type>()).Returns((object?)null);

        var state = Create(configValue: null, configExplicit: false, sp: sp);

        var first = state.GetEffectiveCap();
        var second = state.GetEffectiveCap();
        var third = state.GetEffectiveCap();

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        // Autodetect must only probe the service provider once, regardless of call count.
        sp.Received(1).GetService(Arg.Any<Type>());
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
