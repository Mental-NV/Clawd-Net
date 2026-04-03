# PLAN-04: Full TUI Parity v3, Slice 1

## Objective

Land the first implementation slice of `Full TUI Parity v3` by adding the most impactful bounded TUI improvements: session status display, session rename, context inspection, root positional prompt shorthand, and startup session resume picker.

## Scope

This slice covers:

- `/status` slash command showing current session state (provider, model, permission mode, message count, task count)
- `/rename <name>` slash command to update session title
- `/context` slash command showing session context summary
- Root positional prompt shorthand: `clawdnet "hello"` pre-fills TUI composer
- Startup session resume: when prior sessions exist and no `--session` is provided, show session drawer on launch

This slice does not attempt:

- interactive model picker (`/model`)
- effort/budget controls (`/effort`)
- permission tool allow/deny management (`/permissions`)
- settings editor (`/config`)
- diff viewer (`/diff`)
- copy to clipboard (`/copy`)
- theme/color/output-style customization
- diagnostic commands (`/doctor`, `/stats`, `/cost`)
- legacy config compatibility layer
- `--help` CLI flag
- command aliases

## Assumptions and Non-Goals

- All changes are additive; existing TUI behavior remains backward-compatible.
- Session title changes persist to the conversation store.
- Root positional prompt is a simple pre-fill, not a full command dispatch.
- Startup resume reuses the existing session drawer infrastructure.

## Likely Change Areas

- `ClawdNet.Terminal/Tui/TuiHost.cs` — new slash commands, startup resume, root prompt pre-fill
- `ClawdNet.App/Program.cs` — root positional prompt handling
- `ClawdNet.App/AppHost.cs` — prompt pre-fill parameter
- `ClawdNet.Terminal/Models/TuiOverlayKind.cs` — potentially new overlay type
- `ClawdNet.Terminal/Rendering/ConsoleTuiRenderer.cs` — render new overlays
- `ClawdNet.Tests/TuiHostTests.cs` — new slash command tests

## Implementation Plan

1. **Add `/status` command**: Show session state as a `Session` overlay
2. **Add `/rename <name>` command**: Update session title in conversation store
3. **Add `/context` command**: Show session context summary (message count, provider, model, permission mode)
4. **Root positional prompt**: Accept `clawdnet "prompt"` and pre-fill TUI composer
5. **Startup session resume**: When launching TUI with no `--session` and existing sessions are present, open session drawer automatically
6. **Add tests** for new slash commands
7. Run sequential validation and smoke tests.
8. Update `docs/PARITY.md`, `docs/PLAN.md`, and `docs/PLAN-04.md`.

## Implementation Results

- Added `/status` slash command showing session state (provider, model, permission mode, message count, task counts)
- Added `/rename <name>` slash command updating session title in conversation store
- Added `/context` slash command showing session context summary (message breakdown by role)
- All three commands use the existing `Session` overlay kind for consistent display
- Added tests for all three new commands

## Validation Results

Completed sequentially:

1. `dotnet build ./ClawdNet.slnx`
   - passed
2. `dotnet test ./ClawdNet.slnx`
   - passed
   - `120` tests passing (117 existing + 3 new)
   - new tests: `Tui_rename_slash_command_updates_session_title`, `Tui_status_slash_command_shows_session_info`, `Tui_context_slash_command_shows_context_info`
3. Smoke checks
   - `dotnet run --project ./ClawdNet.App --`
     - TUI launches successfully

## Remaining Follow-Ups For This Milestone

- root positional prompt shorthand: `clawdnet "hello"` should pre-fill TUI composer (requires Program.cs/AppHost.cs changes)
- startup session resume picker: show session drawer on launch when prior sessions exist (requires AppHost changes)
- continue with remaining TUI parity gaps: `/model`, `/effort`, `/permissions`, `/config`, `/diff`, `/copy`, theme/color, diagnostics commands
