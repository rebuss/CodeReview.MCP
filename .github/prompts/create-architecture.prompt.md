# Task: Create `.github/Architecture.md`

## Goal

Create a **deep-dive architecture document** (~200–300 lines) that explains **how the system works internally and why it's structured this way**.

This file sits between the brief cheat sheet (`ProjectConventions.md`, ~130 lines — answers "what do I do?") and the raw inventory (`CodebaseUnderstanding.md`, ~720 lines — answers "what exists?"). `Architecture.md` answers **"how does it work under the hood and why?"** — the knowledge an agent needs when touching cross-cutting concerns, changing protocol behavior, modifying auth/config, or implementing complex features that span multiple layers.

## Relationship to existing files

| File | Role | When to attach |
|---|---|---|
| `ProjectConventions.md` | Cheat sheet: conventions, data flow summaries, extension recipes | Every task |
| `Architecture.md` | Deep dive: component interactions, protocol, auth, config, design rationale | Complex/cross-cutting tasks |
| `CodebaseUnderstanding.md` | Full inventory: every file, dependency, DI registration | When agent needs to find specific files |

**Non-duplication rule:** `ProjectConventions.md` already has brief summaries of architecture (§1), data flow (§2), and config/auth (§6). `Architecture.md` must go **deeper** — show internal mechanics, decision rationale, edge cases, and component interactions that don't fit in a 130-line cheat sheet. Do NOT repeat the same information at the same level of detail.

## How to build it

### Step 1: Read existing documentation for orientation

- `#file:'.github/ProjectConventions.md'` — know what's already covered (avoid duplication)
- `#file:'.github/CodebaseUnderstanding.md'` — sections 2 (dependency graph) and 3 (DI registrations) as source material
- `#file:'DeveloperGuide.md'` — user-facing auth/config docs (verify against implementation)

### Step 2: Read and trace implementation code

Read these files **in full** to understand internal mechanics — don't summarize from docs:

**MCP protocol layer (SDK-based):**
- `REBUSS.Pure/Program.cs` — MCP server bootstrap: `AddMcpServer`, `WithStdioServerTransport`, `WithToolsFromAssembly` (the SDK handles JSON-RPC loop, transport, and serialization)
- `REBUSS.Pure/Tools/GetPullRequestDiffToolHandler.cs` — reference tool: `[McpServerToolType]` class with `[McpServerTool]` methods, `[Description]` on parameters (the SDK discovers tools via assembly scanning)
- `REBUSS.Pure/Tools/GetLocalChangesFilesToolHandler.cs` — simpler tool example for comparison

**DI composition root:**
- `REBUSS.Pure/Program.cs` — full file: `Main`, `ConfigureBusinessServices`, `DetectProvider`, `BuildCliConfigOverrides`, `ResolvePatTarget`

**Provider architecture (pick ONE provider to trace fully, note differences with the other):**
- `REBUSS.Pure.AzureDevOps/ServiceCollectionExtensions.cs` — full DI registration
- `REBUSS.Pure.AzureDevOps/AzureDevOpsScmClient.cs` — facade: delegation pattern, metadata enrichment
- `REBUSS.Pure.AzureDevOps/Providers/AzureDevOpsDiffProvider.cs` — core orchestration: API calls → parse → build hunks → skip logic
- `REBUSS.Pure.AzureDevOps/Api/AzureDevOpsApiClient.cs` — HTTP client: lazy BaseAddress, HTML 203 detection
- `REBUSS.Pure.GitHub/ServiceCollectionExtensions.cs` — note differences from ADO registration
- `REBUSS.Pure.GitHub/GitHubScmClient.cs` — note differences from ADO facade
- `REBUSS.Pure.GitHub/Providers/GitHubDiffProvider.cs` — note: `ParseWithCommits` single-parse, `Task.WhenAll` parallel fetch

