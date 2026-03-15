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
    /// Handles the <c>get_local_file_diff</c> MCP tool.
    /// Returns a structured diff for a single locally changed file so an AI agent
    /// can inspect the exact changes in detail.
    /// </summary>
    public class GetLocalFileDiffToolHandler : IMcpToolHandler
    {
        private readonly ILocalReviewProvider _reviewProvider;
        private readonly ILogger<GetLocalFileDiffToolHandler> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public string ToolName => "get_local_file_diff";

        public GetLocalFileDiffToolHandler(
            ILocalReviewProvider reviewProvider,
            ILogger<GetLocalFileDiffToolHandler> logger)
        {
            _reviewProvider = reviewProvider;
            _logger = logger;
        }

        public McpTool GetToolDefinition() => new()
        {
            Name = ToolName,
            Description =
                "Returns a structured diff for a single locally changed file. " +
                "Call get_local_files first to discover which files changed, then call this tool " +
                "for files you want to inspect in detail. " +
                "Supported scopes match get_local_files: 'working-tree' (default), 'staged', or a base branch/ref.",
            InputSchema = new ToolInputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, ToolProperty>
                {
                    ["path"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Repository-relative path of the file to diff (e.g. 'src/Service.cs')"
                    },
                    ["scope"] = new ToolProperty
                    {
                        Type = "string",
                        Description =
                            "The change scope. " +
                            "'working-tree' (default): all uncommitted changes vs HEAD. " +
                            "'staged': only staged changes vs HEAD. " +
                            "Any other value is treated as a base branch/ref."
                    }
                },
                Required = new List<string> { "path" }
            }
        };

        public async Task<ToolResult> ExecuteAsync(
            Dictionary<string, object>? arguments,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!TryExtractPath(arguments, out var path, out var error))
                {
                    _logger.LogWarning("[{ToolName}] Validation failed: {Error}", ToolName, error);
                    return CreateErrorResult(error);
                }

                var scopeStr = ExtractScope(arguments!);
                var scope = LocalReviewScope.Parse(scopeStr);

                _logger.LogInformation("[{ToolName}] Entry: path='{Path}', scope={Scope}",
                    ToolName, path, scope);
                var sw = Stopwatch.StartNew();

                var diff = await _reviewProvider.GetFileDiffAsync(path, scope, cancellationToken);

                var structured = new StructuredDiffResult
                {
                    PrNumber = null,
                    Files = diff.Files.Select(f => new StructuredFileChange
                    {
                        Path = f.Path,
                        ChangeType = f.ChangeType,
                        SkipReason = f.SkipReason,
                        Additions = f.Additions,
                        Deletions = f.Deletions,
                        Hunks = f.Hunks.Select(h => new StructuredHunk
                        {
                            OldStart = h.OldStart,
                            OldCount = h.OldCount,
                            NewStart = h.NewStart,
                            NewCount = h.NewCount,
                            Lines = h.Lines.Select(l => new StructuredLine
                            {
                                Op = l.Op.ToString(),
                                Text = l.Text
                            }).ToList()
                        }).ToList()
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(structured, JsonOptions);
                sw.Stop();

                _logger.LogInformation(
                    "[{ToolName}] Completed: path='{Path}', scope={Scope}, {ResponseLength} chars, {ElapsedMs}ms",
                    ToolName, path, scope, json.Length, sw.ElapsedMilliseconds);

                return CreateSuccessResult(json);
            }
            catch (LocalRepositoryNotFoundException ex)
            {
                _logger.LogWarning(ex, "[{ToolName}] Repository not found", ToolName);
                return CreateErrorResult($"Repository not found: {ex.Message}");
            }
            catch (LocalFileNotFoundException ex)
            {
                _logger.LogWarning(ex, "[{ToolName}] File not found in local changes (path='{Path}')",
                    ToolName, arguments?.GetValueOrDefault("path"));
                return CreateErrorResult($"File not found in local changes: {ex.Message}");
            }
            catch (GitCommandException ex)
            {
                _logger.LogWarning(ex, "[{ToolName}] Git command failed (path='{Path}')",
                    ToolName, arguments?.GetValueOrDefault("path"));
                return CreateErrorResult($"Git command failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{ToolName}] Error (path='{Path}')",
                    ToolName, arguments?.GetValueOrDefault("path"));
                return CreateErrorResult($"Error retrieving local file diff: {ex.Message}");
            }
        }

        private static bool TryExtractPath(
            Dictionary<string, object>? arguments,
            out string path,
            out string errorMessage)
        {
            path = string.Empty;
            errorMessage = string.Empty;

            if (arguments == null || !arguments.TryGetValue("path", out var pathObj))
            {
                errorMessage = "Missing required parameter: path";
                return false;
            }

            path = pathObj is JsonElement jsonElement
                ? jsonElement.GetString() ?? string.Empty
                : pathObj?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                errorMessage = "path parameter must not be empty";
                return false;
            }

            return true;
        }

        private static string? ExtractScope(Dictionary<string, object> arguments)
        {
            if (!arguments.TryGetValue("scope", out var scopeObj))
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
