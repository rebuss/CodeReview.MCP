using System.Diagnostics;
using Microsoft.Extensions.Logging;
using REBUSS.Pure.Mcp;
using REBUSS.Pure.Mcp.Models;
using REBUSS.Pure.Services.LocalReview;
using REBUSS.Pure.Tools.Models;
using System.Text.Json;

namespace REBUSS.Pure.Tools
{
    /// <summary>
    /// Handles the <c>get_local_files</c> MCP tool.
    /// Returns a classified list of locally changed files so an AI agent can
    /// decide which files to inspect in detail.
    /// </summary>
    public class GetLocalChangesFilesToolHandler : IMcpToolHandler
    {
        private readonly ILocalReviewProvider _reviewProvider;
        private readonly ILogger<GetLocalChangesFilesToolHandler> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public string ToolName => "get_local_files";

        public GetLocalChangesFilesToolHandler(
            ILocalReviewProvider reviewProvider,
            ILogger<GetLocalChangesFilesToolHandler> logger)
        {
            _reviewProvider = reviewProvider;
            _logger = logger;
        }

        public McpTool GetToolDefinition() => new()
        {
            Name = ToolName,
            Description =
                "Lists all locally changed files in the git repository with classification metadata " +
                "(status, extension, binary/generated/test flags, review priority) and a summary by category. " +
                "Use this as the first step of a self-review to discover what changed before inspecting diffs. " +
                "Supported scopes: 'working-tree' (default, staged + unstaged vs HEAD), " +
                "'staged' (index vs HEAD only), or any branch/ref name to diff the current branch against it.",
            InputSchema = new ToolInputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, ToolProperty>
                {
                    ["scope"] = new ToolProperty
                    {
                        Type = "string",
                        Description =
                            "The change scope to review. " +
                            "'working-tree' (default): all uncommitted changes vs HEAD. " +
                            "'staged': only staged (indexed) changes vs HEAD. " +
                            "Any other value is treated as a base branch/ref (e.g. 'main', 'origin/main') " +
                            "and returns all commits on the current branch not yet merged into that base."
                    }
                },
                Required = new List<string>()
            }
        };

        public async Task<ToolResult> ExecuteAsync(
            Dictionary<string, object>? arguments,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var scopeStr = ExtractScope(arguments);
                var scope = LocalReviewScope.Parse(scopeStr);

                _logger.LogInformation("[{ToolName}] Entry: scope={Scope}", ToolName, scope);
                var sw = Stopwatch.StartNew();

                var reviewFiles = await _reviewProvider.GetFilesAsync(scope, cancellationToken);

                var result = new LocalReviewFilesResult
                {
                    RepositoryRoot = reviewFiles.RepositoryRoot,
                    Scope = reviewFiles.Scope,
                    CurrentBranch = reviewFiles.CurrentBranch,
                    TotalFiles = reviewFiles.Files.Count,
                    Files = reviewFiles.Files.Select(f => new PullRequestFileItem
                    {
                        Path = f.Path,
                        Status = f.Status,
                        Additions = f.Additions,
                        Deletions = f.Deletions,
                        Changes = f.Changes,
                        Extension = f.Extension,
                        IsBinary = f.IsBinary,
                        IsGenerated = f.IsGenerated,
                        IsTestFile = f.IsTestFile,
                        ReviewPriority = f.ReviewPriority
                    }).ToList(),
                    Summary = new PullRequestFilesSummaryResult
                    {
                        SourceFiles = reviewFiles.Summary.SourceFiles,
                        TestFiles = reviewFiles.Summary.TestFiles,
                        ConfigFiles = reviewFiles.Summary.ConfigFiles,
                        DocsFiles = reviewFiles.Summary.DocsFiles,
                        BinaryFiles = reviewFiles.Summary.BinaryFiles,
                        GeneratedFiles = reviewFiles.Summary.GeneratedFiles,
                        HighPriorityFiles = reviewFiles.Summary.HighPriorityFiles
                    }
                };

                var json = JsonSerializer.Serialize(result, JsonOptions);
                sw.Stop();

                _logger.LogInformation(
                    "[{ToolName}] Completed: scope={Scope}, {FileCount} file(s), {ResponseLength} chars, {ElapsedMs}ms",
                    ToolName, scope, reviewFiles.Files.Count, json.Length, sw.ElapsedMilliseconds);

                return CreateSuccessResult(json);
            }
            catch (LocalRepositoryNotFoundException ex)
            {
                _logger.LogWarning(ex, "[{ToolName}] Repository not found", ToolName);
                return CreateErrorResult($"Repository not found: {ex.Message}");
            }
            catch (GitCommandException ex)
            {
                _logger.LogWarning(ex, "[{ToolName}] Git command failed", ToolName);
                return CreateErrorResult($"Git command failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{ToolName}] Error", ToolName);
                return CreateErrorResult($"Error retrieving local files: {ex.Message}");
            }
        }

        private static string? ExtractScope(Dictionary<string, object>? arguments)
        {
            if (arguments == null || !arguments.TryGetValue("scope", out var scopeObj))
                return null;

            return scopeObj is JsonElement jsonElement
                ? jsonElement.GetString()
                : scopeObj?.ToString();
        }

        private static ToolResult CreateSuccessResult(string text) => new()
        {
            Content = new List<ContentItem> { new() { Type = "text", Text = text } },
            IsError = false
        };

        private static ToolResult CreateErrorResult(string errorMessage) => new()
        {
            Content = new List<ContentItem> { new() { Type = "text", Text = $"Error: {errorMessage}" } },
            IsError = true
        };
    }
}
