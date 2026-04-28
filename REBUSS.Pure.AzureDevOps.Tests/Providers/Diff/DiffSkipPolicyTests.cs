using REBUSS.Pure.AzureDevOps.Providers.Diff;
using REBUSS.Pure.Core.Models;
using REBUSS.Pure.Core.Shared;

namespace REBUSS.Pure.AzureDevOps.Tests.Providers.Diff;

/// <summary>
/// Focused unit tests for <see cref="DiffSkipPolicy"/>. Cases relocated from
/// <c>AzureDevOpsDiffProviderTests</c> after the policy was extracted from the
/// orchestrator — same scenarios, narrower seam.
/// </summary>
public class DiffSkipPolicyTests
{
    private readonly DiffSkipPolicy _policy = new(new FileClassifier());

    [Fact]
    public void GetSkipReason_ReturnsFileDeleted_ForDeleteChangeType()
    {
        var file = new FileChange { Path = "/src/File.cs", ChangeType = "delete" };
        Assert.Equal("file deleted", _policy.GetSkipReason(file));
    }

    [Fact]
    public void GetSkipReason_ReturnsFileRenamed_ForRenameChangeType()
    {
        var file = new FileChange { Path = "/src/File.cs", ChangeType = "rename" };
        Assert.Equal("file renamed", _policy.GetSkipReason(file));
    }

    [Fact]
    public void GetSkipReason_ReturnsBinaryFile_ForBinaryExtension()
    {
        var file = new FileChange { Path = "/assets/logo.png", ChangeType = "edit" };
        Assert.Equal("binary file", _policy.GetSkipReason(file));
    }

    [Fact]
    public void GetSkipReason_ReturnsGeneratedFile_ForGeneratedPath()
    {
        var file = new FileChange { Path = "/obj/Debug/net8.0/AssemblyInfo.cs", ChangeType = "edit" };
        Assert.Equal("generated file", _policy.GetSkipReason(file));
    }

    [Fact]
    public void GetSkipReason_ReturnsNull_ForNormalSourceFile()
    {
        var file = new FileChange { Path = "/src/Service.cs", ChangeType = "edit" };
        Assert.Null(_policy.GetSkipReason(file));
    }

    [Fact]
    public void GetSkipReason_ReturnsNull_ForNewFile()
    {
        var file = new FileChange { Path = "/src/NewService.cs", ChangeType = "add" };
        Assert.Null(_policy.GetSkipReason(file));
    }
}
