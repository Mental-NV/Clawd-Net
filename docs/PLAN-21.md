# PLAN-21: Safety and Approval UX Parity v1

## Objective

Broaden TUI safety surfaces beyond the current approval/edit overlays to meet the "Safety and Approval UX Parity v1" milestone.

**Scope:**
1. Add `/permissions` command to TUI/REPL to view current permission mode and tool allow/deny state
2. Add bypass-permissions warning when entering that mode interactively
3. Add trust/permission mode display in the TUI context pane (current mode + active tool filters)
4. Add a `/config` command to view current configuration state (provider, model, permission mode, session)

**Non-goals (deferred):**
- Hook config views (requires plugin hook introspection UI)
- Interactive permission editing (this is view-only v1)
- Per-tool "remember decision" memory
- Rich diff viewer with syntax highlighting
- Settings file browser/editor

## Assumptions

1. The permission engine (`DefaultPermissionService`) is complete and correct
2. The overlay system (`TuiOverlayState`, `TuiOverlayKind`) is structurally sound
3. `TuiHost.cs` and `ReplHost.cs` share the same `QueryEngine` and permission model
4. Tool allow/deny lists are applied at query time in `QueryEngine`
5. Current permission mode is already shown in the footer via `FormatPermissionMode()`
6. The `/permissions` command should be view-only for v1 (not interactive editing)

## Files and Subsystems Likely to Change

| File | Change Type | Reason |
|------|------------|--------|
| `ClawdNet.Terminal/Tui/TuiHost.cs` | Modify | Add `/permissions` slash command handler, add config display overlay |
| `ClawdNet.Terminal/Repl/ReplHost.cs` | Modify | Add `/permissions` slash command handler for REPL parity |
| `ClawdNet.Terminal/Models/TuiOverlayKind.cs` | Modify | Add `Permissions` and `Config` overlay kinds |
| `ClawdNet.Terminal/Models/TuiDrawerKind.cs` | Possibly modify | Add `Settings` drawer if needed |
| `ClawdNet.Terminal/Rendering/ConsoleTranscriptRenderer.cs` | Possibly modify | Enhance permission mode display in context pane |
| `ClawdNet.Core/Services/QueryEngine.cs` | Read only | Understand how tool filters are applied |
| `ClawdNet.Tests/TuiHostTests.cs` | Possibly modify | Add tests for new slash commands |
| `ClawdNet.Tests/ReplHostTests.cs` | Possibly modify | Add tests for new slash commands |

## Step-by-Step Implementation Plan

### Step 1: Add Permissions and Config Overlay Kinds

**Goal:** Extend the overlay enum to support the new surfaces.

1. Add `Permissions` and `Config` to `TuiOverlayKind` enum
2. These will be view-only informational overlays

### Step 2: Implement `/permissions` Command in TUI

**Goal:** Show current permission mode, tool categories, and active allow/deny lists.

1. In `TuiHost.cs`, add `/permissions` to the slash command handler
2. Build an informational overlay showing:
   - Current permission mode
   - Tool category summary (how many ReadOnly/Write/Execute tools)
   - Active allow/deny lists (from the current QueryRequest if available)
   - Brief explanation of what each mode means
3. Use `TuiOverlayKind.Permissions` with `RequiresConfirmation = false` (informational)

### Step 3: Implement `/permissions` Command in REPL

**Goal:** Same information as TUI but rendered as plain text.

1. In `ReplHost.cs`, add `/permissions` to the slash command handler
2. Print the same permission state information as plain text
3. Keep it consistent with TUI output

### Step 4: Implement `/config` Command in TUI

**Goal:** Show current configuration state.

1. In `TuiHost.cs`, add `/config` to the slash command handler
2. Build an informational overlay showing:
   - Current provider and model
   - Current session ID
   - Current permission mode
   - Config file paths (providers.json, mcp.json, etc.)
3. Use `TuiOverlayKind.Config` with `RequiresConfirmation = false`

### Step 5: Implement `/config` Command in REPL

**Goal:** Same information as TUI but rendered as plain text.

1. In `ReplHost.cs`, add `/config` to the slash command handler
2. Print the same configuration state as plain text

