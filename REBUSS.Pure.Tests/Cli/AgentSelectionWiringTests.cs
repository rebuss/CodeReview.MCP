using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using REBUSS.Pure.Cli;
using REBUSS.Pure.Core.Services.AgentInvocation;
using REBUSS.Pure.Core.Services.CopilotReview;
using REBUSS.Pure.Services.AgentInvocation;
using REBUSS.Pure.Services.CopilotReview;
using REBUSS.Pure.Services.CopilotReview.Inspection;

namespace REBUSS.Pure.Tests.Cli;

/// <summary>
/// Verifies that the DI switch in <c>Program.ConfigureBusinessServices</c> picks the
/// correct <see cref="IAgentInvoker"/> implementation based on the parsed <c>--agent</c>
/// value. We mirror the switch here rather than invoking Program.Main, so the test
/// stays decoupled from the full MCP host startup (Copilot SDK, hosted services, etc.).
/// </summary>
public class AgentSelectionWiringTests
{
    /// <summary>Replicates the exact switch used in Program.ConfigureBusinessServices.</summary>
    private static void RegisterAgentInvoker(IServiceCollection services, string? agent)
    {
        if (string.Equals(agent, CliArgumentParser.AgentClaude, StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IAgentInvoker, ClaudeCliAgentInvoker>();
        else
            services.AddSingleton<IAgentInvoker, CopilotAgentInvoker>();
    }

    private static IServiceProvider BuildContainer(string? agent)
    {
        var services = new ServiceCollection();

        // Open-generic ILogger<T> for any constructor-injected logger. Avoids having
        // to register each concrete NullLogger<Foo> individually.
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Dependencies CopilotAgentInvoker needs — substituted so resolution works
        // without the full Copilot SDK startup chain.
        services.AddSingleton(Substitute.For<ICopilotSessionFactory>());
        services.AddSingleton(Options.Create(new CopilotReviewOptions()));

        RegisterAgentInvoker(services, agent);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AgentClaude_RegistersClaudeCliAgentInvoker()
    {
        using var sp = (ServiceProvider)BuildContainer("claude");

        var invoker = sp.GetRequiredService<IAgentInvoker>();

        Assert.IsType<ClaudeCliAgentInvoker>(invoker);
    }

    [Fact]
    public void AgentCopilot_RegistersCopilotAgentInvoker()
    {
        using var sp = (ServiceProvider)BuildContainer("copilot");

        var invoker = sp.GetRequiredService<IAgentInvoker>();

        Assert.IsType<CopilotAgentInvoker>(invoker);
    }

    [Fact]
    public void AgentNull_DefaultsToCopilotAgentInvoker()
    {
        // Preserves existing behavior for operators who never opted into Claude —
        // an absent --agent flag must continue to wire Copilot SDK.
        using var sp = (ServiceProvider)BuildContainer(null);

        var invoker = sp.GetRequiredService<IAgentInvoker>();

        Assert.IsType<CopilotAgentInvoker>(invoker);
    }

    [Fact]
    public void AgentClaude_CaseInsensitive()
    {
        // Guards against operators typing `--agent Claude` or `CLAUDE` in shell configs.
        using var sp = (ServiceProvider)BuildContainer("CLAUDE");

        var invoker = sp.GetRequiredService<IAgentInvoker>();

        Assert.IsType<ClaudeCliAgentInvoker>(invoker);
    }

    [Fact]
    public void AgentPageReviewer_ResolvesWithEitherInvoker()
    {
        // Smoke: the reviewer's ctor takes IAgentInvoker post-refactor. Both branches
        // of the DI switch must produce a container where it resolves without error.
        foreach (var agent in new[] { "copilot", "claude" })
        {
            var services = new ServiceCollection();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton(Substitute.For<ICopilotSessionFactory>());
            services.AddSingleton(Options.Create(new CopilotReviewOptions()));
            services.AddSingleton<IAgentInspectionWriter, NoOpAgentInspectionWriter>();

            RegisterAgentInvoker(services, agent);
            services.AddSingleton<IAgentPageReviewer, AgentPageReviewer>();

            using var sp = services.BuildServiceProvider();
            var reviewer = sp.GetRequiredService<IAgentPageReviewer>();

            Assert.NotNull(reviewer);
        }
    }
}
