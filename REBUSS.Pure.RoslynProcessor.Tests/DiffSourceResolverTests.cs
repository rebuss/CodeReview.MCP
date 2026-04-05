using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using REBUSS.Pure.Core;
using REBUSS.Pure.RoslynProcessor;

namespace REBUSS.Pure.RoslynProcessor.Tests;

public class DiffSourceResolverTests : IDisposable
{
    private readonly IRepositoryDownloadOrchestrator _orchestrator = Substitute.For<IRepositoryDownloadOrchestrator>();
    private readonly DiffSourceResolver _resolver;
    private readonly string _tempDir;

    public DiffSourceResolverTests()
    {
        _resolver = new DiffSourceResolver(
            _orchestrator,
            NullLogger<DiffSourceResolver>.Instance);
        _tempDir = Path.Combine(Path.GetTempPath(), $"resolver-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public async Task ResolveAsync_FileExists_ReturnsPairWithBeforeAndAfter()
    {
        // Simulate ZIP extraction: _tempDir/wrapper/src/File.cs
        // ResolveRoot detects the single wrapper directory and uses it as root
        var wrapperDir = Path.Combine(_tempDir, "repo-abc123");
        var srcDir = Path.Combine(wrapperDir, "src");
        Directory.CreateDirectory(srcDir);

        var afterCode = "class C { void Foo(int x, string y) { } }";
        File.WriteAllText(Path.Combine(srcDir, "File.cs"), afterCode);

        _orchestrator.GetExtractedPathAsync(Arg.Any<CancellationToken>()).Returns(_tempDir);

        var diff = "=== src/File.cs (edit: +1 -1) ===\n@@ -1,1 +1,1 @@\n-class C { void Foo(int x) { } }\n+class C { void Foo(int x, string y) { } }";
        var result = await _resolver.ResolveAsync(diff);

        Assert.NotNull(result);
        Assert.Equal("src/File.cs", result.FilePath);
        Assert.Equal(afterCode, result.AfterCode);
        Assert.Contains("Foo(int x)", result.BeforeCode);
    }

    [Fact]
    public async Task ResolveAsync_RepoNotReady_ReturnsNull()
    {
        _orchestrator.GetExtractedPathAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var diff = "=== src/File.cs (edit: +1 -1) ===\n@@ -1,1 +1,1 @@\n-old\n+new";
        var result = await _resolver.ResolveAsync(diff);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_FileNotFound_ReturnsNull()
    {
        Directory.CreateDirectory(_tempDir);
        _orchestrator.GetExtractedPathAsync(Arg.Any<CancellationToken>()).Returns(_tempDir);

        var diff = "=== src/Missing.cs (edit: +1 -1) ===\n@@ -1,1 +1,1 @@\n-old\n+new";
        var result = await _resolver.ResolveAsync(diff);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_FileTooLarge_ReturnsNull()
    {
        Directory.CreateDirectory(_tempDir);
        var srcDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(srcDir);

        // Create a file larger than 100KB
        var largePath = Path.Combine(srcDir, "Large.cs");
        File.WriteAllText(largePath, new string('x', 101 * 1024));

        _orchestrator.GetExtractedPathAsync(Arg.Any<CancellationToken>()).Returns(_tempDir);

        var diff = "=== src/Large.cs (edit: +1 -1) ===\n@@ -1,1 +1,1 @@\n-old\n+new";
        var result = await _resolver.ResolveAsync(diff);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_TimeoutWaitingForRepo_ReturnsNull()
    {
        _orchestrator.GetExtractedPathAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync<OperationCanceledException>();

        var diff = "=== src/File.cs (edit: +1 -1) ===\n@@ -1,1 +1,1 @@\n-old\n+new";
        var result = await _resolver.ResolveAsync(diff);

        Assert.Null(result);
    }
}
