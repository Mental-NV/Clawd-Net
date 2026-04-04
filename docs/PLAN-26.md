# PLAN-26: Own Settings Only Refactor v1

## Objective

Remove all legacy TypeScript settings compatibility code from the active .NET runtime so that ClawdNet has only its own app-owned configuration contract.

**Scope:**
1. Remove `--add-dir` flag and all legacy directory scanning from the ask command
2. Remove legacy settings loading (`LegacySettingsLoader`) from the active runtime path
3. Remove legacy memory/CLAUDE.md loading (`MemoryFileLoader`) from the active runtime path
4. Remove legacy project MCP config loading (`ProjectMcpConfigLoader`) from the active runtime path
5. Remove `LegacyConfigPaths`, `LegacyTranscriptReader` and all transitional helpers
6. Remove legacy loader fields from `CommandContext` and `AppHost`
7. Remove legacy config dir resolution from `Program.cs`
8. Delete legacy compatibility test files
9. Update documentation to reflect app-only configuration contract

**Non-goals (explicitly out of scope):**
- Changing the app-owned config layout under `<LocalApplicationData>/ClawdNet`
- Changing how `--settings` works for app-native explicit settings input
- Legacy JSONL transcript import/resume (separate decision, remains deferred)
- Any new feature work

## Assumptions

1. Legacy settings compatibility was always intended as transitional, as documented in ARCHITECTURE.md
2. The `--add-dir` flag exists solely to support legacy `.claude/` directory scanning
3. Memory files (CLAUDE.md) and project `.mcp.json` loading are legacy surfaces, not app-owned config
4. Removing these paths does not affect the app's own `config/` directory loading
5. The interactive TUI/REPL do not directly use these legacy loaders (only the ask command does)

## Files and Subsystems Likely to Change

| File | Change Type | Reason |
|------|------------|--------|
| `ClawdNet.Core/Commands/AskCommandHandler.cs` | Modify | Remove `--add-dir` flag, legacy settings/memory loading |
| `ClawdNet.Core/Models/QueryRequest.cs` | Modify | Remove `AddDirs` field |
| `ClawdNet.Core/Models/CommandContext.cs` | Modify | Remove legacy loader fields |
| `ClawdNet.App/AppHost.cs` | Modify | Remove legacy loader fields and initialization |
| `ClawdNet.App/Program.cs` | Modify | Remove legacy config dir resolution |
| `ClawdNet.Core/Services/LegacyConfigPaths.cs` | Delete | Legacy path resolution |
| `ClawdNet.Core/Services/LegacySettingsLoader.cs` | Delete | Legacy settings merging |
| `ClawdNet.Core/Services/MemoryFileLoader.cs` | Delete | Legacy CLAUDE.md loading |
| `ClawdNet.Core/Services/ProjectMcpConfigLoader.cs` | Delete | Legacy .mcp.json loading |
| `ClawdNet.Core/Services/LegacyTranscriptReader.cs` | Delete | Legacy JSONL transcript reading |
| `ClawdNet.Tests/LegacyConfigPathsTests.cs` | Delete | Tests for deleted service |
| `ClawdNet.Tests/LegacySettingsLoaderTests.cs` | Delete | Tests for deleted service |
| `ClawdNet.Tests/MemoryFileLoaderTests.cs` | Delete | Tests for deleted service |
| `ClawdNet.Tests/ProjectMcpConfigLoaderTests.cs` | Delete | Tests for deleted service |
| `ClawdNet.Tests/LegacyTranscriptReaderTests.cs` | Delete | Tests for deleted service |
| `docs/PARITY.md` | Update | Remove legacy settings as unresolved gap |
| `docs/ARCHITECTURE.md` | Update | Remove transitional code references |
| `docs/PLAN.md` | Update | Mark milestone complete |
| `README.md` | Update | Remove `--add-dir` from supported surface |

## Step-by-Step Implementation Plan

### Step 1: Remove `--add-dir` from AskCommandHandler
- Remove `--add-dir` case from the argument parser
- Remove `AddDirs` from `AskOptions` record
- Simplify `LoadSettingsAndMemoryAsync` to only handle `--settings` (app-native)
- Remove `ComposeSystemPrompt` memory content composition (no legacy memory files)
- Update help text to remove `--add-dir` documentation

### Step 2: Remove `AddDirs` from QueryRequest
- Remove the `AddDirs` field from the `QueryRequest` record
- Update all call sites that construct `QueryRequest` (AskCommandHandler)

### Step 3: Remove legacy loaders from CommandContext
- Remove `LegacySettingsLoader`, `MemoryFileLoader`, `ProjectMcpConfigLoader` optional fields from the record

### Step 4: Remove legacy loaders from AppHost
- Remove `_legacySettingsLoader`, `_memoryFileLoader`, `_projectMcpConfigLoader` fields
- Remove their instantiation in the constructor
- Remove them from `CommandContext` construction
- Remove `legacyConfigDir` parameter from constructor (no longer needed)

### Step 5: Remove legacy config resolution from Program.cs
- Remove `LegacyConfigPaths.GetLegacyConfigDir()` call
- Remove `legacyConfigDir` argument from `AppHost` construction

### Step 6: Delete legacy service files
- Delete all 5 legacy compatibility service files from `ClawdNet.Core/Services/`

