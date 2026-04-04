# PLAN-28: Plugin Lifecycle Parity v2

## Objective

Decide and document the supported local plugin lifecycle contract for ClawdNet, implement missing local lifecycle commands that are low-to-medium effort, and explicitly document marketplace/update/scope behavior as out of scope or deferred.

**Scope:**
1. Decide the supported local plugin lifecycle contract
2. Implement `plugin validate <path>` command (medium effort, high parity value)
3. Implement `plugin disable --all` flag (low effort)
4. Implement `plugin uninstall --keep-data` flag (low effort)
5. Decide and document the migration position for marketplace and update behavior
6. Decide and document the scope model position (single-scope vs multi-scope)
7. Update PARITY.md, ARCHITECTURE.md, and PLAN.md accordingly

**Non-goals (explicitly out of scope):**
- Marketplace add/list/remove/update commands (high effort, distribution surface)
- Plugin update command (requires marketplace first)
- Multi-scope install/enable/disable (high effort, requires settings layers)
- `plugin list --available` (requires marketplace)
- Reverse dependency warnings (medium effort, low immediate value)
- Plugin configuration storage (separate concern)
- Organization policy checks (enterprise concern)
- `--json` output flag (deferred, low priority)

## Assumptions

1. Local filesystem plugin installation is the supported contract for now
2. Marketplace distribution is a separate product decision and should not block migration acceptance
3. Single-scope (app-data plugins directory) is the supported model for now
4. `plugin validate <path>` is a high-value command for plugin authors and parity
5. Small CLI flag additions (`--all`, `--keep-data`) improve migration acceptance with minimal risk

## Files and Subsystems Likely to Change

| File | Change Type | Reason |
|------|------------|--------|
| `ClawdNet.Core/Commands/PluginCommandHandler.cs` | Modify | Add validate subcommand, --all flag, --keep-data flag, update help text |
| `ClawdNet.Runtime/Plugins/PluginCatalog.cs` | Modify | Add ValidateAsync method for standalone validation |
| `ClawdNet.Core/Abstractions/IPluginCatalog.cs` | Modify | Add ValidateAsync to interface |
| `ClawdNet.Core/Models/PluginManifest.cs` | Review | Ensure manifest model captures all fields needed for validation |
| `ClawdNet.Core/Models/PluginDefinition.cs` | Review | Ensure definition model is complete |
| `ClawdNet.Tests/PluginCommandHandlerTests.cs` | Create/Modify | Tests for validate, --all, --keep-data |
| `docs/PARITY.md` | Update | Move plugin lifecycle rows to resolved states |
| `docs/ARCHITECTURE.md` | Update | Record plugin lifecycle decision |
| `docs/PLAN.md` | Update | Mark milestone complete |
| `docs/PLAN-28.md` | Create | This execution plan |

## Step-by-Step Implementation Plan

### Step 1: Implement `plugin validate <path>` command
- Add `ValidateAsync(path)` method to `IPluginCatalog` and `PluginCatalog`
- Validate: manifest exists, required fields present, extension entries shape, no reserved name conflicts
- Return structured validation result (errors/warnings with messages)
- Add `validate` subcommand to `PluginCommandHandler`
- Output validation result as human-readable text
- Exit code: 0 if valid, 1 if errors, 0 with warnings if warnings-only

### Step 2: Add `plugin disable --all` flag
- Add `--all` option parsing to `PluginCommandHandler` disable subcommand
- Enumerate all plugins and set `enabled: false` on each
- Output summary of disabled plugins

### Step 3: Add `plugin uninstall --keep-data` flag
- Add `--keep-data` option parsing to `PluginCommandHandler` uninstall subcommand
- When set, move plugin directory to a `.uninstalled/` staging area instead of deleting
- When not set, behavior unchanged (delete directory)

### Step 4: Document marketplace/update/scope decisions
- Add intentional deviation entries in PARITY.md for marketplace, update, and scope
- Update ARCHITECTURE.md plugin section to document single-scope, no-marketplace decision

### Step 5: Update documentation
- Update PARITY.md: move plugin lifecycle rows to verified/intentionally deferred
- Update ARCHITECTURE.md: record plugin lifecycle scope decisions
- Update PLAN.md: mark milestone as [v]

## Validation Plan

### Automated
```bash
dotnet build ./ClawdNet.slnx
dotnet test ./ClawdNet.slnx
```

### Manual smoke tests
```bash
# Validate a valid plugin directory
dotnet run --project ClawdNet.App -- plugin validate /path/to/plugin

# Validate an invalid path
dotnet run --project ClawdNet.App -- plugin validate /tmp/nonexistent-plugin

# Disable all plugins
dotnet run --project ClawdNet.App -- plugin disable --all

# Uninstall with keep-data
dotnet run --project ClawdNet.App -- plugin uninstall <name> --keep-data

# Existing commands should still work
dotnet run --project ClawdNet.App -- plugin list
dotnet run --project ClawdNet.App -- plugin show <name>
```

## Rollback / Risk Notes

- **Risk:** validate logic may diverge from load-time validation. **Mitigation:** Reuse same validation paths used during catalog load
- **Risk:** --keep-data creates unbounded disk usage. **Mitigation:** .uninstalled/ is informational only; user can manually clean; document it clearly
- **Risk:** marketplace deferral may later prove wrong. **Mitigation:** Decision is documented clearly so it can be revisited
- **Rollback:** All changes are in version control

## Exit Criteria

- [x] `plugin validate <path>` implemented and tested
- [x] `plugin disable --all` implemented
- [x] `plugin uninstall --keep-data` implemented
- [x] Marketplace/update/scope documented as deferred/intentional deviations
- [x] PARITY.md plugin lifecycle rows resolved
- [x] ARCHITECTURE.md updated
- [x] PLAN.md milestone marked complete
- [x] dotnet build passes
- [x] dotnet test passes
- [x] Changes committed
