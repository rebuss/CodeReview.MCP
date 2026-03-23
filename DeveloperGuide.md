# 📖 Technical Reference

## CLI Commands

### `rebuss-pure init`

Initializes MCP configuration in the current Git repository.

```bash
# Default — uses Azure CLI for authentication
rebuss-pure init

# With a Personal Access Token
rebuss-pure init --pat <your-pat>
```

**What it does:**

1. Finds the Git repository root
2. Authenticates (Azure CLI or PAT)
3. Detects IDEs and writes `mcp.json` to the appropriate directory
4. Copies prompt files to `.github/prompts/`

**IDE detection logic:**

| Markers found | Config written to |
|---|---|
| `.vscode/` or `*.code-workspace` only | `.vscode/mcp.json` |
| `.vs/` or `*.sln` only | `.vs/mcp.json` |
| Both or neither | Both locations |

---

### Server mode (launched automatically by MCP client)

The MCP client starts the server via the generated `mcp.json`. You can also start it manually:

```bash
rebuss-pure --repo /path/to/repo [--pat <token>] [--org <org>] [--project <project>] [--repository <repo-name>]
```

| Argument | Description |
|---|---|
| `--repo` | Path to the local Git repository |
| `--pat` | Personal Access Token for Azure DevOps |
| `--org` | Azure DevOps organization name (auto-detected from Git remote if omitted) |
| `--project` | Azure DevOps project name (auto-detected if omitted) |
| `--repository` | Azure DevOps repository name (auto-detected if omitted) |

---

## Authentication

REBUSS.Pure uses a chained authentication strategy. It tries each method in order and uses the first one that succeeds:

### 1. Personal Access Token (PAT) — explicit config (highest priority)

Provide via CLI:

```bash
rebuss-pure init --pat <your-pat>
```

Or create `appsettings.Local.json` next to the server executable:

```json
{
  "AzureDevOps": {
    "PersonalAccessToken": "<your-pat-here>"
  }
}
```

**How to create a PAT:**

1. Go to `https://dev.azure.com/<your-org>/_usersSettings/tokens`
2. Click **+ New Token**
3. Select scope: **Code (Read)**
4. Copy the token

### 2. Cached token (automatic)

Tokens acquired via Azure CLI are cached locally at:

```
%LOCALAPPDATA%\REBUSS.Pure\config.json     (Windows)
~/.local/share/REBUSS.Pure/config.json      (Linux/macOS)
```

Bearer tokens are refreshed automatically when expired.

### 3. Azure CLI (recommended for interactive use)

If no PAT is configured and no valid cached token exists, the server acquires a token via:

```bash
az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798
```

**If Azure CLI is not installed:**

