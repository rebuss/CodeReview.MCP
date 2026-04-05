using Microsoft.Extensions.Logging;
using REBUSS.Pure.Core;
using REBUSS.Pure.Core.Shared;

namespace REBUSS.Pure.RoslynProcessor;

/// <summary>
/// Roslyn-based implementation of <see cref="ICodeProcessor"/>.
/// Enriches C# file diffs with before/after context lines using Roslyn syntax analysis
/// to determine context level (None/Minimal/Full).
/// </summary>
public class RoslynCodeProcessor : ICodeProcessor
{
    private const int MaxFileSizeBytes = 100 * 1024; // 100KB
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(3);

    private readonly IRepositoryDownloadOrchestrator _orchestrator;
    private readonly ILogger<RoslynCodeProcessor> _logger;

    public RoslynCodeProcessor(
        IRepositoryDownloadOrchestrator orchestrator,
        ILogger<RoslynCodeProcessor> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task<string> AddBeforeAfterContext(string diff, CancellationToken ct = default)
    {
        try
        {
            // 1. Parse file path from diff header
            var filePath = DiffParser.TryParseFilePath(diff);
            if (filePath == null)
                return diff;

            // 2. Skip non-C# files
            if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return diff;

            // 3. Skip skipped files
            if (diff.Contains("skipped) ==="))
                return diff;

            // 4. Wait for repository download with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(DownloadTimeout);

            string? repoPath;
            try
            {
                repoPath = await _orchestrator.GetExtractedPathAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogDebug("Repository download timed out for {FilePath}, returning diff unchanged", filePath);
                return diff;
            }

            if (repoPath == null)
                return diff;

            // 5. Resolve file in extracted repository
            var repoRoot = RepositoryFileResolver.ResolveRoot(repoPath);
            var resolvedPath = RepositoryFileResolver.ResolvePath(repoRoot, filePath);
            if (resolvedPath == null)
            {
                _logger.LogDebug("File {FilePath} not found in extracted repository", filePath);
                return diff;
            }

            // 6. Read source file (skip if too large)
            var fileInfo = new FileInfo(resolvedPath);
            if (fileInfo.Length > MaxFileSizeBytes)
            {
                _logger.LogDebug("File {FilePath} exceeds size limit ({Size} bytes), skipping Roslyn analysis", filePath, fileInfo.Length);
                return diff;
            }

            var sourceContent = await File.ReadAllTextAsync(resolvedPath, ct);
            var sourceLines = sourceContent.Replace("\r\n", "\n").Split('\n');

            // 7. Parse hunks
            var hunks = DiffParser.ParseHunks(diff);
            if (hunks.Count == 0)
                return diff;

            // 8. For each hunk, reconstruct before/after code and analyze
            // The source file represents the "after" state (PR head commit).
            // We use the overall file change to determine context level.
            var beforeCode = ReconstructBeforeCode(sourceLines, hunks, diff);
            var decision = BeforeAfterAnalyzer.Analyze(beforeCode, sourceContent);

            // 9. Rebuild diff with context
            return DiffParser.RebuildDiffWithContext(diff, sourceLines, hunks, decision);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during Roslyn before/after context analysis, returning diff unchanged");
            return diff;
        }
    }

    /// <summary>
    /// Reconstructs the "before" code by reversing the diff changes on the source file.
    /// The source file is the "after" state. We remove '+' lines and restore '-' lines.
    /// </summary>
    private static string ReconstructBeforeCode(string[] sourceLines, List<ParsedHunk> hunks, string diff)
    {
        // Simple approach: extract the deleted lines from the diff and the context,
        // build a rough "before" version of the changed region.
        var lines = diff.Replace("\r\n", "\n").Split('\n');
        var beforeLines = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("===") || line.StartsWith("@@"))
                continue;

            if (line.StartsWith("+"))
                continue; // Added in "after", not in "before"

            if (line.StartsWith("-"))
                beforeLines.Add(line[1..]); // Removed line was in "before"
            else if (line.Length > 0 && line[0] == ' ')
                beforeLines.Add(line[1..]); // Context line exists in both
        }

        return string.Join("\n", beforeLines);
    }
}
