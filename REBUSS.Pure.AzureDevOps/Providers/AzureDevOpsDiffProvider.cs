using Microsoft.Extensions.Logging;
using REBUSS.Pure.AzureDevOps.Properties;
using REBUSS.Pure.Core.Exceptions;
using REBUSS.Pure.Core.Models;
using REBUSS.Pure.Core.Shared;
using System.Diagnostics;

namespace REBUSS.Pure.AzureDevOps.Providers
{
    /// <summary>
    /// Fetches structured diff content for an Azure DevOps pull request. Top-level
    /// orchestrator (~240 LOC) composing four focused collaborators — each is a
    /// DI-registered singleton living next to this class under <c>Providers/Diff/</c>:
    /// <list type="bullet">
    ///   <item><see cref="Diff.PrDataFetcher"/> — bundles the three sequential ADO API
    ///         calls (PR details, last iteration, iteration-changes) and parses each
    ///         into a <see cref="Diff.PullRequestData"/> record.</item>
    ///   <item><see cref="Diff.DiffSkipPolicy"/> — pre-filter that returns a
    ///         <see cref="FileChange.SkipReason"/> for delete/rename/binary/generated
    ///         files so the diff loop skips them without I/O.</item>
    ///   <item><see cref="Diff.DiffSourcePairFactory"/> — picks an
    ///         <see cref="Diff.IDiffSourcePair"/> implementation per file count and
    ///         <see cref="AzureDevOpsDiffOptions.ZipFallbackThreshold"/>: API path
    ///         (per-file <c>GetFileContentAtCommitAsync</c> via <see cref="Diff.ApiDiffSourcePair"/>)
    ///         for ≤ threshold; ZIP path (<see cref="Diff.ZipDiffSourcePair"/> wrapping
    ///         an <see cref="Diff.ExtractedArchiveWorkspace"/>) for &gt; threshold.</item>
    ///   <item><see cref="IStructuredDiffBuilder"/> — rebuilds hunks locally because
    ///         Azure DevOps does not return a unified-diff <c>patch</c> field.</item>
    /// </list>
    /// The orchestrator runs <b>one unified <see cref="Parallel.ForEachAsync"/> loop</b>
    /// (capped at <see cref="MaxParallelDiffRequests"/> = 15) over the kept files;
    /// the API/ZIP strategy difference is hidden behind <see cref="Diff.IDiffSourcePair.ReadAsync"/>.
    /// The ZIP-path's archive workspace is disposed via <c>await using</c> so the temp
    /// directory is always cleaned up. <see cref="Diff.FullFileRewriteDetector"/> is
    /// consulted per file inside <see cref="BuildSingleFileDiff"/> — files where every
    /// line changed (≥ 10 LOC) are skipped because the rebuilt hunks would be a wall
    /// of red+green carrying no signal.
    /// </summary>
    public class AzureDevOpsDiffProvider
    {
        private readonly Diff.PrDataFetcher _prDataFetcher;
        private readonly IStructuredDiffBuilder _diffBuilder;
        private readonly Diff.DiffSkipPolicy _skipPolicy;
        private readonly Diff.DiffSourcePairFactory _sourcePairFactory;
        private readonly ILogger<AzureDevOpsDiffProvider> _logger;

        // Constructor is internal so internal collaborator types (the Diff.* sibling types)
        // can be used in the signature without leaking through the public surface. DI
        // composition root and tests reach it via InternalsVisibleTo.
        internal AzureDevOpsDiffProvider(
            Diff.PrDataFetcher prDataFetcher,
            IStructuredDiffBuilder diffBuilder,
            Diff.DiffSkipPolicy skipPolicy,
            Diff.DiffSourcePairFactory sourcePairFactory,
            ILogger<AzureDevOpsDiffProvider> logger)
        {
            _prDataFetcher = prDataFetcher;
            _diffBuilder = diffBuilder;
            _skipPolicy = skipPolicy;
            _sourcePairFactory = sourcePairFactory;
            _logger = logger;
        }

        public virtual async Task<PullRequestDiff> GetDiffAsync(int prNumber, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Fetching diff for PR #{PrNumber}", prNumber);
                var sw = Stopwatch.StartNew();

                var data = await _prDataFetcher.FetchAsync(prNumber, cancellationToken);

                _logger.LogInformation(
                    "PR #{PrNumber}: {FileCount} file(s) changed, building diffs (base={BaseCommit}, target={TargetCommit})",
                    prNumber, data.Files.Count,
                    ShortSha(data.BaseCommit), ShortSha(data.TargetCommit));

                await BuildFileDiffsAsync(data.Files, data.BaseCommit, data.TargetCommit, cancellationToken);

                var result = BuildDiff(data.Metadata, data.Files, data.TargetCommit);
                sw.Stop();

                var totalHunks = result.Files.Sum(f => f.Hunks.Count);
                _logger.LogInformation(
                    "Diff for PR #{PrNumber} completed: {FileCount} file(s), {TotalHunks} hunk(s), {ElapsedMs}ms",
                    prNumber, data.Files.Count, totalHunks, sw.ElapsedMilliseconds);

                return result;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Pull Request #{PrNumber} not found", prNumber);
                throw new PullRequestNotFoundException(string.Format(Resources.ErrorPullRequestNotFound, prNumber), ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching diff for PR #{PrNumber}", prNumber);
                throw;
            }
        }

