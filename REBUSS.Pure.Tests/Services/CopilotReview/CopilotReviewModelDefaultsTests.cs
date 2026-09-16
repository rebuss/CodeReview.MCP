using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using REBUSS.Pure.Services.AgentInvocation;
using REBUSS.Pure.Services.CopilotReview;

namespace REBUSS.Pure.Tests.Services.CopilotReview;

public class CopilotReviewModelDefaultsTests
{
    private static string ResolveModel(string? agent, string? configuredModel)
    {
        var values = new Dictionary<string, string?>();
        if (configuredModel is not null)
            values["CopilotReview:Model"] = configuredModel;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.Configure<CopilotReviewOptions>(configuration.GetSection(CopilotReviewOptions.SectionName));
        services.AddSingleton<IPostConfigureOptions<CopilotReviewOptions>>(new CopilotReviewModelDefaults(agent));

        using var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptions<CopilotReviewOptions>>().Value.Model;
    }

    [Theory]
    [InlineData(null, CopilotReviewOptions.DefaultCopilotModel)]
    [InlineData("copilot", CopilotReviewOptions.DefaultCopilotModel)]
    [InlineData("claude", CopilotReviewOptions.DefaultClaudeModel)]
    public void NoModelConfigured_UsesAgentDefault(string? agent, string expected)
    {
        Assert.Equal(expected, ResolveModel(agent, configuredModel: null));
    }

    [Fact]
    public void ClaudeAgentWithoutModel_DoesNotSendCopilotModelToClaudeCli()
    {
        var model = ResolveModel("claude", configuredModel: null);

        Assert.StartsWith("claude-", ClaudeCliAgentInvoker.NormalizeModelForClaudeCli(model));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("claude")]
    public void ExplicitModel_Wins(string? agent)
    {
        Assert.Equal("my-model", ResolveModel(agent, "my-model"));
    }

    [Fact]
    public void BlankModel_FallsBackToAgentDefault()
    {
        Assert.Equal(CopilotReviewOptions.DefaultClaudeModel, ResolveModel("claude", "  "));
    }
}
