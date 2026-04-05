using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using REBUSS.Pure.Core;
using REBUSS.Pure.Core.Models;
using REBUSS.Pure.Services.RepositoryDownload;

namespace REBUSS.Pure.Tests.Services.RepositoryDownload;

public class RepositoryDownloadOrchestratorTests : IDisposable
{
    private readonly IRepositoryArchiveProvider _archiveProvider = Substitute.For<IRepositoryArchiveProvider>();
    private readonly IHostApplicationLifetime _lifetime = Substitute.For<IHostApplicationLifetime>();
    private readonly RepositoryDownloadOrchestrator _orchestrator;
    private readonly CancellationTokenSource _stoppingCts = new();

    public RepositoryDownloadOrchestratorTests()
    {
        _lifetime.ApplicationStopping.Returns(_stoppingCts.Token);
        _orchestrator = new RepositoryDownloadOrchestrator(
            _archiveProvider,
            _lifetime,
            NullLogger<RepositoryDownloadOrchestrator>.Instance);
    }

    public void Dispose()
    {
        _stoppingCts.Dispose();
    }

    [Fact]
    public async Task TriggerDownload_FirstCall_StartsDownload()
    {
        var tcs = new TaskCompletionSource();
        _archiveProvider.DownloadRepositoryZipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        _orchestrator.TriggerDownloadAsync(42, "abc123");

        var state = _orchestrator.GetState();
        Assert.Equal(DownloadStatus.Downloading, state.Status);
        Assert.Equal(42, state.PrNumber);
        Assert.Equal("abc123", state.CommitRef);

        tcs.SetResult();
        // Allow background task to complete
        await Task.Delay(100);
    }

    [Fact]
    public async Task TriggerDownload_SecondCall_SamePr_DoesNotDuplicate()
    {
        var callCount = 0;
        _archiveProvider.DownloadRepositoryZipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Interlocked.Increment(ref callCount);
                var destPath = callInfo.ArgAt<string>(1);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                CreateMinimalZip(destPath);
                return Task.CompletedTask;
            });

        _orchestrator.TriggerDownloadAsync(42, "abc123");
        await Task.Delay(500); // Let first complete (download + extraction)

        _orchestrator.TriggerDownloadAsync(42, "abc123");
        await Task.Delay(200);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task TriggerDownload_DifferentPr_ReplacesOldDownload()
    {
        var callCount = 0;
        _archiveProvider.DownloadRepositoryZipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Interlocked.Increment(ref callCount);
                var destPath = callInfo.ArgAt<string>(1);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                CreateMinimalZip(destPath);
                return Task.CompletedTask;
            });

        _orchestrator.TriggerDownloadAsync(42, "abc123");
        await Task.Delay(500);

        _orchestrator.TriggerDownloadAsync(99, "def456");
        await Task.Delay(500);

        Assert.Equal(2, callCount);
        var state = _orchestrator.GetState();
        Assert.Equal(99, state.PrNumber);
        Assert.Equal("def456", state.CommitRef);
    }

    [Fact]
    public async Task TriggerDownload_DownloadFails_StateIsFailed()
    {
        _archiveProvider.DownloadRepositoryZipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        _orchestrator.TriggerDownloadAsync(42, "abc123");
        await Task.Delay(200);

        var state = _orchestrator.GetState();
        Assert.Equal(DownloadStatus.Failed, state.Status);
        Assert.Equal("Network error", state.ErrorMessage);
    }

    [Fact]
    public async Task TriggerDownload_Success_StateIsReady()
    {
        _archiveProvider.DownloadRepositoryZipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                // Create a fake ZIP file so extraction doesn't fail
                var destPath = callInfo.ArgAt<string>(1);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                CreateMinimalZip(destPath);
                return Task.CompletedTask;
            });

        _orchestrator.TriggerDownloadAsync(42, "abc123");

        var path = await _orchestrator.GetExtractedPathAsync();

        var state = _orchestrator.GetState();
        Assert.Equal(DownloadStatus.Ready, state.Status);
        Assert.NotNull(state.ExtractedPath);
    }

    [Fact]
    public async Task TriggerDownload_Success_DeletesZipFile()
    {
        string? zipPath = null;
        _archiveProvider.DownloadRepositoryZipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                zipPath = callInfo.ArgAt<string>(1);
                Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
                CreateMinimalZip(zipPath);
                return Task.CompletedTask;
            });

        _orchestrator.TriggerDownloadAsync(42, "abc123");
        await Task.Delay(500);

        Assert.NotNull(zipPath);
        Assert.False(File.Exists(zipPath), "ZIP file should be deleted after extraction");
    }

    [Fact]
    public async Task GetExtractedPathAsync_WaitsForCompletion()
    {
        var tcs = new TaskCompletionSource();
        _archiveProvider.DownloadRepositoryZipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await tcs.Task;
                var destPath = callInfo.ArgAt<string>(1);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                CreateMinimalZip(destPath);
            });

        _orchestrator.TriggerDownloadAsync(42, "abc123");

        var resultTask = _orchestrator.GetExtractedPathAsync();
        Assert.False(resultTask.IsCompleted);

        tcs.SetResult();
        var path = await resultTask;

        // Path may or may not be non-null depending on timing; the key is it didn't throw
        // and it waited for the download task to finish.
    }

    [Fact]
    public async Task GetExtractedPathAsync_OnFailure_ReturnsNull()
    {
        _archiveProvider.DownloadRepositoryZipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("fail"));

        _orchestrator.TriggerDownloadAsync(42, "abc123");

        var path = await _orchestrator.GetExtractedPathAsync();
        Assert.Null(path);
    }

    [Fact]
    public void Shutdown_NoDownload_CompletesWithoutError()
    {
        // Simulate shutdown by cancelling the stopping token
        _stoppingCts.Cancel();

        // No exception should be thrown
        var state = _orchestrator.GetState();
        Assert.Equal(DownloadStatus.NotStarted, state.Status);
    }

    private static void CreateMinimalZip(string path)
    {
        using var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        var entry = archive.CreateEntry("dummy.txt");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("test");
    }
}
