using REBUSS.Pure.AzureDevOps.Providers.Diff;
using REBUSS.Pure.Core.Models;

namespace REBUSS.Pure.AzureDevOps.Tests.Providers.Diff;

/// <summary>
/// Focused unit tests for <see cref="FullFileRewriteDetector"/>. Cases relocated
/// from <c>AzureDevOpsDiffProviderTests</c> after the predicate was extracted from
/// the orchestrator (Step 1 refactor) — same scenarios, narrower seam.
/// </summary>
public class FullFileRewriteDetectorTests
{
    [Fact]
    public void IsFullFileRewrite_ReturnsFalse_WhenBaseContentIsNull()
    {
        Assert.False(FullFileRewriteDetector.IsFullFileRewrite(null, "content", new List<DiffHunk>()));
    }

    [Fact]
    public void IsFullFileRewrite_ReturnsFalse_WhenTargetContentIsNull()
    {
        Assert.False(FullFileRewriteDetector.IsFullFileRewrite("content", null, new List<DiffHunk>()));
    }

    [Fact]
    public void IsFullFileRewrite_ReturnsFalse_WhenBothContentsEmpty()
    {
        Assert.False(FullFileRewriteDetector.IsFullFileRewrite(string.Empty, string.Empty, new List<DiffHunk>()));
    }

    [Fact]
    public void IsFullFileRewrite_ReturnsFalse_WhenHunksAreEmpty()
    {
        Assert.False(FullFileRewriteDetector.IsFullFileRewrite("old", "new", new List<DiffHunk>()));
    }

    [Fact]
    public void IsFullFileRewrite_ReturnsFalse_WhenFilesAreTooSmall()
    {
        // Both files are below FullRewriteMinLineCount = 10 → predicate short-circuits to false.
        var hunks = new List<DiffHunk>
        {
            new()
            {
                OldStart = 1, OldCount = 3, NewStart = 1, NewCount = 3,
                Lines = new List<DiffLine>
                {
                    new() { Op = '-', Text = "a" },
                    new() { Op = '-', Text = "b" },
                    new() { Op = '-', Text = "c" },
                    new() { Op = '+', Text = "x" },
                    new() { Op = '+', Text = "y" },
                    new() { Op = '+', Text = "z" }
                }
            }
        };
        Assert.False(FullFileRewriteDetector.IsFullFileRewrite("a\nb\nc", "x\ny\nz", hunks));
    }

    [Fact]
    public void IsFullFileRewrite_ReturnsTrue_WhenNoContextLines()
    {
        // 12-line file, every line marked as deletion + addition, no context lines → full rewrite.
        var oldLines = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"old{i}"));
        var newLines = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"new{i}"));
        var hunks = new List<DiffHunk>
        {
            new()
            {
                OldStart = 1, OldCount = 12, NewStart = 1, NewCount = 12,
                Lines = Enumerable.Range(1, 12).Select(i => new DiffLine { Op = '-', Text = $"old{i}" })
                    .Concat(Enumerable.Range(1, 12).Select(i => new DiffLine { Op = '+', Text = $"new{i}" }))
                    .ToList()
            }
        };

        Assert.True(FullFileRewriteDetector.IsFullFileRewrite(oldLines, newLines, hunks));
    }

    [Fact]
    public void IsFullFileRewrite_ReturnsFalse_WhenContextLinesExist()
    {
        // 12-line file with only line 3 changed — context lines mean it's NOT a full rewrite.
        var oldLines = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"line{i}"));
        var newLines = "line1\nline2\nCHANGED\n" +
                       string.Join("\n", Enumerable.Range(4, 9).Select(i => $"line{i}"));
        var hunks = new List<DiffHunk>
        {
            new()
            {
                OldStart = 1, OldCount = 4, NewStart = 1, NewCount = 4,
                Lines = new List<DiffLine>
                {
                    new() { Op = ' ', Text = "line1" },
                    new() { Op = ' ', Text = "line2" },
                    new() { Op = '-', Text = "line3" },
                    new() { Op = '+', Text = "CHANGED" },
                    new() { Op = ' ', Text = "line4" }
                }
            }
        };

        Assert.False(FullFileRewriteDetector.IsFullFileRewrite(oldLines, newLines, hunks));
    }

    [Fact]
    public void IsFullFileRewrite_NormalizesCrlfLineEndings_TrueOnFullRewrite()
    {
        // Verify that CRLF inputs produce the same line count as LF — bug-prone area.
        var oldLines = string.Join("\r\n", Enumerable.Range(1, 12).Select(i => $"old{i}"));
        var newLines = string.Join("\r\n", Enumerable.Range(1, 12).Select(i => $"new{i}"));
        var hunks = new List<DiffHunk>
        {
            new()
            {
                OldStart = 1, OldCount = 12, NewStart = 1, NewCount = 12,
                Lines = Enumerable.Range(1, 12).Select(i => new DiffLine { Op = '-', Text = $"old{i}" })
                    .Concat(Enumerable.Range(1, 12).Select(i => new DiffLine { Op = '+', Text = $"new{i}" }))
                    .ToList()
            }
        };

        Assert.True(FullFileRewriteDetector.IsFullFileRewrite(oldLines, newLines, hunks));
    }
}
