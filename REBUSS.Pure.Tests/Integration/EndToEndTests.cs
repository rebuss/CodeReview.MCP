using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using NSubstitute;
using REBUSS.Pure.Core;
using REBUSS.Pure.Core.Exceptions;
using REBUSS.Pure.Core.Models;
using REBUSS.Pure.Core.Shared;
using REBUSS.Pure.Services.ResponsePacking;
using REBUSS.Pure.Tools;
using System.Text.Json;
using REBUSS.Pure.Services.Pagination;

namespace REBUSS.Pure.Tests.Integration;

/// <summary>
/// Integration tests: real handler → mocked diff provider → structured JSON result.
/// Validates the full handler pipeline including DI construction, serialization, and error handling.
/// </summary>
public class EndToEndTests
{
    private readonly IPullRequestDataProvider _diffProvider = Substitute.For<IPullRequestDataProvider>();
    private readonly IContextBudgetResolver _budgetResolver = Substitute.For<IContextBudgetResolver>();
    private readonly ITokenEstimator _tokenEstimator = Substitute.For<ITokenEstimator>();
    private readonly IFileClassifier _fileClassifier = Substitute.For<IFileClassifier>();

    public EndToEndTests()
    {
        _budgetResolver.Resolve(Arg.Any<int?>(), Arg.Any<string?>())
            .Returns(new BudgetResolutionResult(200000, 140000, BudgetSource.Default, Array.Empty<string>()));
        _tokenEstimator.Estimate(Arg.Any<string>(), Arg.Any<int>())
            .Returns(new TokenEstimationResult(100, 0.07, true));
        _fileClassifier.Classify(Arg.Any<string>())
            .Returns(new FileClassification { Category = FileCategory.Source, Extension = ".cs", IsBinary = false, IsGenerated = false, IsTestFile = false, ReviewPriority = "high" });
    }

    private GetPullRequestDiffToolHandler BuildHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddSingleton(_diffProvider);
        services.AddSingleton<IResponsePacker, ResponsePacker>();
        services.AddSingleton(_budgetResolver);
        services.AddSingleton(_tokenEstimator);
        services.AddSingleton(_fileClassifier);
        services.AddSingleton<IPageAllocator, PageAllocator>();
        services.AddSingleton<IPageReferenceCodec, PageReferenceCodec>();
        services.AddSingleton<GetPullRequestDiffToolHandler>();

        return services.BuildServiceProvider().GetRequiredService<GetPullRequestDiffToolHandler>();
    }

    [Fact]
    public async Task FullPipeline_ReturnsStructuredJson_ByDefault()
    {
        _diffProvider
            .GetDiffAsync(42, Arg.Any<CancellationToken>())
            .Returns(new PullRequestDiff
            {
                Title = "Fix bug",
                Status = "active",
                SourceBranch = "feature/fix",
                TargetBranch = "main",
                Files = new List<FileChange>
                {
                    new()
                    {
                        Path = "/src/App.cs",
                        ChangeType = "edit",
                        Additions = 1,
                        Deletions = 1,
                        Hunks = new List<DiffHunk>
                        {
                            new()
                            {
                                OldStart = 1, OldCount = 1, NewStart = 1, NewCount = 1,
                                Lines = new List<DiffLine>
                                {
                                    new() { Op = '-', Text = "old" },
                                    new() { Op = '+', Text = "new" }
                                }
                            }
                        }
                    }
                }
            });

        var handler = BuildHandler();
        var json = await handler.ExecuteAsync(prNumber: 42);

        var structured = JsonDocument.Parse(json).RootElement;

        Assert.Equal(42, structured.GetProperty("prNumber").GetInt32());
        Assert.True(structured.TryGetProperty("files", out var files));
        Assert.Equal(1, files.GetArrayLength());
        Assert.Equal("/src/App.cs", files[0].GetProperty("path").GetString());
        Assert.True(files[0].TryGetProperty("hunks", out var hunks));
        Assert.Equal(1, hunks.GetArrayLength());
        Assert.False(structured.TryGetProperty("title", out _));
    }

    [Fact]
    public async Task FullPipeline_PrNotFound_ThrowsMcpException()
    {
        _diffProvider
            .GetDiffAsync(999, Arg.Any<CancellationToken>())
            .Returns<PullRequestDiff>(x => throw new PullRequestNotFoundException("PR 999 not found"));

        var handler = BuildHandler();

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            handler.ExecuteAsync(prNumber: 999));

        Assert.Contains("not found", ex.Message);
    }
}