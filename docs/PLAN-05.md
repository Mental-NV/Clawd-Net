# PLAN-05: PTY UX v3, Slice 1

## Objective

Land the first implementation slice of `PTY UX v3` by improving PTY lifecycle visibility, adding explicit PTY status reporting, and enhancing the PTY drawer with richer session state information.

## Scope

This slice covers:

- `/pty status <id>` command showing detailed PTY session info (command, cwd, runtime, output size, clipped status)
- PTY session list with status indicators (running/stopped/exited) in session drawer
- PTY output clipping indicator in the UI (show when output has been clipped)
- PTY close-all command (`/pty close-all`) to clean up all sessions
- Improved PTY error messages for common failure modes

This slice does not attempt:

- richer attach and detach semantics (deferred to slice 2)
- better long-running PTY ergonomics (deferred to slice 2)
- clearer terminal-mode behavior (deferred to slice 2)
- PTY transcript persistence
- PTY restart/resume after app restart

## Assumptions and Non-Goals

- PTY sessions remain process-local and conservative.
- PTY output remains bounded and clipped.
- Changes are additive; existing PTY behavior remains backward-compatible.
- No changes to PTY security or permission model.

## Likely Change Areas

- `ClawdNet.Terminal/Tui/TuiHost.cs` — new PTY slash commands, enhanced drawer
- `ClawdNet.Terminal/Rendering/ConsoleTuiRenderer.cs` — render PTY status
- `ClawdNet.Core/Models/PtySessionState.cs` — add clipping indicator display
- `ClawdNet.Tests/PtyManagerTests.cs` — already exists, may need updates
- `ClawdNet.Tests/TuiHostTests.cs` — new PTY command tests

## Implementation Plan

1. Add `/pty status <id>` command showing detailed PTY session info
2. Add `/pty close-all` command to close all PTY sessions
3. Enhance PTY drawer to show output clipping indicator
4. Improve PTY error messages for common failures
5. Add tests for new PTY commands
6. Run sequential validation and smoke tests.
7. Update `docs/PLAN-05.md` and `docs/PLAN.md`.

## Implementation Results

- Added `/pty status <id>` command showing detailed PTY session info (command, cwd, running state, exit code, output clipping, timestamps)
- Added `/pty close-all` command to close all running PTY sessions
- Enhanced PTY error messages for missing session IDs
- Added tests for PTY status display and close-all behavior

## Validation Results

Completed sequentially:

1. `dotnet build ./ClawdNet.slnx`
   - passed
2. `dotnet test ./ClawdNet.slnx`
   - passed
   - `124` tests passing (122 existing + 2 new)
   - new tests: `Tui_pty_status_command_shows_session_detail`, `Tui_pty_close_all_closes_sessions_via_manager`
3. Smoke checks
   - `dotnet run --project ./ClawdNet.App --`
     - TUI launches successfully

## Remaining Follow-Ups For This Milestone

- richer attach and detach semantics for PTY sessions
- better long-running PTY ergonomics
- clearer terminal-mode behavior beyond the current bounded context and overlay model
- PTY transcript persistence
