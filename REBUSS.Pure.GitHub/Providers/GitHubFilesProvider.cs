using Microsoft.Extensions.Logging;
using REBUSS.Pure.Core.Models;
using REBUSS.Pure.Core.Shared;
using REBUSS.Pure.GitHub.Api;
using REBUSS.Pure.GitHub.Parsers;
using System.Diagnostics;

namespace REBUSS.Pure.GitHub.Providers;

/// <summary>
/// Fetches the list of changed files for a pull request, classifies each file,
/// and builds a category summary. Calls the GitHub API directly to retrieve
/// file metadata (including line counts) without fetching individual file contents.
/// </summary>
public class GitHubFilesProvider
{
    private readonly IGitHubApiClient _apiClient;
    private readonly IGitHubFileChangesParser _fileChangesParser;
    private readonly IFileClassifier _fileClassifier;
    private readonly ILogger<GitHubFilesProvider> _logger;

    public GitHubFilesProvider(
        IGitHubApiClient apiClient,
        IGitHubFileChangesParser fileChangesParser,
        IFileClassifier fileClassifier,
        ILogger<GitHubFilesProvider> logger)
    {
        _apiClient = apiClient;
        _fileChangesParser = fileChangesParser;
        _fileClassifier = fileClassifier;
        _logger = logger;
    }

    public virtual async Task<PullRequestFiles> GetFilesAsync(int prNumber, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching files for PR #{PrNumber}", prNumber);
        var sw = Stopwatch.StartNew();

        var filesJson = await _apiClient.GetPullRequestFilesAsync(prNumber, cancellationToken);
        var fileChanges = _fileChangesParser.Parse(filesJson);

        var classified = fileChanges
            .Select(f => (fileChange: f, classification: _fileClassifier.Classify(f.Path)))
            .ToList();

        var files = classified.Select(x => BuildFileInfo(x.fileChange, x.classification)).ToList();
        var summary = BuildSummary(classified.Select(x => x.classification).ToList(), files);

        sw.Stop();

        _logger.LogInformation(
            "Files for PR #{PrNumber} completed: {TotalFiles} file(s) " +
            "(source={SourceFiles}, test={TestFiles}, config={ConfigFiles}, docs={DocsFiles}, " +
            "binary={BinaryFiles}, generated={GeneratedFiles}, highPriority={HighPriority}), {ElapsedMs}ms",
            prNumber, files.Count,
            summary.SourceFiles, summary.TestFiles, summary.ConfigFiles, summary.DocsFiles,
            summary.BinaryFiles, summary.GeneratedFiles, summary.HighPriorityFiles,
            sw.ElapsedMilliseconds);

        return new PullRequestFiles { Files = files, Summary = summary };
    }

    private static PullRequestFileInfo BuildFileInfo(FileChange fileChange, FileClassification classification)
    {
        return new PullRequestFileInfo
        {
            Path = fileChange.Path.TrimStart('/'),
            Status = MapStatus(fileChange.ChangeType),
            Additions = fileChange.Additions,
            Deletions = fileChange.Deletions,
            Changes = fileChange.Additions + fileChange.Deletions,
            Extension = classification.Extension,
            IsBinary = classification.IsBinary,
            IsGenerated = classification.IsGenerated,
            IsTestFile = classification.IsTestFile,
            ReviewPriority = classification.ReviewPriority
        };
    }

    private static string MapStatus(string changeType) => changeType.ToLowerInvariant() switch
    {
        "add" => "added",
        "edit" => "modified",
        "delete" => "removed",
        "rename" => "renamed",
        _ => changeType
    };

    private static PullRequestFilesSummary BuildSummary(
        List<FileClassification> classifications, List<PullRequestFileInfo> files)
    {
        return new PullRequestFilesSummary
        {
            SourceFiles = classifications.Count(c => c.Category == FileCategory.Source),
            TestFiles = classifications.Count(c => c.Category == FileCategory.Test),
            ConfigFiles = classifications.Count(c => c.Category == FileCategory.Config),
            DocsFiles = classifications.Count(c => c.Category == FileCategory.Docs),
            BinaryFiles = classifications.Count(c => c.Category == FileCategory.Binary),
            GeneratedFiles = classifications.Count(c => c.Category == FileCategory.Generated),
            HighPriorityFiles = files.Count(f => f.ReviewPriority == "high")
        };
    }
}
