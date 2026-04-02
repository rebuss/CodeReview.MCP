namespace REBUSS.Pure;

/// <summary>
/// Application-level string constants for the REBUSS.Pure MCP server.
/// </summary>
internal static class AppConstants
{
    /// <summary>MCP server name and application directory name.</summary>
    public const string ServerName = "REBUSS.Pure";

    /// <summary>Fallback executable file name used when <see cref="Environment.ProcessPath"/> is unavailable.</summary>
    public const string ExecutableName = "REBUSS.Pure.exe";

    /// <summary>MCP server version reported via ServerInfo.</summary>
    public const string ServerVersion = "1.0.0";

    /// <summary>Main application settings file name.</summary>
    public const string AppSettingsFileName = "appsettings.json";

    /// <summary>Local (user-specific, git-ignored) settings file name.</summary>
    public const string AppSettingsLocalFileName = "appsettings.Local.json";

    /// <summary>CLI sub-command for initialization.</summary>
    public const string CliCommandInit = "init";

    /// <summary>Configuration key for the active SCM provider.</summary>
    public const string ConfigKeyProvider = "Provider";

    /// <summary>Git arguments to resolve the current branch name.</summary>
    public const string GitRevParseAbbrevRefHead = "rev-parse --abbrev-ref HEAD";
}