- During `rebuss-pure init`, the tool offers to install it automatically
- Manual install: [https://aka.ms/install-azure-cli](https://aka.ms/install-azure-cli)

### 4. Error (no auth available)

If none of the above methods work, the server returns a clear error message instructing you to run `az login` or configure a PAT.

> **Note:** Local self-review (`get_local_files`, `get_local_file_diff`) works without any authentication.

---

## Configuration

### `appsettings.json`

Located next to the server executable. All fields are optional — auto-detected from Git remote when not specified.

```json
{
  "AzureDevOps": {
    "OrganizationName": "",
    "ProjectName": "",
    "RepositoryName": "",
    "PersonalAccessToken": ""
  }
}
```

### `appsettings.Local.json`

Same structure as above. Overrides `appsettings.json`. Excluded from Git via `.gitignore`. Use this for secrets like PATs.

### Environment variables

All settings can be overridden via environment variables:

```
AzureDevOps__OrganizationName=myorg
AzureDevOps__ProjectName=myproject
AzureDevOps__RepositoryName=myrepo
AzureDevOps__PersonalAccessToken=mytoken
```

### Auto-detection

When `OrganizationName`, `ProjectName`, or `RepositoryName` are not configured, the server automatically detects them from the `origin` Git remote URL (both HTTPS and SSH formats are supported).

---

## MCP Tools Reference

### PR Review Tools (require Azure DevOps authentication)

| Tool | Description |
|---|---|
| `get_pr_metadata(prNumber)` | Returns PR title, author, state, branches, stats, commit SHAs, description |
| `get_pr_files(prNumber)` | Returns classified list of changed files with per-file stats and review priority |
| `get_pr_diff(prNumber, [format])` | Returns the complete diff for all files in the PR. Use for small PRs |
| `get_file_diff(prNumber, path, [format])` | Returns the diff for a single file. Preferred for large PRs |
| `get_file_content_at_ref(path, ref)` | Returns full file content at a specific commit/branch/tag |

### Local Self-Review Tools (no authentication needed)

| Tool | Description |
|---|---|
| `get_local_files([scope])` | Lists locally changed files with classification metadata |
| `get_local_file_diff(path, [scope])` | Returns structured diff for a single locally changed file |

**Scopes for local tools:**

| Scope | Description |
|---|---|
| `working-tree` (default) | All uncommitted changes (staged + unstaged) vs HEAD |
| `staged` | Only staged (indexed) changes vs HEAD |
| `<branch-name>` | All commits on current branch not yet merged into `<branch-name>` |

---

## Review Workflows

### PR Review

```
get_pr_metadata(prNumber)
  → get_pr_files(prNumber)
    → get_file_diff(prNumber, path)      ← per file, minimal tokens
      → get_file_content_at_ref(path, ref)  ← only when diff is insufficient
```

### Self-Review

```
get_local_files(scope)
  → get_local_file_diff(path, scope)     ← per file
```

---

## Prompts

After running `rebuss-pure init`, you get:

```
.github/prompts/
├── review-pr.md
├── self-review.md
└── create-pr.md
```

> **Note for contributors:** The files in `.github/prompts/` are **generated** by `rebuss-pure init` from embedded resources compiled into the tool (`REBUSS.Pure/Cli/Prompts/*.md`). The embedded files in `REBUSS.Pure/Cli/Prompts/` are the **source of truth**. Always edit the embedded source files and keep the `.github/prompts/` copies in sync — do **not** edit `.github/prompts/` files directly, as those changes will be lost when a user re-runs `init` on a fresh repository.

These prompts instruct the AI agent on the review and PR creation workflows. You can customize the **deployed** copies (in `.github/prompts/`) in your own repository to:

- enforce team coding standards
- adjust review priorities
- change the default self-review scope (default: `staged`)
- adjust the Azure DevOps WIQL state filter for work item auto-resolution (see below)

### `#create-pr` command

The `create-pr.md` prompt enables a `#create-pr` command in GitHub Copilot Chat.

**Trigger syntax:**

| Command | Description |
|---------|-------------|
| `123 #create-pr` | Creates a PR linked to work item 123 (Azure DevOps) or issue #123 (GitHub). |
| `#create-pr` | Creates a PR; automatically resolves the active work item for the current user. |

**How it works:**

1. Parses an optional numeric work item ID from the leading digits in the message (Step 1–2: branch detection).
2. Detects the current branch (`git rev-parse --abbrev-ref HEAD`) and base branch (upstream or repo default).
3. Resolves the work item or issue (Step 3):
   - Explicit ID: fetches metadata via `gh issue view` (GitHub) or `az boards work-item show` (Azure DevOps).
   - No ID: queries open/active items assigned to `@me`; uses the ID automatically when exactly one is found.
4. Collects local changes using the `get_local_files` and `get_local_file_diff` MCP tools (Step 4).
5. Generates a concise PR description from the work item metadata and the code diff (Step 5).
6. Asks the user whether the PR should be created as a **draft** (Step 6).
7. Creates the PR via `gh pr create` (GitHub) or `az repos pr create` (Azure DevOps), passing `--draft` when requested, and links the work item when applicable (Step 7).
8. Prints the PR URL on success, or a clear actionable error message on failure (Step 8).

**Azure DevOps WIQL state filter:**

When no work item ID is provided, the prompt queries:

```sql
[System.State] = 'Active'
```

This matches the standard Agile and Scrum process templates. Teams using custom process templates may use different state names (e.g., `'In Progress'`, `'Doing'`). To adapt, edit `.github/prompts/create-pr.md` in your repository and change the state value in the WIQL query to match your team's process.

---

## Logging

Server logs are written to daily-rotated files:

```
%LOCALAPPDATA%\REBUSS.Pure\server-yyyy-MM-dd.log   (Windows)
~/.local/share/REBUSS.Pure/server-yyyy-MM-dd.log    (Linux/macOS)
```

Logs older than 3 days are automatically cleaned up.

---

## Troubleshooting

### "AUTHENTICATION REQUIRED" error

Run `az login` and restart your IDE, or configure a PAT in `appsettings.Local.json`.

### MCP tools not available in AI chat

1. Ensure `rebuss-pure init` completed successfully
2. Check that `.vscode/mcp.json` or `.vs/mcp.json` exists
3. Restart your IDE or reload the MCP client

### Azure DevOps organization/project not detected

If your Git remote uses a non-standard format, specify explicitly:

```bash
rebuss-pure --repo . --org myorg --project myproject --repository myrepo
```

Or configure in `appsettings.Local.json`:

```json
{
  "AzureDevOps": {
    "OrganizationName": "myorg",
    "ProjectName": "myproject",
    "RepositoryName": "myrepo"
  }
}
```

### Token expired / 203 HTML redirect

The server automatically invalidates stale tokens and retries via Azure CLI. If the issue persists, re-authenticate:

```bash
az login
```

---

## 📄 License

MIT

---

## 👤 Author

**Michał Korbecki**  
Creator of REBUSS ecosystem  
[https://github.com/rebuss/CodeReview.MCP](https://github.com/rebuss/CodeReview.MCP)
