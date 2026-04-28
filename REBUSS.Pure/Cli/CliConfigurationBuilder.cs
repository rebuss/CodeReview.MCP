using REBUSS.Pure.AzureDevOps.Configuration;
using REBUSS.Pure.GitHub.Configuration;
using REBUSS.Pure.Properties;
using AzureDevOpsNames = REBUSS.Pure.AzureDevOps.Names;
using GitHubNames = REBUSS.Pure.GitHub.Names;

namespace REBUSS.Pure.Cli
{
    /// <summary>
    /// Builds the in-memory configuration overrides applied on top of <c>appsettings*.json</c>
    /// when the server is started with CLI flags (<c>--pat</c>, <c>--owner</c>, <c>--org</c>, etc.).
    /// Routes the PAT secret only to the configuration section of the inferred target provider.
    /// </summary>
    internal static class CliConfigurationBuilder
    {
        internal static Dictionary<string, string?> BuildOverrides(CliParseResult parseResult)
        {
            var overrides = new Dictionary<string, string?>();

            if (!string.IsNullOrWhiteSpace(parseResult.Provider))
                overrides[Resources.ConfigKeyProvider] = parseResult.Provider;

            // Determine which provider should receive the PAT based on CLI context
            var patTarget = ResolvePatTarget(parseResult);

            if (!string.IsNullOrWhiteSpace(parseResult.Pat))
            {
                if (patTarget is null || string.Equals(patTarget, AzureDevOpsNames.Provider, StringComparison.OrdinalIgnoreCase))
                    overrides[$"{AzureDevOpsOptions.SectionName}:{nameof(AzureDevOpsOptions.PersonalAccessToken)}"] = parseResult.Pat;
                if (patTarget is null || string.Equals(patTarget, GitHubNames.Provider, StringComparison.OrdinalIgnoreCase))
                    overrides[$"{GitHubOptions.SectionName}:{nameof(GitHubOptions.PersonalAccessToken)}"] = parseResult.Pat;
            }

            if (!string.IsNullOrWhiteSpace(parseResult.Organization))
                overrides[$"{AzureDevOpsOptions.SectionName}:{nameof(AzureDevOpsOptions.OrganizationName)}"] = parseResult.Organization;

            if (!string.IsNullOrWhiteSpace(parseResult.Project))
                overrides[$"{AzureDevOpsOptions.SectionName}:{nameof(AzureDevOpsOptions.ProjectName)}"] = parseResult.Project;

            if (!string.IsNullOrWhiteSpace(parseResult.Repository))
            {
                if (patTarget is null || string.Equals(patTarget, AzureDevOpsNames.Provider, StringComparison.OrdinalIgnoreCase))
                    overrides[$"{AzureDevOpsOptions.SectionName}:{nameof(AzureDevOpsOptions.RepositoryName)}"] = parseResult.Repository;
                if (patTarget is null || string.Equals(patTarget, GitHubNames.Provider, StringComparison.OrdinalIgnoreCase))
                    overrides[$"{GitHubOptions.SectionName}:{nameof(GitHubOptions.RepositoryName)}"] = parseResult.Repository;
            }

            // GitHub CLI overrides
            if (!string.IsNullOrWhiteSpace(parseResult.Owner))
                overrides[$"{GitHubOptions.SectionName}:{nameof(GitHubOptions.Owner)}"] = parseResult.Owner;

            return overrides;
        }

        /// <summary>
        /// Infers which provider the CLI arguments are targeting so that secrets
        /// (e.g. PAT) are only written to the relevant configuration section.
        /// Returns <c>null</c> when the target cannot be determined.
        /// </summary>
        internal static string? ResolvePatTarget(CliParseResult parseResult)
        {
            if (!string.IsNullOrWhiteSpace(parseResult.Provider))
                return parseResult.Provider;
            if (!string.IsNullOrWhiteSpace(parseResult.Owner))
                return GitHubNames.Provider;
            if (!string.IsNullOrWhiteSpace(parseResult.Organization))
                return AzureDevOpsNames.Provider;
            return null;
        }
    }
}
