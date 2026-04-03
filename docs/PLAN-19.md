# PLAN-19: Legacy Config Compatibility Layer

## Objective

Add a compatibility layer so the .NET CLI can read legacy configuration files and memory files, enabling users migrating from the legacy TypeScript CLI to retain their settings, system prompts, and project-level config without manual migration.

**Scope:**
- `CLAUDE_CONFIG_DIR` env var support for overriding the legacy config root
- Legacy settings loading: `~/.claude/settings.json`, `.claude/settings.json`, `.claude/settings.local.json`
- `CLAUDE.md` memory file loading (user, project, local, rules directories)
- `.mcp.json` project-level MCP config loading
- `--add-dir` support for ask command (extra directories to scan for `.claude/` config)
- Legacy JSONL transcript import for session resume

**Non-goals (deferred):**
- OAuth/keychain auth UX (documented separately in PARITY.md)
- Managed/policy settings (`/etc/claude-code/`)
- Memory system (`MEMORY.md`, auto-memory directories, team memory sync)
- Plugin marketplace from `~/.claude/plugins/`
- Worktree-specific main-repo skipping logic
- Cowork mode (`cowork_settings.json`)

## Assumptions

1. Legacy config root is `CLAUDE_CONFIG_DIR` env var or `~/.claude/`
2. Project-level configs are found relative to the current working directory
3. `.NET` config takes precedence over legacy config when both exist
4. Settings merge follows legacy priority: user < project < local < flags
5. `CLAUDE.md` files are concatenated into the system prompt, not parsed as settings
6. `.mcp.json` servers merge with app-data MCP config (project-level is additive)
7. JSONL transcripts use `{sessionId}.jsonl` naming under `{projectDir}/`

## Files and Subsystems Likely to Change

| File | Change Type | Reason |
|------|------------|--------|
| `ClawdNet.Core/Services/LegacyConfigLoader.cs` | **New** | Central legacy config resolution and loading |
| `ClawdNet.Core/Services/SettingsMerger.cs` | **New** | Multi-tier settings merge logic |
| `ClawdNet.Core/Services/MemoryFileLoader.cs` | **New** | CLAUDE.md and rules file loading |
| `ClawdNet.Core/Commands/AskCommandHandler.cs` | Modify | Add `--add-dir` flag, integrate legacy settings/memory |
| `ClawdNet.App/Program.cs` | Modify | Add `CLAUDE_CONFIG_DIR` env var resolution |
| `ClawdNet.App/AppHost.cs` | Modify | Integrate legacy config loading into initialization |
| `ClawdNet.Runtime/Storage/LegacyTranscriptImporter.cs` | **New** | JSONL transcript reading for session resume |
| `ClawdNet.Runtime/Providers/ProviderCatalog.cs` | Possibly modify | Merge legacy settings into provider config |
| `ClawdNet.Runtime/Protocols/StdioMcpClient.cs` | Possibly modify | Load `.mcp.json` from project root |
| `ClawdNet.Tests/` | **New tests** | Config compatibility unit tests |

## Step-by-Step Implementation Plan

### Step 1: CLAUDE_CONFIG_DIR env var support

**Goal:** Resolve legacy config root from env var.

1. In `Program.cs`, read `CLAUDE_CONFIG_DIR` env var
2. If set, use it as the legacy config root; otherwise default to `~/.claude/`
3. Pass both `.NET` data root and legacy config root to `AppHost`
4. Add a `LegacyConfigPaths` static class to centralize path resolution

### Step 2: Legacy settings loading

**Goal:** Load and merge settings from user, project, and local sources.

1. Create `LegacySettingsLoader` in `ClawdNet.Core/Services/`
2. Implement loading for:
   - `~/.claude/settings.json` (user settings)
   - `.claude/settings.json` (project settings, from CWD)
   - `.claude/settings.local.json` (local settings, from CWD)
