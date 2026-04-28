using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using REBUSS.Pure.AzureDevOps;
using REBUSS.Pure.Cli;
using REBUSS.Pure.Core;
using REBUSS.Pure.Core.Services.CopilotReview;
using REBUSS.Pure.Core.Shared;
using REBUSS.Pure.GitHub;
using REBUSS.Pure.ProviderDetection;
using REBUSS.Pure.RoslynProcessor;
using REBUSS.Pure.Services;
using REBUSS.Pure.Services.ContextWindow;
using REBUSS.Pure.Services.CopilotReview;
using REBUSS.Pure.Services.LocalReview;
using REBUSS.Pure.Services.PrEnrichment;
using REBUSS.Pure.Services.RepositoryDownload;
using AzureDevOpsNames = REBUSS.Pure.AzureDevOps.Names;
using GitHubNames = REBUSS.Pure.GitHub.Names;
using Pagination = REBUSS.Pure.Services.Pagination;
using ResponsePacking = REBUSS.Pure.Services.ResponsePacking;

namespace REBUSS.Pure.DependencyInjection
{
    /// <summary>
    /// DI composition root extensions. Splits the previously monolithic
    /// <c>Program.ConfigureBusinessServices</c> into focused, single-purpose registration
    /// groups so each area (core, copilot agent, validation, providers, local review)
    /// can be reasoned about — and replaced in tests — in isolation.
    /// </summary>
    internal static class ServiceCollectionExtensions
    {
        internal static IServiceCollection AddBusinessServices(
            this IServiceCollection services,
            IConfiguration configuration,
            string? repoPath,
            string? agent)
        {
            services.AddCoreServices(configuration);
            services.AddCopilotAgentServices(configuration, agent);
            services.AddInspectionServices();
            services.AddFindingValidationServices();
            services.AddContextWindowServices(configuration);
            services.AddProviderServices(configuration, repoPath);
            services.AddLocalReviewServices();
            return services;
        }

        private static IServiceCollection AddCoreServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Workspace root provider: resolves repository path from CLI --repo, MCP roots, or localRepoPath
            services.AddSingleton<IWorkspaceRootProvider, McpWorkspaceRootProvider>();

            // Shared services (provider-agnostic)
            services.AddSingleton<IDiffAlgorithm, DiffPlexDiffAlgorithm>();
            services.AddSingleton<IStructuredDiffBuilder, StructuredDiffBuilder>();
            services.AddSingleton<IFileClassifier, FileClassifier>();
            services.AddSingleton<DiffSourceResolver>();
            services.AddSingleton<IDiffEnricher, BeforeAfterEnricher>();         // Order=100
            services.AddSingleton<IDiffEnricher, ScopeAnnotatorEnricher>();      // Order=150
            services.AddSingleton<IDiffEnricher, StructuralChangeEnricher>();    // Order=200
            services.AddSingleton<IDiffEnricher, UsingsChangeEnricher>();        // Order=250
            services.AddSingleton<CallSiteScanner>();
            services.AddSingleton<IDiffEnricher, CallSiteEnricher>();            // Order=300
            services.AddSingleton<IDiffEnricher, FileStructureValidationEnricher>(); // Order=400
            services.AddSingleton<ICodeProcessor, CompositeCodeProcessor>();

            // Progress reporting (MCP notifications/progress)
            services.AddSingleton<IProgressReporter, ProgressReporter>();

            // Workflow timeouts (progressive PR metadata feature)
            services.Configure<WorkflowOptions>(configuration.GetSection(WorkflowOptions.SectionName));
            services.AddSingleton<IPrEnrichmentOrchestrator, PrEnrichmentOrchestrator>();

            // Response Packing
            services.AddSingleton<IResponsePacker, ResponsePacking.ResponsePacker>();

            // Deterministic Pagination (Feature 004)
            services.AddSingleton<IPageAllocator, Pagination.PageAllocator>();
            services.AddSingleton<IPageReferenceCodec, Pagination.PageReferenceCodec>();

            // PR diff cache (eliminates duplicate API calls between metadata and content)
            services.AddSingleton<IPullRequestDiffCache, PullRequestDiffCache>();
            return services;
        }