**Authentication chains:**
- `REBUSS.Pure.AzureDevOps/Configuration/ChainedAuthenticationProvider.cs` — chain logic, caching, invalidation
- `REBUSS.Pure.AzureDevOps/Configuration/AuthenticationDelegatingHandler.cs` — lazy auth header, 203 retry
- `REBUSS.Pure.GitHub/Configuration/GitHubChainedAuthenticationProvider.cs` — same pattern, differences
- `REBUSS.Pure.GitHub/Configuration/GitHubAuthenticationHandler.cs` — GitHub headers, 401/403 retry, rate-limit exclusion

**Configuration resolution:**
- `REBUSS.Pure.AzureDevOps/Configuration/ConfigurationResolver.cs` — `IPostConfigureOptions` pattern, merge priorities
- `REBUSS.Pure.AzureDevOps/Configuration/GitRemoteDetector.cs` — URL parsing, candidate directories
- `REBUSS.Pure.GitHub/Configuration/GitHubConfigurationResolver.cs` — same pattern, note differences

**Workspace root resolution:**
- `REBUSS.Pure/Services/McpWorkspaceRootProvider.cs` — CLI `--repo` > MCP roots (fetched lazily via SDK's `IMcpServer.RequestRootsAsync`) > `localRepoPath` config; URI conversion

**Analysis pipeline:**
- `REBUSS.Pure.Core/Analysis/ReviewContextOrchestrator.cs` — orchestration: fetch → order → filter → run → accumulate
- `REBUSS.Pure.Core/Analysis/IReviewAnalyzer.cs` — contract: `SectionKey`, `Order`, `CanAnalyze`, `AnalyzeAsync`
- `REBUSS.Pure.Core/Analysis/AnalysisInput.cs` — immutable record with `PreviousSections` for inter-analyzer data sharing

**Local review pipeline:**
- `REBUSS.Pure/Services/LocalReview/LocalGitClient.cs` — git process spawning, scope handling, `WorkingTreeRef` sentinel
- `REBUSS.Pure/Services/LocalReview/LocalReviewProvider.cs` — orchestration: git status → classify → diff
- `REBUSS.Pure/Services/LocalReview/LocalReviewScope.cs` — value type, `Parse` logic

### Step 3: Trace edge cases and design decisions

While reading the code above, pay special attention to and document:

1. **Why interface forwarding?** — `IScmClient` extends `IPullRequestDataProvider` + `IFileContentDataProvider`; tool handlers depend on narrow interfaces; why this decomposition?
2. **Why one provider per process?** — `DetectProvider` selects once; no runtime switching. What's the design rationale?
3. **Why `IPostConfigureOptions` for config resolution?** — why not resolve in constructor? What does the lazy pattern buy?
4. **Why fine-grained providers instead of one big class?** — SRP, testability, but also: how do they share data (e.g. diff provider data reused by files provider)?
5. **MCP SDK integration** — how does the app configure the MCP SDK (`AddMcpServer`, `WithStdioServerTransport`, `WithToolsFromAssembly`)? How are tools discovered via `[McpServerToolType]`/`[McpServerTool]` attributes?
6. **HTML 203 response detection** — Azure DevOps returns HTML on expired auth with 2xx status. How is this caught?
7. **GitHub rate-limit exclusion** — `GitHubAuthenticationHandler` retries on 401/403 but NOT on rate-limit 403. How is this distinguished?
8. **`WorkingTreeRef` sentinel** — what is it, why does it exist, how does `LocalGitClient` use it?
9. **Inter-analyzer data sharing** — `AnalysisInput.PreviousSections` pattern: how does one analyzer consume another's output?
10. **Workspace root resolution priority** — CLI `--repo` > MCP roots > config. Why this order? What happens with unexpanded variables?

### Step 4: Write the document

Use this **exact structure** (all sections mandatory):

---

```markdown
# Architecture Deep Dive

> Internal architecture reference for coding agents working on cross-cutting changes.
> For quick conventions and extension recipes, see `ProjectConventions.md`.
> For file inventory and DI registrations, see `CodebaseUnderstanding.md`.
> Update this file when internal mechanics, protocol handling, or design patterns change.

## 1. MCP Protocol Layer (SDK-Based)

### SDK Bootstrap
[How Program.cs configures the MCP SDK: `AddMcpServer` (server info), 
`WithStdioServerTransport` (stdin/stdout), `WithToolsFromAssembly` (attribute-based 
tool discovery). The SDK handles JSON-RPC loop, message dispatch, and serialization — 
no custom McpServer, transport, or serializer classes exist.]

### Tool Discovery & Execution
[How the SDK discovers tools via `[McpServerToolType]` classes and `[McpServerTool]` methods.
`[Description]` attributes on methods and parameters generate tool definitions.
Constructor injection works via the DI container — tool classes are not singletons, 
they are resolved per-call.]

### Transport & Serialization
[SDK provides stdio transport. Logging must go to stderr (stdout reserved for MCP).
Tool handlers serialize their output DTOs to JSON strings (WriteIndented=true, camelCase, 
WhenWritingNull) which the SDK wraps in the JSON-RPC envelope.]

## 2. Provider Architecture

### The Provider Pattern (detailed)
[Full decomposition: IScmClient (facade) → fine-grained providers (DiffProvider, 
MetadataProvider, FilesProvider, FileContentProvider) → parsers → API client.
WHY this layering: testability, SRP, data sharing between providers.
Concrete example: how FilesProvider reuses DiffProvider's output.]

### Interface Forwarding & Narrow Dependencies
[IScmClient extends IPullRequestDataProvider + IFileContentDataProvider.
Tool handlers depend on narrow interfaces. ServiceCollectionExtensions registers 
facade + forwards. WHY: tool handlers don't need to know which provider is active.]

### Provider Selection (DetectProvider)
[Priority chain: explicit --provider → GitHub.Owner → AzureDevOps.OrganizationName 
→ git remote URL → default AzureDevOps. One provider per process, no runtime switching.
WHY: simplicity, avoids multi-provider complexity in DI.]

### Azure DevOps vs GitHub: Key Implementation Differences
[Table or bullet list of notable differences:
- ADO uses iterations API for base/target commits; GitHub uses head/base SHAs from PR details
- ADO fetches file content by commitId; GitHub by ref (sha)
- GitHub uses `ParseWithCommits` for single-parse optimization
- GitHub fetches base/head content in parallel (Task.WhenAll); ADO sequential
- ADO DiffProvider detects HTML 203; GitHub AuthHandler detects 401/403
- Any other significant differences found in Step 2]

## 3. Authentication & Token Management

### Chained Authentication Pattern
[Both providers use identical chain: PAT → cached token → CLI tool → error.
Describe the chain logic, lazy evaluation, short-circuit on first success.]

### Token Caching & Invalidation
[Where tokens are cached (LocalConfigStore / GitHubConfigStore, %LOCALAPPDATA%).
When invalidation happens (DelegatingHandler retry on auth failure).
How cached tokens are refreshed (expiry check → CLI re-acquisition).]

### DelegatingHandler Retry Logic
[ADO: AuthenticationDelegatingHandler — lazy auth header, retry on HTTP 203 HTML.
GitHub: GitHubAuthenticationHandler — required headers (Accept, User-Agent, 
X-GitHub-Api-Version), retry on 401/403, rate-limit exclusion via X-RateLimit-Remaining.
WHY different: ADO returns 203 HTML on expired tokens; GitHub returns proper 401/403.]

## 4. Configuration Resolution

### IPostConfigureOptions Pattern
[Both providers use IPostConfigureOptions for config resolution.
WHY: options are bound from IConfiguration early, but git remote detection and 
cache reads need workspace root (available later). PostConfigure runs lazily 
on first IOptions<T>.Value access.]

### Merge Priority
[User config (appsettings) > auto-detected (git remote) > cached (local store).
Describe how ConfigurationResolver.PostConfigure merges these.
Note: CLI args are injected via AddInMemoryCollection BEFORE PostConfigure runs, 
so they win over everything.]

### Workspace Root Resolution
[McpWorkspaceRootProvider: CLI --repo (highest) > MCP roots (from initialize) > 
localRepoPath config. URI-to-path conversion. Guard against unexpanded variables.
WHY CLI --repo is highest: user explicitly specified it. MCP roots come from IDE.
Config is fallback.]

## 5. Analysis Pipeline

### Orchestrator Pattern
[ReviewContextOrchestrator: fetch (diff + metadata + files) → sort analyzers 
by Order → filter (CanAnalyze) → run sequentially → accumulate sections.
WHY sequential: analyzers can depend on previous sections' output.]

### Inter-Analyzer Data Sharing
[AnalysisInput is a record. Before each analyzer runs, orchestrator creates 
a new AnalysisInput copy with PreviousSections populated. Pattern is explicit — 
no shared mutable state.]

### Extension Point
[Register IReviewAnalyzer in DI. Orchestrator discovers via IEnumerable<IReviewAnalyzer>.
No changes to orchestrator needed. Cross-reference with recipe 5.4 in ProjectConventions.md.]

## 6. Local Review Pipeline

### Scope Model
[LocalReviewScope: WorkingTree (staged+unstaged vs HEAD), Staged (index vs HEAD), 
BranchDiff(base) (current branch vs base). Parse logic from string input.]

### Git Process Spawning
[LocalGitClient: runs git as child process. Uses `diff --name-status` for all scopes.
WorkingTreeRef sentinel: represents "filesystem content" (not a real git ref) — 
used when reading file content for working-tree scope (reads file directly instead 
of git show).]

### File Content Resolution
[How LocalReviewProvider resolves base and target content depending on scope:
- WorkingTree: base from HEAD, target from filesystem
- Staged: base from HEAD, target from index
- BranchDiff: base from merge-base, target from HEAD
How StructuredDiffBuilder produces hunks from base/target strings.]

## 7. Design Decisions & Rationale

[Collect the WHY answers from Steps 2-3 that don't fit in the sections above:
- Why singletons everywhere (stateless services, expensive HTTP clients)
- Why no abstract base class for providers (composition over inheritance)
- Why embedded resources for prompts (single deployment artifact, version-locked)
- Why daily log rotation with 3-day retention (MCP servers are long-lived)
- Any other notable decisions discovered during analysis]
```

---

## Quality rules

1. **Deep, not broad** — each section should explain mechanics and rationale, not just list components. A reader of this file should understand HOW things work, not just WHAT exists.
2. **Code-verified** — every claim must be verified against actual implementation (Step 2). Don't guess from docs.
3. **Complement, don't duplicate** — if `ProjectConventions.md` already covers something at adequate depth, reference it instead of repeating. Go deeper only where the cheat sheet is insufficient.
4. **Target 200–300 lines** — this is a reference, not a novel. Use diagrams, tables, and bullet points. Prose only for rationale/WHY sections.
5. **Stable** — don't include volatile details (specific file counts, line numbers). Focus on patterns and mechanics.
6. **Include design rationale** — the unique value of this document is explaining WHY, not just HOW. Every section should have at least one "WHY" explanation.

## After creating the file

1. Add the file to `CodebaseUnderstanding.md` Documentation section:
   ```
   | `.github\Architecture.md` | Deep-dive architecture reference: MCP protocol, provider internals, auth/config mechanics, analysis pipeline, design rationale |
   ```
2. Update `.github/copilot-instructions.md` to add:
   ```markdown
   ### Architecture Updates
   Update `.github/Architecture.md` **only if** the change modifies internal mechanics
   of the MCP protocol layer, provider architecture, authentication/configuration
   resolution, or analysis pipeline. Do not update for routine feature additions
   covered by `ProjectConventions.md` extension recipes.
   ```
3. Verify no contradictions with `ProjectConventions.md` or `CodebaseUnderstanding.md`.
4. Do NOT update `README.md` or `DeveloperGuide.md` — this task only creates a new file.
