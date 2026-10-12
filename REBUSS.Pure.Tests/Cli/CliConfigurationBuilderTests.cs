using REBUSS.Pure.Cli;
using REBUSS.Pure.Services.CopilotReview;

namespace REBUSS.Pure.Tests.Cli;

public class CliConfigurationBuilderTests
{
    private const string ModelKey = "CopilotReview:Model";

    [Fact]
    public void BuildOverrides_WithModel_MapsToCopilotReviewModel()
    {
        var parse = CliArgumentParser.Parse(["--repo", "C:\\repo", "--model", "claude-opus-4.5"]);

        var overrides = CliConfigurationBuilder.BuildOverrides(parse);

        Assert.Equal($"{CopilotReviewOptions.SectionName}:{nameof(CopilotReviewOptions.Model)}", ModelKey);
        Assert.Equal("claude-opus-4.5", overrides[ModelKey]);
    }

    [Fact]
    public void BuildOverrides_WithoutModel_DoesNotEmitModelKey()
    {
        var parse = CliArgumentParser.Parse(["--repo", "C:\\repo"]);

        var overrides = CliConfigurationBuilder.BuildOverrides(parse);

        Assert.False(overrides.ContainsKey(ModelKey));
    }
}