        public virtual async Task<PullRequestDiff> GetFileDiffAsync(int prNumber, string path, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Fetching diff for file '{Path}' in PR #{PrNumber}", path, prNumber);
                var sw = Stopwatch.StartNew();

                var data = await _prDataFetcher.FetchAsync(prNumber, cancellationToken);

                var normalizedPath = NormalizePath(path);
                var matchingFiles = data.Files
                    .Where(f => NormalizePath(f.Path).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingFiles.Count == 0)
                {
                    _logger.LogWarning("File '{Path}' not found in PR #{PrNumber}", path, prNumber);
                    throw new FileNotFoundInPullRequestException(
                            string.Format(Resources.ErrorFileNotFoundInPullRequest, path, prNumber));
                }

                await BuildFileDiffsAsync(matchingFiles, data.BaseCommit, data.TargetCommit, cancellationToken);

                var result = BuildDiff(data.Metadata, matchingFiles, data.TargetCommit);
                sw.Stop();

                var totalHunks = result.Files.Sum(f => f.Hunks.Count);
                _logger.LogInformation(
                    "File diff for '{Path}' in PR #{PrNumber} completed: {TotalHunks} hunk(s), {ElapsedMs}ms",
                    path, prNumber, totalHunks, sw.ElapsedMilliseconds);

                return result;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Pull Request #{PrNumber} not found", prNumber);
                throw new PullRequestNotFoundException(string.Format(Resources.ErrorPullRequestNotFound, prNumber), ex);
            }
            catch (FileNotFoundInPullRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching diff for file '{Path}' in PR #{PrNumber}", path, prNumber);
                throw;
            }
        }

        private static string NormalizePath(string path) => path.TrimStart('/');

        private static string ShortSha(string sha) => sha.Length > 7 ? sha[..7] : sha;

        internal const int MaxParallelDiffRequests = 15;

        private async Task BuildFileDiffsAsync(
            List<FileChange> files,
            string baseCommit,
            string targetCommit,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(baseCommit) || string.IsNullOrEmpty(targetCommit))
            {
                foreach (var file in files)
                {
                    _logger.LogDebug(
                        "Skipping diff for '{FilePath}': commit SHAs not resolved (base={BaseCommit}, target={TargetCommit})",
                        file.Path, baseCommit ?? "<null>", targetCommit ?? "<null>");
                }
                return;
            }

            // Pre-filter skippable files synchronously (no I/O needed)
            foreach (var file in files)
            {
                var skipReason = _skipPolicy.GetSkipReason(file);
                if (skipReason is not null)
                {
                    file.SkipReason = skipReason;
                    _logger.LogDebug(
                        "Skipping diff for '{FilePath}': {SkipReason}",
                        file.Path, skipReason);
                }
            }

            var filesToDiff = files.Where(f => f.SkipReason is null).ToList();
            if (filesToDiff.Count == 0)
                return;

            await using var sourcePair = await _sourcePairFactory.CreateAsync(
                filesToDiff.Count, baseCommit, targetCommit, cancellationToken);

            // Thread-safety: each FileChange is a distinct instance — the lambda
            // mutates only its own file (Hunks, SkipReason, Additions, Deletions).
            await Parallel.ForEachAsync(
                filesToDiff,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxParallelDiffRequests,
                    CancellationToken = cancellationToken
                },
                async (file, ct) =>
                {
                    var fileSw = Stopwatch.StartNew();

                    var (baseContent, targetContent) = await sourcePair.ReadAsync(file.Path, ct);
                    BuildSingleFileDiff(file, baseContent, targetContent);

                    fileSw.Stop();

                    _logger.LogDebug(
                        "Built diff for '{FilePath}' ({ChangeType}): {HunkCount} hunk(s), {ElapsedMs}ms",
                        file.Path, file.ChangeType, file.Hunks.Count, fileSw.ElapsedMilliseconds);
                });
        }

        private void BuildSingleFileDiff(FileChange file, string? baseContent, string? targetContent)
        {
            file.Hunks = _diffBuilder.Build(file.Path, baseContent, targetContent);

            if (Diff.FullFileRewriteDetector.IsFullFileRewrite(baseContent, targetContent, file.Hunks))
            {
                file.SkipReason = Resources.SkipReasonFullFileRewrite;
                file.Hunks = new List<DiffHunk>();
                _logger.LogDebug(
                    "Replaced diff for '{FilePath}': detected full file rewrite",
                    file.Path);
            }
            else
            {
                file.Additions = file.Hunks.SelectMany(h => h.Lines).Count(l => l.Op == '+');
                file.Deletions = file.Hunks.SelectMany(h => h.Lines).Count(l => l.Op == '-');
            }
        }

        private static PullRequestDiff BuildDiff(
            PullRequestMetadata metadata,
            List<FileChange> files,
            string? sourceCommitId)
        {
            return new PullRequestDiff
            {
                Title = metadata.Title,
                Status = metadata.Status,
                SourceBranch = metadata.SourceBranch,
                TargetBranch = metadata.TargetBranch,
                SourceRefName = metadata.SourceRefName,
                TargetRefName = metadata.TargetRefName,
                Files = files,
                LastSourceCommitId = sourceCommitId
            };
        }
    }
}