### Step 7: Delete legacy test files
- Delete all 5 legacy test files from `ClawdNet.Tests/`

### Step 8: Update documentation
- Update PARITY.md: mark legacy settings compatibility as resolved/removed
- Update ARCHITECTURE.md: remove transitional code references
- Update PLAN.md: mark milestone as [v]
- Update README.md: remove `--add-dir` from command surface

## Validation Plan

### Automated
```bash
dotnet build ./ClawdNet.slnx
dotnet test ./ClawdNet.slnx
```

### Manual smoke tests
```bash
# Verify ask still works without --add-dir
dotnet run --project ClawdNet.App -- ask "hello"

# Verify --settings still works (app-native)
dotnet run --project ClawdNet.App -- ask --settings '{"allowedTools":["echo"]}' "hello"

# Verify --add-dir is no longer accepted
dotnet run --project ClawdNet.App -- ask --add-dir /tmp "hello"  # should fail or be treated as prompt

# Verify help no longer shows --add-dir
dotnet run --project ClawdNet.App -- ask --help

# Verify interactive mode still launches
dotnet run --project ClawdNet.App -- --help
```

## Rollback / Risk Notes

- **Risk:** Legacy loaders may be used in unexpected code paths. **Mitigation:** Search all references before deletion, verify no interactive flow depends on them
- **Risk:** `--add-dir` removal may break users who relied on it. **Mitigation:** This is an intentional deviation documented in PARITY.md
- **Risk:** Memory content (CLAUDE.md) may have been providing useful context. **Mitigation:** App-owned config is the supported contract; legacy memory was transitional
- **Rollback:** All deletions are in version control; reverting the commit restores the code

## Exit Criteria

- [x] Legacy settings compatibility code fully removed from active runtime
- [x] `--add-dir` no longer supported
- [x] `dotnet build` passes (0 errors, 0 warnings)
- [x] `dotnet test` passes (226 tests, 0 failures)
- [x] PARITY.md updated to remove legacy settings as unresolved gap
- [x] ARCHITECTURE.md updated to remove transitional code references
- [x] PLAN.md updated to mark milestone complete
- [x] README.md checked (no `--add-dir` references found)
- [x] Changes committed

## Implementation Summary

### What was done:
1. Removed `--add-dir` flag and directory scanning from `AskCommandHandler`
2. Simplified `LoadSettingsAndMemoryAsync` to handle only `--settings` (app-native)
3. Removed `ComposeSystemPrompt` memory content composition
4. Removed `AddDirs` from `QueryRequest` record
5. Removed `LegacySettingsLoader`, `MemoryFileLoader`, `ProjectMcpConfigLoader` from `CommandContext`
6. Removed legacy loader fields and initialization from `AppHost`
7. Removed `legacyConfigDir` parameter from `AppHost` constructor
8. Removed `LegacyConfigPaths.GetLegacyConfigDir()` call from `Program.cs`
9. Deleted all 5 legacy compatibility service files
10. Deleted all 5 legacy test files
11. Fixed `QueryRequest` positional argument counts in `TuiHost`, `ReplHost`, and all test files
12. Updated `PARITY.md`, `ARCHITECTURE.md`, and `PLAN.md`

### What changed:
- `ClawdNet.Core/Commands/AskCommandHandler.cs` - removed `--add-dir`, legacy loaders, memory composition
- `ClawdNet.Core/Models/QueryRequest.cs` - removed `AddDirs` field
- `ClawdNet.Core/Models/CommandContext.cs` - removed legacy loader fields
- `ClawdNet.App/AppHost.cs` - removed legacy loader fields, initialization, and constructor parameter
- `ClawdNet.App/Program.cs` - removed legacy config dir resolution
- `ClawdNet.Terminal/Tui/TuiHost.cs` - fixed `QueryRequest` positional args
- `ClawdNet.Terminal/Repl/ReplHost.cs` - fixed `QueryRequest` positional args
- `ClawdNet.Tests/AppHostTests.cs` - fixed `AppHost` constructor calls
- `ClawdNet.Tests/Commands/ReportingCommandsTests.cs` - fixed `AppHost` constructor calls
- Deleted: `ClawdNet.Core/Services/LegacyConfigPaths.cs`
- Deleted: `ClawdNet.Core/Services/LegacySettingsLoader.cs`
- Deleted: `ClawdNet.Core/Services/MemoryFileLoader.cs`
- Deleted: `ClawdNet.Core/Services/ProjectMcpConfigLoader.cs`
- Deleted: `ClawdNet.Core/Services/LegacyTranscriptReader.cs`
- Deleted: `ClawdNet.Tests/LegacyConfigPathsTests.cs`
- Deleted: `ClawdNet.Tests/LegacySettingsLoaderTests.cs`
- Deleted: `ClawdNet.Tests/MemoryFileLoaderTests.cs`
- Deleted: `ClawdNet.Tests/ProjectMcpConfigLoaderTests.cs`
- Deleted: `ClawdNet.Tests/LegacyTranscriptReaderTests.cs`

### Remaining follow-ups:
- Legacy JSONL transcript import/resume remains a separate migration decision (Legacy Transcript Import Decision v1 in PLAN.md)
- `CLAUDE_CONFIG_DIR` env var is no longer recognized by the .NET CLI
