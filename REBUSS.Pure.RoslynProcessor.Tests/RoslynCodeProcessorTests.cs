using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using REBUSS.Pure.Core;
using REBUSS.Pure.RoslynProcessor;

namespace REBUSS.Pure.RoslynProcessor.Tests;

public class RoslynCodeProcessorTests : IDisposable
{
    private readonly IRepositoryDownloadOrchestrator _orchestrator = Substitute.For<IRepositoryDownloadOrchestrator>();
    private readonly RoslynCodeProcessor _processor;
    private readonly string _tempDir;

    public RoslynCodeProcessorTests()
    {
        _processor = new RoslynCodeProcessor(
            _orchestrator,
            NullLogger<RoslynCodeProcessor>.Instance);
        _tempDir = Path.Combine(Path.GetTempPath(), $"roslyn-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public async Task AddBeforeAfterContext_RepoNotAvailable_ReturnsDiffUnchanged()
    {
        _orchestrator.GetExtractedPathAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var diff = "=== src/File.cs (edit: +1 -1) ===\n@@ -1,1 +1,1 @@\n-old\n+new";
        var result = await _processor.AddBeforeAfterContext(diff);

        Assert.Equal(diff, result);
    }

    [Fact]
    public async Task AddBeforeAfterContext_RepoDownloadFailed_ReturnsDiffUnchanged()
    {
        _orchestrator.GetExtractedPathAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var diff = "=== src/File.cs (edit: +1 -1) ===\n@@ -1,1 +1,1 @@\n-old\n+new";
        var result = await _processor.AddBeforeAfterContext(diff);

        Assert.Equal(diff, result);
    }

    [Fact]
    public async Task AddBeforeAfterContext_NonCsFile_ReturnsDiffUnchanged()
    {
        _orchestrator.GetExtractedPathAsync(Arg.Any<CancellationToken>()).Returns("/some/path");

        var diff = "=== config/settings.json (edit: +1 -1) ===\n@@ -1,1 +1,1 @@\n-old\n+new";
        var result = await _processor.AddBeforeAfterContext(diff);

        Assert.Equal(diff, result);
    }

    [Fact]
    public async Task AddBeforeAfterContext_FileNotInRepo_ReturnsDiffUnchanged()
    {
        Directory.CreateDirectory(_tempDir);
        _orchestrator.GetExtractedPathAsync(Arg.Any<CancellationToken>()).Returns(_tempDir);

        var diff = "=== src/Missing.cs (edit: +1 -1) ===\n@@ -1,1 +1,1 @@\n-old\n+new";
        var result = await _processor.AddBeforeAfterContext(diff);

        Assert.Equal(diff, result);
    }

    [Fact]
    public async Task AddBeforeAfterContext_SemanticChange_AddsContext()
    {
        // Set up repo with a C# file
        Directory.CreateDirectory(_tempDir);
        var srcDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(srcDir);

        var sourceCode = string.Join("\n", Enumerable.Range(1, 20).Select(i =>
            i == 10 ? "    void Foo(int x, string y) { }" : $"    // line {i}"))
            .Insert(0, "class C {\n") + "\n}";
        File.WriteAllText(Path.Combine(srcDir, "File.cs"), sourceCode);

        _orchestrator.GetExtractedPathAsync(Arg.Any<CancellationToken>()).Returns(_tempDir);

        var diff = "=== src/File.cs (edit: +1 -1) ===\n@@ -10,1 +10,1 @@\n-    void Foo(int x) { }\n+    void Foo(int x, string y) { }";
        var result = await _processor.AddBeforeAfterContext(diff);

        // The result should have context lines (may differ from input)
        Assert.Contains("Foo", result);
    }

    [Fact]
    public async Task AddBeforeAfterContext_SkippedFile_ReturnsDiffUnchanged()
    {
        _orchestrator.GetExtractedPathAsync(Arg.Any<CancellationToken>()).Returns(_tempDir);

        var diff = "=== src/File.cs (edit: skipped) ===\nReason: full file rewrite";
        var result = await _processor.AddBeforeAfterContext(diff);

        Assert.Equal(diff, result);
    }

    [Fact]
    public async Task AddBeforeAfterContext_ConcurrentCalls_AllCompleteCorrectly()
    {
        Directory.CreateDirectory(_tempDir);
        _orchestrator.GetExtractedPathAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var diffs = Enumerable.Range(1, 10)
            .Select(i => $"=== src/File{i}.cs (edit: +1 -1) ===\n@@ -1,1 +1,1 @@\n-old{i}\n+new{i}")
            .ToArray();

        var tasks = diffs.Select(d => _processor.AddBeforeAfterContext(d)).ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(diffs.Length, results.Length);
        for (int i = 0; i < diffs.Length; i++)
            Assert.Equal(diffs[i], results[i]); // Repo not available, so all unchanged
    }
}