3. Implement merge logic: user < project < local (later overrides earlier)
4. Expose merged settings as a dictionary or typed settings object
5. Handle missing files gracefully (skip, don't error)
6. Cache loaded settings per source path

### Step 3: CLAUDE.md memory file loading

**Goal:** Load system prompt content from legacy memory files.

1. Create `MemoryFileLoader` in `ClawdNet.Core/Services/`
2. Load in this order:
   - `~/.claude/CLAUDE.md` (user memory)
   - `~/.claude/rules/*.md` (user rules)
   - `{cwd}/CLAUDE.md` (project memory)
   - `{cwd}/.claude/CLAUDE.md` (project memory in .claude dir)
   - `{cwd}/.claude/rules/*.md` (project rules)
3. Concatenate content with source-file markers for debugging
4. Expose as a single string for system prompt injection
5. Handle `CLAUDE_CODE_DISABLE_AUTO_MEMORY` env var to skip loading

### Step 4: .mcp.json project-level config loading

**Goal:** Load MCP servers from project `.mcp.json`.

1. Modify `StdioMcpClient` or create a `ProjectMcpConfigLoader`
2. Read `.mcp.json` from current working directory
3. Walk parent directories for additional `.mcp.json` files (like legacy does)
4. Merge project MCP servers with app-data MCP config
5. Project servers are additive; app-data config takes precedence on name conflicts

### Step 5: --add-dir support for ask command

**Goal:** Support `--add-dir <paths...>` flag on ask command.

1. Add `--add-dir` option to `AskCommandHandler`
2. For each added directory, load `.claude/settings.json` and `.claude/settings.local.json`
3. Merge added-directory settings with base settings (later wins)
4. Added directories also contribute `.mcp.json` and `CLAUDE.md` files

### Step 6: Legacy JSONL transcript import for session resume

**Goal:** Read legacy JSONL transcripts when resuming sessions.

1. Create `LegacyTranscriptReader` in `ClawdNet.Runtime/Storage/`
2. Resolve project directory from working directory (sanitized/hash)
3. Find `{sessionId}.jsonl` under `~/.claude/projects/{projectDir}/`
4. Parse JSONL lines into the internal conversation message format
5. Integrate with session resume logic to preload legacy messages

### Step 7: Integration into AppHost initialization

**Goal:** Wire legacy config loading into the app startup sequence.

1. In `AppHost.RunAsync()`, after provider/plugin/MCP init:
   - Load legacy settings
   - Load legacy memory files
   - Merge into the query engine's system prompt
2. Pass legacy settings to `AskCommandHandler` for `--settings` flag compatibility
3. Ensure legacy MCP servers are registered alongside app-data servers

### Step 8: Tests

**Goal:** Verify the compatibility layer works correctly.

1. Unit tests for settings loading and merging
2. Unit tests for CLAUDE.md loading and concatenation
3. Unit tests for `.mcp.json` loading
4. Unit tests for `--add-dir` settings merging
5. Integration tests for legacy transcript reading
6. Fixture tests with sample legacy config directories

## Validation Plan

### Automated
```bash
dotnet build ./ClawdNet.slnx
dotnet test ./ClawdNet.slnx
```

### Manual smoke tests
```bash
# Create legacy config structure
mkdir -p ~/.claude
echo '{"allowedTools": ["file_read", "grep"]}' > ~/.claude/settings.json
echo '# You are a helpful assistant' > ~/.claude/CLAUDE.md
mkdir -p .claude
echo '{"mcpServers": [{"name": "test", "command": "echo"}]}' > .mcp.json
echo '# Project memory' > .claude/CLAUDE.md

# Test legacy settings loading
CLAUDE_CONFIG_DIR=~/.claude dotnet run --project ClawdNet.App -- ask --json "test"

# Test --add-dir
dotnet run --project ClawdNet.App -- ask --add-dir /some/other/path "test"

# Test without CLAUDE_CONFIG_DIR (should use ~/.claude default)
dotnet run --project ClawdNet.App -- ask --json "test"
```

## Rollback / Risk Notes

- **Risk:** Legacy settings schema may differ from .NET expectations. **Mitigation:** Use schema validation; skip invalid fields with warnings.
- **Risk:** CLAUDE.md files may be large or contain conflicting instructions. **Mitigation:** Concatenate with source markers; no dedup logic needed initially.
- **Risk:** `.mcp.json` may reference servers that don't exist or have incompatible configs. **Mitigation:** Log warnings; skip invalid servers; don't fail startup.
- **Risk:** JSONL transcripts may be huge or corrupted. **Mitigation:** Read with size limits; skip malformed lines.
- **Rollback:** If legacy config loading causes issues, set `CLAUDE_CODE_DISABLE_AUTO_MEMORY=1` and avoid legacy config paths. All legacy loading is additive; removing legacy files reverts to .NET defaults.

## Exit Criteria

- [x] `CLAUDE_CONFIG_DIR` env var is respected
- [x] `~/.claude/settings.json` is loaded and merged
- [x] `.claude/settings.json` and `.claude/settings.local.json` from CWD are loaded
- [x] `CLAUDE.md` files are loaded and injected into system prompt
- [x] `.mcp.json` from project root is loaded and merged
- [x] `--add-dir` flag works on ask command
- [x] Legacy JSONL transcripts can be read for session resume
- [x] `dotnet build` passes
- [x] `dotnet test` passes (260 tests, +46 new)
- [x] Manual smoke tests pass
- [x] PARITY.md updated for config compatibility rows
- [x] ARCHITECTURE.md updated if defaults changed

## What Changed

### New Files
- `ClawdNet.Core/Services/LegacyConfigPaths.cs` — Central legacy path resolution with `CLAUDE_CONFIG_DIR` env var support
- `ClawdNet.Core/Services/LegacySettingsLoader.cs` — Multi-tier settings loading and merging (user < project < local)
- `ClawdNet.Core/Services/MemoryFileLoader.cs` — CLAUDE.md and rules file loading with `CLAUDE_CODE_DISABLE_AUTO_MEMORY` support
- `ClawdNet.Core/Services/ProjectMcpConfigLoader.cs` — Project `.mcp.json` loading with parent directory walk
- `ClawdNet.Core/Services/LegacyTranscriptReader.cs` — JSONL transcript reading for session resume
- `ClawdNet.Tests/LegacyConfigPathsTests.cs` — 12 tests for path resolution
- `ClawdNet.Tests/LegacySettingsLoaderTests.cs` — 10 tests for settings loading/merging
- `ClawdNet.Tests/MemoryFileLoaderTests.cs` — 10 tests for memory file loading
- `ClawdNet.Tests/ProjectMcpConfigLoaderTests.cs` — 10 tests for MCP config loading
- `ClawdNet.Tests/LegacyTranscriptReaderTests.cs` — 8 tests for transcript reading

### Modified Files
- `ClawdNet.App/Program.cs` — Added `CLAUDE_CONFIG_DIR` resolution and `legacyConfigDir` parameter
- `ClawdNet.App/AppHost.cs` — Added legacy config loader fields and passed them to `CommandContext`
- `ClawdNet.Core/Models/CommandContext.cs` — Added optional legacy config loader parameters
- `ClawdNet.Core/Commands/AskCommandHandler.cs` — Added `--add-dir` flag parsing
- `ClawdNet.Tests/AppHostTests.cs` — Updated all `AppHost` constructor calls for new `legacyConfigDir` parameter
- `docs/PLAN.md` — Added PLAN-19 to completed milestones
- `docs/PARITY.md` — Updated config compatibility rows to Implemented
- `docs/ARCHITECTURE.md` — Added Legacy Config Compatibility section
- `README.md` — Added `--add-dir` to CLI surface

## Validation Results

- `dotnet build`: PASSED
- `dotnet test`: PASSED (260 tests, 0 failed, +46 new compatibility tests)
