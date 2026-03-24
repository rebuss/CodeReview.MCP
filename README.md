# 🚀 REBUSS.Pure – AI Code Review That Focuses Only on What Matters

**Stop sending irrelevant code to AI.**  
Send only the *right context* — and understand Pull Requests faster.

---

## 💡 What is this?

`REBUSS.Pure` is a lightweight MCP server that enables AI agents (GitHub Copilot, ChatGPT, Claude) to perform **high-signal code reviews** by providing only the context that actually matters.

Instead of overwhelming the model with your entire repository, REBUSS.Pure:

- 🔍 analyzes **Azure DevOps and GitHub Pull Requests**
- 📄 provides **only relevant code changes**
- 🧠 enables **focused code review & self-review**
- ⚡ delivers **minimal, high-signal context**

---

## 🎯 Why this exists

Most AI workflows today:

- ❌ send too much code
- ❌ drown the model in noise
- ❌ produce generic, low-quality feedback

REBUSS.Pure changes the approach:

- ✅ sends **only relevant context**
- ✅ reduces noise, not just tokens
- ✅ helps AI focus on **what actually matters**

👉 built for **real-world code review**, not demos

---

## 🧠 Core idea

AI doesn’t need more code.  
It needs the *right* code.

Instead of:

```
❌ full repo → LLM
```

You get:

```
LLM → MCP → high-signal context only
```

---

## ✨ Key Features

- 🔹 Azure DevOps and GitHub Pull Request integration
- 🔹 High-signal, diff-based AI context
- 🔹 Local self-review (no network required)
- 🔹 No repo cloning needed
- 🔹 Incremental, on-demand data access
- 🔹 Ready-to-use review prompts
- 🔹 Works with any MCP-compatible agent
- 🔹 Authentication via Azure CLI, GitHub CLI (`gh auth`), or PAT
- 🔹 Auto-detects VS Code and Visual Studio
- 🔹 Auto-detects provider from Git remote URL

---

## 🔒 Security & Privacy

**Your source code never leaves your machine.**

REBUSS.Pure runs as a **local process** on your workstation. It does not upload, store, or relay your code to any external service. The MCP server acts as a controlled gateway between your AI agent and the data it actually needs:

- **Local processing only** — the server runs on `localhost`; no outbound code transmission occurs.
- **Minimal data exposure** — the AI model receives only **relevant context**, not the full repository.
- **Azure DevOps stays yours** — when fetching PR data, requests go directly to **your organization's** Azure DevOps APIs using **your credentials**. No intermediary services are involved.
- **GitHub stays yours** — GitHub API requests go directly to `api.github.com` using **your personal access token**. No intermediary services are involved.
- **Offline self-review** — local review (`#self-review`) operates entirely without network access. Git operations run against your local repository; nothing is sent anywhere.
- **No telemetry, no tracking** — the server collects zero usage data and phones home to nobody.

> **In short:** REBUSS.Pure gives AI agents *precise, scoped access* to exactly the context they need — and nothing more.

---

## 🆚 Compared to typical AI workflows

| Feature | REBUSS.Pure | Typical approach |
|---------|-------------|------------------|
| Context quality | High-signal | Noisy |
| Context size | Minimal | Huge |
| Token usage | Efficient | Wasteful |
| Setup | 1 command | Complex |
| Review quality | Focused | Generic |
| Data privacy | Code stays local | Full repo sent to AI |

---

# ⚡ Quick Start

## 1. Install

### Option A — .NET global tool (recommended)

```bash
dotnet tool install -g CodeReview.MCP
```

### Option B - PowerShell

```powershell
irm https://raw.githubusercontent.com/rebuss/CodeReview.MCP/master/install.ps1 | iex
```

### Option C - Bash

```bash
curl -fsSL https://raw.githubusercontent.com/rebuss/CodeReview.MCP/master/install.sh | bash
```

---

## 2. Initialize in your repo

```bash
cd /path/to/your/repo
rebuss-pure init
```

This will:

- ✔ detect your IDE (VS Code → `.vscode/mcp.json`, Visual Studio → `.vs/mcp.json`)
- ✔ generate MCP server configuration
- ✔ copy review prompts to `.github/prompts/`
- ✔ authenticate via Azure CLI (opens browser for login) or accept a GitHub PAT

---

## 3. Review a Pull Request

In Copilot / AI chat:

```
123 #review-pr
```

Where `123` is the Azure DevOps or GitHub Pull Request number.

---

## 4. Self-review local changes

```
#self-review
```

Works **offline** — no Azure DevOps connection required.

---

## 🧪 Running Tests

### Unit tests

```bash
dotnet test REBUSS.Pure.Tests
dotnet test REBUSS.Pure.Core.Tests
dotnet test REBUSS.Pure.AzureDevOps.Tests
dotnet test REBUSS.Pure.GitHub.Tests
```

### Smoke tests

Smoke tests exercise the compiled binary as a child process — covering the `init` command (GitHub & Azure DevOps), MCP protocol tools over stdio, and a full pack → install → handshake flow.

```bash
dotnet test REBUSS.Pure.SmokeTests
```

### Contract tests

Live contract tests run the compiled binary against **real Azure DevOps and GitHub APIs** using dedicated fixture PRs that are never merged. They validate the full stack: CLI arg parsing → DI → provider → API → response structure.

**Protocol tests** (no credentials needed):

```bash
dotnet test REBUSS.Pure.SmokeTests --filter "Category=Protocol"
```

**Azure DevOps contract tests** (requires env vars):

```bash
REBUSS_ADO_PAT=<pat> REBUSS_ADO_ORG=<org> REBUSS_ADO_PROJECT=<project> \
REBUSS_ADO_REPO=<repo> REBUSS_ADO_PR_NUMBER=<pr> \
dotnet test REBUSS.Pure.SmokeTests --filter "Category=ContractAdo"
```

**GitHub contract tests** (requires env vars):

```bash
REBUSS_GH_PAT=<pat> REBUSS_GH_OWNER=<owner> \
REBUSS_GH_REPO=<repo> REBUSS_GH_PR_NUMBER=<pr> \
dotnet test REBUSS.Pure.SmokeTests --filter "Category=ContractGitHub"
```

When credentials are not configured, contract tests are **automatically skipped**.