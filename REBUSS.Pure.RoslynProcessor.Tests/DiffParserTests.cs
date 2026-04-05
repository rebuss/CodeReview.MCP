using REBUSS.Pure.RoslynProcessor;

namespace REBUSS.Pure.RoslynProcessor.Tests;

public class DiffParserTests
{
    [Fact]
    public void TryParseFilePath_ValidDiff_ReturnsPath()
    {
        var diff = "=== src/Services/MyService.cs (edit: +5 -3) ===\n@@ -1,3 +1,5 @@\n+new line\n";
        Assert.Equal("src/Services/MyService.cs", DiffParser.TryParseFilePath(diff));
    }

    [Fact]
    public void TryParseFilePath_SkippedFile_ReturnsPath()
    {
        var diff = "=== src/File.cs (edit: skipped) ===\nReason: full file rewrite";
        Assert.Equal("src/File.cs", DiffParser.TryParseFilePath(diff));
    }

    [Fact]
    public void TryParseFilePath_InvalidDiff_ReturnsNull()
    {
        Assert.Null(DiffParser.TryParseFilePath("not a diff"));
    }

    [Fact]
    public void TryParseFilePath_EmptyString_ReturnsNull()
    {
        Assert.Null(DiffParser.TryParseFilePath(""));
    }

    [Fact]
    public void ParseHunks_SingleHunk_ReturnsCorrectRange()
    {
        var diff = "=== src/A.cs (edit: +1 -1) ===\n@@ -10,3 +10,3 @@\n-old\n+new\n";
        var hunks = DiffParser.ParseHunks(diff);

        Assert.Single(hunks);
        Assert.Equal(10, hunks[0].OldStart);
        Assert.Equal(3, hunks[0].OldCount);
        Assert.Equal(10, hunks[0].NewStart);
        Assert.Equal(3, hunks[0].NewCount);
    }

    [Fact]
    public void ParseHunks_MultipleHunks_ReturnsAll()
    {
        var diff = "=== src/A.cs (edit: +2 -2) ===\n@@ -5,2 +5,2 @@\n-a\n+b\n@@ -20,3 +20,3 @@\n-c\n+d\n";
        var hunks = DiffParser.ParseHunks(diff);

        Assert.Equal(2, hunks.Count);
        Assert.Equal(5, hunks[0].OldStart);
        Assert.Equal(20, hunks[1].OldStart);
    }

    [Fact]
    public void RebuildDiffWithContext_NoneDecision_NoChange()
    {
        var diff = "=== src/A.cs (edit: +1 -1) ===\n@@ -5,1 +5,1 @@\n-old\n+new";
        var sourceLines = Enumerable.Range(1, 20).Select(i => $"line{i}").ToArray();
        var hunks = DiffParser.ParseHunks(diff);

        var result = DiffParser.RebuildDiffWithContext(diff, sourceLines, hunks, ContextDecision.None);
        Assert.Equal(diff, result);
    }

    [Fact]
    public void RebuildDiffWithContext_MinimalDecision_Adds3Lines()
    {
        var diff = "=== src/A.cs (edit: +1 -1) ===\n@@ -10,1 +10,1 @@\n-old\n+new";
        var sourceLines = Enumerable.Range(1, 20).Select(i => $"line{i}").ToArray();
        var hunks = DiffParser.ParseHunks(diff);

        var result = DiffParser.RebuildDiffWithContext(diff, sourceLines, hunks, ContextDecision.Minimal);

        // Should contain context lines (space-prefixed)
        Assert.Contains(" line", result);
        // Should contain the original change
        Assert.Contains("-old", result);
        Assert.Contains("+new", result);
    }
}
