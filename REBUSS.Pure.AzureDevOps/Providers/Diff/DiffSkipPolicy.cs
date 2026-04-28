using REBUSS.Pure.AzureDevOps.Properties;
using REBUSS.Pure.Core.Models;
using REBUSS.Pure.Core.Shared;

namespace REBUSS.Pure.AzureDevOps.Providers.Diff;

/// <summary>
/// Decides whether the structured diff for a given <see cref="FileChange"/> should
/// be computed at all. Returns the resource-localized skip reason (one of
/// <c>SkipReasonFileDeleted</c> / <c>SkipReasonFileRenamed</c> / <c>SkipReasonBinaryFile</c> /
/// <c>SkipReasonGeneratedFile</c>) or <c>null</c> when normal diff logic should
/// proceed. Pure logic — no I/O — driven entirely by the change-type string and
/// the result of <see cref="IFileClassifier.Classify"/>.
/// </summary>
internal sealed class DiffSkipPolicy
{
    private readonly IFileClassifier _fileClassifier;

    public DiffSkipPolicy(IFileClassifier fileClassifier)
    {
        _fileClassifier = fileClassifier;
    }

    public string? GetSkipReason(FileChange file)
    {
        if (string.Equals(file.ChangeType, "delete", StringComparison.OrdinalIgnoreCase))
            return Resources.SkipReasonFileDeleted;

        if (string.Equals(file.ChangeType, "rename", StringComparison.OrdinalIgnoreCase))
            return Resources.SkipReasonFileRenamed;

        var classification = _fileClassifier.Classify(file.Path);

        if (classification.IsBinary)
            return Resources.SkipReasonBinaryFile;

        if (classification.IsGenerated)
            return Resources.SkipReasonGeneratedFile;

        return null;
    }
}
