namespace REBUSS.Pure.Core;

/// <summary>
/// Well-known skip-reason strings assigned to <see cref="Models.FileChange.SkipReason"/>
/// when a file cannot be diffed. Shared across all SCM providers.
/// </summary>
public static class SkipReasons
{
    public const string BinaryFile = "binary file";
    public const string FileDeleted = "file deleted";
    public const string FileRenamed = "file renamed";
    public const string FullFileRewrite = "full file rewrite";
    public const string GeneratedFile = "generated file";
}
