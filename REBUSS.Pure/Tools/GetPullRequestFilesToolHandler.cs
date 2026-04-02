using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using REBUSS.Pure.Core;
using REBUSS.Pure.Core.Exceptions;
using REBUSS.Pure.Core.Models;
using REBUSS.Pure.Core.Models.Pagination;
using REBUSS.Pure.Core.Models.ResponsePacking;
using REBUSS.Pure.Core.Shared;
using REBUSS.Pure.Properties;
using REBUSS.Pure.Services.Pagination;
using REBUSS.Pure.Tools.Models;
using REBUSS.Pure.Tools.Shared;

namespace REBUSS.Pure.Tools
{
    /// <summary>
    /// Handles the execution of the get_pr_files MCP tool.
    /// Validates input, delegates to <see cref="IPullRequestFilesProvider"/>,
    /// and formats the result as a structured JSON response.
    /// Integrates with response packing (F003) and deterministic pagination (F004).
    /// </summary>
    [McpServerToolType]
    public class GetPullRequestFilesToolHandler
    {
        private readonly IPullRequestDataProvider _filesProvider;
        private readonly IResponsePacker _packer;
        private readonly IContextBudgetResolver _budgetResolver;
        private readonly ITokenEstimator _tokenEstimator;
        private readonly IFileClassifier _fileClassifier;
        private readonly IPageAllocator _pageAllocator;
        private readonly IPageReferenceCodec _pageReferenceCodec;
        private readonly ILogger<GetPullRequestFilesToolHandler> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public GetPullRequestFilesToolHandler(
            IPullRequestDataProvider filesProvider,
            IResponsePacker packer,
            IContextBudgetResolver budgetResolver,
            ITokenEstimator tokenEstimator,
            IFileClassifier fileClassifier,
            IPageAllocator pageAllocator,
            IPageReferenceCodec pageReferenceCodec,
            ILogger<GetPullRequestFilesToolHandler> logger)
        {
            _filesProvider = filesProvider;
            _packer = packer;
            _budgetResolver = budgetResolver;
            _tokenEstimator = tokenEstimator;
            _fileClassifier = fileClassifier;
            _pageAllocator = pageAllocator;
            _pageReferenceCodec = pageReferenceCodec;
            _logger = logger;
        }

        [McpServerTool(Name = "get_pr_files"), Description(
            "Retrieves structured information about all files changed in a specific Pull Request. " +
            "Returns per-file metadata (status, additions, deletions, extension, " +
            "binary/generated/test flags, review priority) and an aggregated summary by category.")]
        public async Task<string> ExecuteAsync(
            [Description("The Pull Request number/ID to retrieve the file list for")] int? prNumber = null,
            [Description("Optional model name (e.g. 'Claude Sonnet') to resolve context window size")] string? modelName = null,
            [Description("Optional explicit context window size in tokens")] int? maxTokens = null,
            [Description("Opaque page reference from a previous response. Encodes all context needed to re-derive the page.")] string? pageReference = null,
            [Description("Page number for direct access (requires original params + budget)")] int? pageNumber = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var mutualExclError = PaginationOrchestrator.ValidateInputs(pageReference, pageNumber);
                if (mutualExclError != null)
                    throw new McpException(mutualExclError);

                if (prNumber != null && prNumber <= 0)
                    throw new McpException(Resources.ErrorPrNumberMustBePositive);

                if (prNumber == null && pageReference == null)
                    throw new McpException(Resources.ErrorMissingRequiredPrNumber);

                var hasExplicitBudget = modelName != null || maxTokens != null;

                _logger.LogInformation(Resources.LogGetPrFilesEntry, prNumber);
                var sw = Stopwatch.StartNew();

                var budget = _budgetResolver.Resolve(maxTokens, modelName);

                var resolution = PaginationOrchestrator.ResolvePage(
                    pageReference, pageNumber, _pageReferenceCodec, budget.SafeBudgetTokens, hasExplicitBudget);

                if (!resolution.IsSuccess)
                    throw new McpException(resolution.ErrorMessage!);

                var effectivePrNumber = prNumber;
                if (resolution.DecodedParams != null)
                {
                    if (resolution.DecodedParams.Value.TryGetProperty("prNumber", out var decodedPr))
                        effectivePrNumber = decodedPr.GetInt32();

                    if (prNumber != null)
                    {
                        var prJsonElement = JsonSerializer.SerializeToElement(prNumber);
                        var paramError = PaginationOrchestrator.ValidateParameterMatch(
                            resolution.DecodedParams, "prNumber", prJsonElement);
                        if (paramError != null)
                            throw new McpException(paramError);
                    }
                }

                if (effectivePrNumber == null || effectivePrNumber <= 0)
                    throw new McpException(Resources.ErrorMissingRequiredPrNumber);

                var effectiveBudget = resolution.ResolvedBudget;

                Task<FullPullRequestMetadata>? metadataTask = null;
                var isPageRefMode = pageReference != null;
                if (isPageRefMode && resolution.Fingerprint != null)
                    metadataTask = _filesProvider.GetMetadataAsync(effectivePrNumber.Value, cancellationToken);

                var prFiles = await _filesProvider.GetFilesAsync(effectivePrNumber.Value, cancellationToken);

                if (!hasExplicitBudget && pageReference == null)
                {
                    var result = BuildPackedResult(effectivePrNumber.Value, prFiles, budget.SafeBudgetTokens);
                    var json = JsonSerializer.Serialize(result, JsonOptions);
                    sw.Stop();
                    _logger.LogInformation(Resources.LogGetPrFilesCompletedF003,
                        effectivePrNumber, prFiles.Files.Count, sw.ElapsedMilliseconds);
                    return json;
                }

                // Feature 004 path
                var fileItems = BuildFileItems(prFiles);
                var candidates = ToolHandlerHelpers.BuildCandidates(
                    fileItems, effectiveBudget, _tokenEstimator, _fileClassifier,
                    fi => fi.Path, fi => fi.Additions + fi.Deletions);
                var sortedCandidates = ToolHandlerHelpers.SortCandidates(candidates);

                PageAllocation allocation;
                try
                {
                    allocation = _pageAllocator.Allocate(sortedCandidates, effectiveBudget);
                }
                catch (BudgetTooSmallException ex)
                {
                    throw new McpException(ex.Message);
                }

                var requestedPage = resolution.PageNumber;
                if (requestedPage < 1 || requestedPage > allocation.TotalPages)
                    throw new McpException(string.Format(Resources.ErrorPageNumberOutOfRange, requestedPage, allocation.TotalPages));

                var pageSlice = allocation.Pages[requestedPage - 1];

                var packedFiles = ToolHandlerHelpers.ExtractPageFiles(
                    fileItems, sortedCandidates, pageSlice, fi => fi.Path);

                StalenessWarningResult? staleness = null;
                if (metadataTask != null)
                {
                    var metadata = await metadataTask;
                    staleness = PaginationOrchestrator.CheckStaleness(
                        resolution.Fingerprint, metadata.LastMergeSourceCommitId, isPageRefMode);
                }

                string? currentFingerprint = resolution.Fingerprint;
                if (currentFingerprint == null)
                {
                    try
                    {
                        var meta = await _filesProvider.GetMetadataAsync(effectivePrNumber.Value, cancellationToken);
                        currentFingerprint = meta.LastMergeSourceCommitId;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, Resources.LogGetPrFilesMetadataFingerprintFailed);
                    }
                }

