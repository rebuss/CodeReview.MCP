namespace REBUSS.Pure.Core;

/// <summary>
/// Constants for Git command-line operations shared across providers.
/// </summary>
public static class GitConstants
{
    /// <summary>Git executable name.</summary>
    public const string Executable = "git";

    /// <summary>Arguments to retrieve the remote origin URL.</summary>
    public const string RemoteGetUrlArgs = "remote get-url origin";
}
