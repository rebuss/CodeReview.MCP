using REBUSS.Pure.Core.Shared;

namespace REBUSS.Pure.Core.Tests.Shared;

public class UnifiedPatchParserTests
{
    // --- ParseHunks (single-file, hunk-only — GitHub's per-file patch field) -------

    [Fact]
    public void ParseHunks_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(UnifiedPatchParser.ParseHunks(null));
        Assert.Empty(UnifiedPatchParser.ParseHunks(""));
    }

    [Fact]
    public void ParseHunks_SingleHunk_ReturnsLines()
    {
        const string patch =
            "@@ -1,3 +1,4 @@\n" +
            " context\n" +
            "-old\n" +
            "+new\n" +
            "+extra\n" +
            " trailing";

        var hunks = UnifiedPatchParser.ParseHunks(patch);

        Assert.Single(hunks);
        Assert.Equal(5, hunks[0].Lines.Count);
        Assert.Equal('-', hunks[0].Lines[1].Op);
        Assert.Equal("old", hunks[0].Lines[1].Text);
        Assert.Equal('+', hunks[0].Lines[2].Op);
    }

    [Fact]
    public void ParseHunks_BackslashNoNewline_Ignored()
    {
        const string patch =
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "\\ No newline at end of file\n" +
            "+new";

        var hunks = UnifiedPatchParser.ParseHunks(patch);

        Assert.Equal(2, hunks[0].Lines.Count);
        Assert.DoesNotContain(hunks[0].Lines, l => l.Text.Contains("No newline"));
    }

    // --- ParseMultiFile (full git diff -p output) ---------------------------------

    [Fact]
    public void ParseMultiFile_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(UnifiedPatchParser.ParseMultiFile(null));
        Assert.Empty(UnifiedPatchParser.ParseMultiFile(""));
    }

    [Fact]
    public void ParseMultiFile_SingleModifiedFile_ParsesPathAndHunk()
    {
        const string diff =
            "diff --git a/src/Foo.cs b/src/Foo.cs\n" +
            "index abc1234..def5678 100644\n" +
            "--- a/src/Foo.cs\n" +
            "+++ b/src/Foo.cs\n" +
            "@@ -10,3 +10,4 @@\n" +
            " context\n" +
            "-old line\n" +
            "+new line\n" +
            "+added line";

        var files = UnifiedPatchParser.ParseMultiFile(diff);

        Assert.Single(files);
        Assert.Equal("src/Foo.cs", files[0].Path);
        Assert.Equal("edit", files[0].ChangeType);
        Assert.Single(files[0].Hunks);
        Assert.Equal(2, files[0].Additions);
        Assert.Equal(1, files[0].Deletions);
    }

    [Fact]
    public void ParseMultiFile_NewFile_SetsChangeTypeAdd()
    {
        const string diff =
            "diff --git a/src/New.cs b/src/New.cs\n" +
            "new file mode 100644\n" +
            "index 0000000..abc1234\n" +
            "--- /dev/null\n" +
            "+++ b/src/New.cs\n" +
            "@@ -0,0 +1,2 @@\n" +
            "+line 1\n" +
            "+line 2";

        var files = UnifiedPatchParser.ParseMultiFile(diff);

        Assert.Single(files);
        Assert.Equal("src/New.cs", files[0].Path);
        Assert.Equal("add", files[0].ChangeType);
        Assert.Equal(2, files[0].Additions);
        Assert.Equal(0, files[0].Deletions);
    }

    [Fact]
    public void ParseMultiFile_DeletedFile_SetsChangeTypeDelete()
    {
        const string diff =
            "diff --git a/src/Old.cs b/src/Old.cs\n" +
            "deleted file mode 100644\n" +
            "index abc1234..0000000\n" +
            "--- a/src/Old.cs\n" +
            "+++ /dev/null\n" +
            "@@ -1,2 +0,0 @@\n" +
            "-line 1\n" +
            "-line 2";

        var files = UnifiedPatchParser.ParseMultiFile(diff);

        Assert.Single(files);
        Assert.Equal("src/Old.cs", files[0].Path);
        Assert.Equal("delete", files[0].ChangeType);
        Assert.Equal(0, files[0].Additions);
        Assert.Equal(2, files[0].Deletions);
    }

    [Fact]
    public void ParseMultiFile_RenamedFile_SetsChangeTypeAndUsesNewPath()
    {
        const string diff =
            "diff --git a/src/Old.cs b/src/New.cs\n" +
            "similarity index 95%\n" +
            "rename from src/Old.cs\n" +
            "rename to src/New.cs\n" +
            "index abc1234..def5678 100644\n" +
            "--- a/src/Old.cs\n" +
            "+++ b/src/New.cs\n" +
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "+new";

        var files = UnifiedPatchParser.ParseMultiFile(diff);

        Assert.Single(files);
        Assert.Equal("src/New.cs", files[0].Path);
        Assert.Equal("rename", files[0].ChangeType);
    }

    [Fact]
    public void ParseMultiFile_BinaryFile_SetsSkipReason()
    {
        const string diff =
            "diff --git a/img.png b/img.png\n" +
            "index abc..def 100644\n" +
            "Binary files a/img.png and b/img.png differ";

        var files = UnifiedPatchParser.ParseMultiFile(diff);

        Assert.Single(files);
        Assert.Equal("img.png", files[0].Path);
        Assert.Equal("binary file", files[0].SkipReason);
        Assert.Empty(files[0].Hunks);
    }

    [Fact]
    public void ParseMultiFile_MultipleFiles_AllParsed()
    {
        const string diff =
            "diff --git a/a.cs b/a.cs\n" +
            "--- a/a.cs\n" +
            "+++ b/a.cs\n" +
            "@@ -1 +1 @@\n" +
            "-x\n" +
            "+y\n" +
            "diff --git a/b.cs b/b.cs\n" +
            "--- a/b.cs\n" +
            "+++ b/b.cs\n" +
            "@@ -5 +5 @@\n" +
            "-foo\n" +
            "+bar";

        var files = UnifiedPatchParser.ParseMultiFile(diff);

        Assert.Equal(2, files.Count);
        Assert.Equal("a.cs", files[0].Path);
        Assert.Equal("b.cs", files[1].Path);
        Assert.Single(files[0].Hunks);
        Assert.Single(files[1].Hunks);
    }

    [Fact]
    public void ParseMultiFile_PathWithSlashes_PreservedFromBSide()
    {
        const string diff =
            "diff --git a/.github/CodebaseUnderstanding.md b/.github/CodebaseUnderstanding.md\n" +
            "--- a/.github/CodebaseUnderstanding.md\n" +
            "+++ b/.github/CodebaseUnderstanding.md\n" +
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "+new";

        var files = UnifiedPatchParser.ParseMultiFile(diff);

        Assert.Single(files);
        Assert.Equal(".github/CodebaseUnderstanding.md", files[0].Path);
    }

    [Fact]
    public void ParseMultiFile_CrlfLineEndings_HandledLikeLf()
    {
        const string diff =
            "diff --git a/x.cs b/x.cs\r\n" +
            "--- a/x.cs\r\n" +
            "+++ b/x.cs\r\n" +
            "@@ -1 +1 @@\r\n" +
            "-old\r\n" +
            "+new";

        var files = UnifiedPatchParser.ParseMultiFile(diff);

        Assert.Single(files);
        Assert.Equal(1, files[0].Additions);
        Assert.Equal(1, files[0].Deletions);
    }

    [Fact]
    public void ParseMultiFile_AdditionsAndDeletionsAccumulateAcrossHunks()
    {
        const string diff =
            "diff --git a/x.cs b/x.cs\n" +
            "--- a/x.cs\n" +
            "+++ b/x.cs\n" +
            "@@ -1,2 +1,3 @@\n" +
            " ctx\n" +
            "-a\n" +
            "+b\n" +
            "+c\n" +
            "@@ -10 +11 @@\n" +
            "-d\n" +
            "+e";

        var files = UnifiedPatchParser.ParseMultiFile(diff);

        Assert.Single(files);
        Assert.Equal(3, files[0].Additions);
        Assert.Equal(2, files[0].Deletions);
        Assert.Equal(2, files[0].Hunks.Count);
    }
}