### Step 6: Add Bypass-Permissions Warning

**Goal:** When user enters bypass-permissions mode, show a warning.

1. In `TuiHost.cs`, when session starts with `BypassPermissions` mode, show a brief warning in the context pane or as a transient overlay
2. In `ConsoleTranscriptRenderer.cs`, enhance the `FormatPermissionMode()` output to include a warning indicator for bypass mode
3. Keep it non-intrusive -- a single line indicator, not a blocking dialog

### Step 7: Tests

**Goal:** Verify new commands work correctly.

1. Unit test: `/permissions` shows mode and tool summary
2. Unit test: `/config` shows provider/model/session
3. Manual smoke: verify TUI overlay rendering
4. Manual smoke: verify REPL text output

## Validation Plan

### Automated
```bash
dotnet build ./ClawdNet.slnx
dotnet test ./ClawdNet.slnx
```

### Manual smoke tests
```bash
# Test TUI /permissions command
dotnet run --project ClawdNet.App --
# Then type: /permissions

# Test TUI /config command  
# Then type: /config

# Test REPL /permissions command
dotnet run --project ClawdNet.App -- --feature legacy-repl
# Then type: /permissions

# Test bypass-permissions warning
dotnet run --project ClawdNet.App -- --permission-mode bypass-permissions
# Verify warning is visible in context pane/footer
```

## Rollback / Risk Notes

- **Risk:** TUI overlay system complexity grows. **Mitigation:** Keep new overlays informational and simple; reuse existing overlay infrastructure.
- **Risk:** Tool count may not be available before query engine initializes. **Mitigation:** Show "pending" or omit tool counts if not available yet.
- **Rollback:** New slash commands are additive; they don't change existing behavior. Safe to revert if issues arise.

## Exit Criteria

- [x] `/permissions` command works in TUI and REPL
- [x] `/config` command works in TUI and REPL
- [x] Bypass-permissions mode shows visible warning
- [x] `dotnet build` passes
- [x] `dotnet test` passes
- [x] `PARITY.md` updated: Safety UI row status changed to "Implemented", Config UI changed to "In Progress"
- [x] `PLAN.md` milestone status updated to `[v]`

## What Changed

### Modified Files
- `ClawdNet.Terminal/Models/TuiOverlayKind.cs` — Added `Permissions` and `Config` overlay kinds
- `ClawdNet.Terminal/Defaults/TerminalFallbackServices.cs` — Added `TerminalFallbackToolRegistry` for TUI/REPL fallback when no tool registry is injected
- `ClawdNet.Terminal/Tui/TuiHost.cs` — Added `IToolRegistry` dependency, `/permissions` and `/config` slash commands with `ShowPermissionsOverlay()` and `ShowConfigOverlay()` methods, updated help overlay to list new commands
- `ClawdNet.Terminal/Repl/ReplHost.cs` — Added `IToolRegistry` dependency, `/permissions` and `/config` slash commands with `ShowPermissionsInfo()` and `ShowConfigInfo()` methods, updated help text
- `ClawdNet.Terminal/Rendering/ConsoleTranscriptRenderer.cs` — Changed `FormatPermissionMode` to `internal`, added `!` suffix for bypass-permissions mode as visible warning
- `ClawdNet.Terminal/Rendering/ConsoleTuiRenderer.cs` — Added permission mode display at top of context pane
- `ClawdNet.App/AppHost.cs` — Passed `_toolRegistry` to `TuiHost` and `ReplHost` constructors

### New Behavior
- `/permissions` in TUI: Shows overlay with current mode description, tool category counts (ReadOnly/Write/Execute), auto-allowed vs requires-approval status, and explicit bypass-permissions warning
- `/permissions` in REPL: Shows same information as text in the activity area
- `/config` in TUI: Shows overlay with provider, model, session ID, and permission mode
- `/config` in REPL: Shows same information as text in the activity area
- Bypass-permissions mode now shows `bypass-permissions!` in the footer (with `!` warning suffix)
- Context pane in TUI now always shows the current permission mode at the top

## Validation Results

- `dotnet build`: PASSED (0 warnings, 0 errors)
- `dotnet test`: PASSED (260 tests, 0 failed)
