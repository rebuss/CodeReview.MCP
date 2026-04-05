using System.Text.RegularExpressions;

namespace REBUSS.Pure.Core.Shared;

/// <summary>
/// Programming languages detectable from diff file paths.
/// </summary>
public enum DiffLanguage
{
    CSharp,
    TypeScript,
    JavaScript,
    Python,
    Go,
    Java,
    Rust,
    Kotlin,
    Ruby,
    Swift,
    Php,
    Cpp,
    Unknown
}

/// <summary>
/// Centralized language detection for diffs. Identifies the programming language
/// from the diff header file path. Used by enrichers for applicability checks.
/// </summary>
public static partial class DiffLanguageDetector
{
    // Same pattern as DiffParser.TryParseFilePath — duplicated to avoid Core→RoslynProcessor dependency.
    private static readonly Regex FilePathRegex = FilePathPattern();

    [GeneratedRegex(@"^=== (.+?) \(", RegexOptions.Multiline)]
    private static partial Regex FilePathPattern();

    /// <summary>
    /// Detects the programming language from the diff header file path.
    /// </summary>
    public static DiffLanguage Detect(string diff)
    {
        if (string.IsNullOrEmpty(diff))
            return DiffLanguage.Unknown;

        var match = FilePathRegex.Match(diff);
        if (!match.Success)
            return DiffLanguage.Unknown;

        var filePath = match.Groups[1].Value;
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".cs" => DiffLanguage.CSharp,
            ".ts" or ".tsx" => DiffLanguage.TypeScript,
            ".js" or ".jsx" or ".mjs" or ".cjs" => DiffLanguage.JavaScript,
            ".py" => DiffLanguage.Python,
            ".go" => DiffLanguage.Go,
            ".java" => DiffLanguage.Java,
            ".rs" => DiffLanguage.Rust,
            ".kt" or ".kts" => DiffLanguage.Kotlin,
            ".rb" => DiffLanguage.Ruby,
            ".swift" => DiffLanguage.Swift,
            ".php" => DiffLanguage.Php,
            ".cpp" or ".cc" or ".cxx" or ".hpp" or ".h" => DiffLanguage.Cpp,
            _ => DiffLanguage.Unknown
        };
    }

    /// <summary>Returns <c>true</c> if the diff is for a C# file.</summary>
    public static bool IsCSharp(string diff) => Detect(diff) == DiffLanguage.CSharp;

    /// <summary>Returns <c>true</c> if the diff is marked as skipped.</summary>
    public static bool IsSkipped(string diff) => diff.Contains("skipped) ===");
}
