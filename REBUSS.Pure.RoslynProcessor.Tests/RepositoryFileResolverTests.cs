using REBUSS.Pure.RoslynProcessor;

namespace REBUSS.Pure.RoslynProcessor.Tests;

public class RepositoryFileResolverTests : IDisposable
{
    private readonly string _tempDir;

    public RepositoryFileResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"resolver-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void ResolveRoot_NestedWrapper_ReturnsInnerDir()
    {
        var inner = Path.Combine(_tempDir, "repo-abc123");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "file.cs"), "content");

        var root = RepositoryFileResolver.ResolveRoot(_tempDir);
        Assert.Equal(inner, root);
    }

    [Fact]
    public void ResolveRoot_FlatStructure_ReturnsOriginalPath()
    {
        File.WriteAllText(Path.Combine(_tempDir, "file.cs"), "content");
        Directory.CreateDirectory(Path.Combine(_tempDir, "src"));

        var root = RepositoryFileResolver.ResolveRoot(_tempDir);
        Assert.Equal(_tempDir, root);
    }

    [Fact]
    public void ResolvePath_FileExists_ReturnsFullPath()
    {
        var srcDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(srcDir);
        var filePath = Path.Combine(srcDir, "File.cs");
        File.WriteAllText(filePath, "class C {}");

        var result = RepositoryFileResolver.ResolvePath(_tempDir, "src/File.cs");
        Assert.NotNull(result);
        Assert.True(File.Exists(result));
    }

    [Fact]
    public void ResolvePath_FileNotFound_ReturnsNull()
    {
        var result = RepositoryFileResolver.ResolvePath(_tempDir, "nonexistent/File.cs");
        Assert.Null(result);
    }
}