        private static IServiceCollection AddCopilotAgentServices(
            this IServiceCollection services,
            IConfiguration configuration,
            string? agent)
        {
            // Copilot Review Layer (feature 013) — SDK-backed server-side PR review.
            // The provider is registered three ways: (1) concrete singleton, (2) interface
            // alias so consumers can depend on ICopilotClientProvider, (3) IHostedService so
            // the generic host calls StopAsync on shutdown. All three resolve to the same instance.
            services.Configure<CopilotReviewOptions>(configuration.GetSection(CopilotReviewOptions.SectionName));
            services.AddSingleton<ICopilotTokenResolver, CopilotTokenResolver>();
            services.AddSingleton<CopilotVerificationRunner>();
            services.AddSingleton<ICopilotVerificationProbe>(sp => sp.GetRequiredService<CopilotVerificationRunner>());
            services.AddSingleton<CopilotClientProvider>();
            services.AddSingleton<ICopilotClientProvider>(sp => sp.GetRequiredService<CopilotClientProvider>());
            services.AddHostedService(sp => sp.GetRequiredService<CopilotClientProvider>());
            services.AddSingleton<CopilotRequestThrottle>();
            services.AddSingleton<ICopilotSessionFactory, CopilotSessionFactory>();

            // IAgentInvoker — one-shot prompt→text abstraction over Copilot SDK or Claude CLI.
            // Selection is driven by --agent on the command line (carried through mcp.json args);
            // when the flag is absent, Copilot is the default to preserve existing behavior.
            // AgentIdentity is registered alongside so tool handlers can label their
            // responses with the actual agent name (e.g. "claude-assisted") instead of
            // hardcoded "copilot-assisted" wording leaking to the wrong backend.
            var isClaude = string.Equals(agent, CliArgumentParser.AgentClaude, StringComparison.OrdinalIgnoreCase);
            services.AddSingleton(new Core.Services.AgentInvocation.AgentIdentity(
                isClaude ? CliArgumentParser.AgentClaude : CliArgumentParser.AgentCopilot));

            if (isClaude)
            {
                services.AddSingleton<Core.Services.AgentInvocation.IAgentInvoker,
                    Services.AgentInvocation.ClaudeCliAgentInvoker>();
            }
            else
            {
                services.AddSingleton<Core.Services.AgentInvocation.IAgentInvoker,
                    Services.AgentInvocation.CopilotAgentInvoker>();
            }

            services.AddSingleton<ICopilotAvailabilityDetector, CopilotAvailabilityDetector>();
            services.AddSingleton<IAgentPageReviewer, AgentPageReviewer>();
            services.AddSingleton<AgentReviewJobRegistry>();
            services.AddSingleton<PageReviewExecutor>();
            services.AddSingleton<FindingValidationPipeline>();
            services.AddSingleton<IAgentReviewOrchestrator, AgentReviewOrchestrator>();
            services.AddSingleton<AgentReviewWaiter>();
            return services;
        }

        private static IServiceCollection AddInspectionServices(this IServiceCollection services)
        {
            // Feature 022 — Copilot inspection (internal diagnostic, env-var gated).
            // REBUSS_COPILOT_INSPECT=1|true|True registers the filesystem writer; any other
            // value registers a no-op. Read once at DI composition time; restart to toggle.
            var inspectEnabled = Environment.GetEnvironmentVariable("REBUSS_COPILOT_INSPECT")
                is "1" or "true" or "True";
            if (inspectEnabled)
            {
                services.AddSingleton<
                    Services.CopilotReview.Inspection.IAgentInspectionWriter,
                    Services.CopilotReview.Inspection.FileSystemAgentInspectionWriter>();
            }
            else
            {
                services.AddSingleton<
                    Services.CopilotReview.Inspection.IAgentInspectionWriter,
                    Services.CopilotReview.Inspection.NoOpAgentInspectionWriter>();
            }
            return services;
        }

        private static IServiceCollection AddFindingValidationServices(this IServiceCollection services)
        {
            // Feature 021 — Finding validation pipeline (false positive reduction).
            // Registered unconditionally; AgentReviewOrchestrator short-circuits at
            // runtime based on CopilotReviewOptions.ValidateFindings (per Principle V
            // deferred resolution — the flag is read at first review, not at DI time).
            // Feature 023 — review-mode-aware source resolution. Remote serves PR reviews
            // (downloaded archive); Local serves local:* reviews (git ref via ILocalGitClient).
            // Selector binds the per-review git ref and dedupes the workspace-root warning.
            services.AddSingleton<Services.CopilotReview.Validation.RemoteArchiveSourceProvider>();
            services.AddSingleton<Services.CopilotReview.Validation.LocalWorkspaceSourceProvider>();
            services.AddSingleton<IFindingSourceProviderSelector,
                Services.CopilotReview.Validation.FindingSourceProviderSelector>();
            services.AddSingleton<Services.CopilotReview.Validation.FindingScopeResolver>();
            services.AddSingleton<Services.CopilotReview.Validation.FindingValidator>();
            return services;
        }

        private static IServiceCollection AddContextWindowServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<ContextWindowOptions>(configuration.GetSection(ContextWindowOptions.SectionName));
            services.AddSingleton<IContextBudgetResolver, ContextBudgetResolver>();
            services.AddSingleton<ITokenEstimator, TokenEstimator>();
            return services;
        }

        private static IServiceCollection AddProviderServices(
            this IServiceCollection services,
            IConfiguration configuration,
            string? repoPath)
        {
            // Provider selection: explicit config > auto-detection from git remote
            var provider = ProviderDetector.Detect(configuration, repoPath);
            switch (provider)
            {
                case GitHubNames.Provider:
                    services.AddGitHubProvider(configuration);
                    break;
                case AzureDevOpsNames.Provider:
                default:
                    services.AddAzureDevOpsProvider(configuration);
                    break;
            }

            // Repository download orchestrator + startup cleanup
            services.AddSingleton<IRepositoryDownloadOrchestrator, RepositoryDownloadOrchestrator>();
            services.AddHostedService<RepositoryCleanupService>();
            return services;
        }

        private static IServiceCollection AddLocalReviewServices(this IServiceCollection services)
        {
            services.AddSingleton<ILocalGitClient, LocalGitClient>();
            services.AddSingleton<ILocalReviewProvider, LocalReviewProvider>();
            services.AddSingleton<ILocalEnrichmentOrchestrator, LocalEnrichmentOrchestrator>();
            return services;
        }
    }
}