                var requestParams = JsonSerializer.SerializeToElement(new { prNumber = effectivePrNumber });

                var paginationMeta = PaginationOrchestrator.BuildPaginationMetadata(
                    allocation, requestedPage, _pageReferenceCodec,
                    "get_pr_files", requestParams, effectiveBudget, currentFingerprint);

                var manifestResult = ToolHandlerHelpers.BuildPageManifest(sortedCandidates, pageSlice, allocation, effectiveBudget);

                var paginatedResult = new PullRequestFilesResult
                {
                    PrNumber = effectivePrNumber.Value,
                    TotalFiles = prFiles.Files.Count,
                    Files = packedFiles,
                    Summary = new PullRequestFilesSummaryResult
                    {
                        SourceFiles = prFiles.Summary.SourceFiles,
                        TestFiles = prFiles.Summary.TestFiles,
                        ConfigFiles = prFiles.Summary.ConfigFiles,
                        DocsFiles = prFiles.Summary.DocsFiles,
                        BinaryFiles = prFiles.Summary.BinaryFiles,
                        GeneratedFiles = prFiles.Summary.GeneratedFiles,
                        HighPriorityFiles = prFiles.Summary.HighPriorityFiles
                    },
                    Manifest = manifestResult,
                    Pagination = paginationMeta,
                    StalenessWarning = staleness
                };

                var jsonResult = JsonSerializer.Serialize(paginatedResult, JsonOptions);
                sw.Stop();
                _logger.LogInformation(
                    Resources.LogGetPrFilesCompletedF004,
                    effectivePrNumber, requestedPage, allocation.TotalPages, sw.ElapsedMilliseconds);

                return jsonResult;
            }
            catch (PullRequestNotFoundException ex)
            {
                _logger.LogWarning(ex, Resources.LogGetPrFilesPrNotFound);
                throw new McpException(string.Format(Resources.ErrorPullRequestNotFound, ex.Message));
            }
            catch (McpException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, Resources.LogGetPrFilesError);
                throw new McpException(string.Format(Resources.ErrorRetrievingPrFiles, ex.Message));
            }
        }

        // --- Build helpers ---

        private static List<PullRequestFileItem> BuildFileItems(PullRequestFiles prFiles)
        {
            return prFiles.Files.Select(f => new PullRequestFileItem
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
            }).ToList();
        }

        // --- F003 result builder ---

        private PullRequestFilesResult BuildPackedResult(int prNumber, PullRequestFiles prFiles, int safeBudgetTokens)
        {
            var fileItems = BuildFileItems(prFiles);
            var candidates = ToolHandlerHelpers.BuildCandidates(
                fileItems, safeBudgetTokens, _tokenEstimator, _fileClassifier,
                fi => fi.Path, fi => fi.Additions + fi.Deletions);
            var decision = _packer.Pack(candidates, safeBudgetTokens);

            var packedFiles = new List<PullRequestFileItem>();
            for (var i = 0; i < decision.Items.Count; i++)
            {
                if (decision.Items[i].Status != PackingItemStatus.Deferred)
                    packedFiles.Add(fileItems[i]);
            }

            return new PullRequestFilesResult
            {
                PrNumber = prNumber,
                TotalFiles = prFiles.Files.Count,
                Files = packedFiles,
                Summary = new PullRequestFilesSummaryResult
                {
                    SourceFiles = prFiles.Summary.SourceFiles,
                    TestFiles = prFiles.Summary.TestFiles,
                    ConfigFiles = prFiles.Summary.ConfigFiles,
                    DocsFiles = prFiles.Summary.DocsFiles,
                    BinaryFiles = prFiles.Summary.BinaryFiles,
                    GeneratedFiles = prFiles.Summary.GeneratedFiles,
                    HighPriorityFiles = prFiles.Summary.HighPriorityFiles
                },
                Manifest = ContentManifestResult.From(decision.Manifest)
            };
        }
    }
}
