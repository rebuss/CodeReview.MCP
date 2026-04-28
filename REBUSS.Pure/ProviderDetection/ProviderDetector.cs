using Microsoft.Extensions.Configuration;
using REBUSS.Pure.AzureDevOps.Configuration;
using REBUSS.Pure.GitHub.Configuration;
using REBUSS.Pure.Properties;
using AzureDevOpsNames = REBUSS.Pure.AzureDevOps.Names;
using GitHubNames = REBUSS.Pure.GitHub.Names;

namespace REBUSS.Pure.ProviderDetection
{
    /// <summary>
    /// Determines the SCM provider to use based on configuration and git remote auto-detection.
    /// Priority: explicit "Provider" key > GitHub config section populated >
    /// AzureDevOps config section populated > git remote URL > default (AzureDevOps).
    /// </summary>
    internal static class ProviderDetector
    {
        internal static string Detect(IConfiguration configuration, string? repoPath = null)
        {
            // 1. Explicit provider setting (normalized to canonical casing)
            var explicitProvider = configuration.GetValue<string>(Resources.ConfigKeyProvider);
            if (!string.IsNullOrWhiteSpace(explicitProvider))
                return explicitProvider.ToLowerInvariant() switch
                {
                    GitHubNames.ProviderLower => GitHubNames.Provider,
                    AzureDevOpsNames.ProviderLower => AzureDevOpsNames.Provider,
                    _ => explicitProvider
                };

            // 2. Check if GitHub section has owner configured
            var githubOwner = configuration[$"{GitHubOptions.SectionName}:{nameof(GitHubOptions.Owner)}"];
            if (!string.IsNullOrWhiteSpace(githubOwner))
                return GitHubNames.Provider;

            // 3. Check if AzureDevOps section has organization configured
            var adoOrg = configuration[$"{AzureDevOpsOptions.SectionName}:{nameof(AzureDevOpsOptions.OrganizationName)}"];
            if (!string.IsNullOrWhiteSpace(adoOrg))
                return AzureDevOpsNames.Provider;

            // 4. Auto-detect from git remote URL
            var remoteUrl = GetGitRemoteUrl(repoPath);
            if (remoteUrl is not null)
            {
                if (remoteUrl.Contains(GitHubNames.Domain, StringComparison.OrdinalIgnoreCase))
                    return GitHubNames.Provider;

                if (remoteUrl.Contains(AzureDevOpsNames.Domain, StringComparison.OrdinalIgnoreCase) ||
                    remoteUrl.Contains(AzureDevOpsNames.LegacyDomain, StringComparison.OrdinalIgnoreCase))
                    return AzureDevOpsNames.Provider;
            }

            // 5. Default
            return AzureDevOpsNames.Provider;
        }

        internal static string? GetGitRemoteUrl(string? workingDirectory = null)
        {
            try
            {
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = Resources.GitExecutable,
                        Arguments = Resources.GitRemoteGetUrlArgs,
                        WorkingDirectory = workingDirectory ?? string.Empty,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.StandardInput.Close();

                if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return null;
                }

                var output = process.StandardOutput.ReadToEnd();
                return process.ExitCode == 0 ? output.Trim() : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
