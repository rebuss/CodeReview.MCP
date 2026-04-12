using Microsoft.Extensions.Logging.Abstractions;
using REBUSS.Pure.Services.RepositoryDownload;

namespace REBUSS.Pure.Tests.Services.RepositoryDownload;

public class RepositoryCleanupServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RepositoryCleanupService _service;

    public RepositoryCleanupServiceTests()
    {
        _tempDir = Path.GetTempPath();
        _service = new RepositoryCleanupService(
            NullLogger<RepositoryCleanupService>.Instance);
    }

    public void Dispose()
    {
        // Best-effort cleanup of any directories we created
        foreach (var dir in Directory.EnumerateDirectories(_tempDir, "rebuss-repo-99999*"))
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task StartAsync_DeletesOrphanedDirectories()
    {
        // PID 99999 is almost certainly not running
        var orphanDir = Path.Combine(_tempDir, "rebuss-repo-99999");
        Directory.CreateDirectory(orphanDir);
        Directory.CreateDirectory(Path.Combine(orphanDir, "42"));

        await _service.StartAsync(CancellationToken.None);

        Assert.False(Directory.Exists(orphanDir), "Orphaned directory should be deleted");
    }

    [Fact]
    public async Task StartAsync_PreservesActiveInstanceDirectories()
    {
        // Current process PID — should NOT be deleted
        var currentPid = Environment.ProcessId;
        var activeDir = Path.Combine(_tempDir, $"rebuss-repo-{currentPid}");
        Directory.CreateDirectory(activeDir);

        try
        {
            await _service.StartAsync(CancellationToken.None);

            Assert.True(Directory.Exists(activeDir), "Active instance directory should be preserved");
        }
        finally
        {
            try { Directory.Delete(activeDir, true); } catch { }
        }
    }

    [Fact]
    public async Task StartAsync_NoOrphanedDirs_CompletesWithoutError()
    {
        // Just verify it doesn't throw when there's nothing to clean
        await _service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public void TryExtractPid_ValidName_ReturnsTrueAndPid()
    {
        Assert.True(RepositoryCleanupService.TryExtractPid("rebuss-repo-12345", "rebuss-repo-", out var pid));
        Assert.Equal(12345, pid);
    }

    [Fact]
    public void TryExtractPid_InvalidName_ReturnsFalse()
    {
        Assert.False(RepositoryCleanupService.TryExtractPid("rebuss-repo-abc", "rebuss-repo-", out _));
    }

    [Fact]
    public void TryExtractPid_WrongPrefix_ReturnsFalse()
    {
        Assert.False(RepositoryCleanupService.TryExtractPid("other-dir-123", "rebuss-repo-", out _));
    }
}
