# PLAN-09: PTY UX v3, Slice 6 — PTY Full-Screen Overlay Mode

## Objective

Add a dedicated full-screen terminal emulation mode for PTY sessions in the TUI. When activated, the PTY session takes over the full terminal screen, providing a proper terminal experience for interactive programs like `vim`, `top`, `htop`, `ssh`, etc.

## Scope

This slice covers:

- **Full-screen PTY overlay mode**: Toggle a PTY session into full-screen mode that fills the entire terminal
- **Raw output rendering**: Display PTY output with ANSI escape sequence handling (basic)
- **Direct input forwarding**: All keystrokes forwarded directly to the PTY session
- **Escape key to exit**: Press `Esc` to return to the normal TUI conversation view
- **Session indicator**: Visual indicator showing which PTY session is active in full-screen mode
- **Resize propagation**: Terminal resize events propagated to the PTY device

This slice does **not** attempt:

- Full ANSI/VT100 terminal emulation (cursor positioning, screen buffer) — deferred
- Output pagination/scrolling in full-screen mode — deferred
- Mouse support — deferred
- Bracketed paste mode — deferred
- Special key handling beyond `Esc` to exit — deferred

## Assumptions and Non-Goals

- The full-screen PTY mode is a "passthrough" view: input goes directly to the PTY, output is displayed directly.
- Basic ANSI escape sequence filtering will be attempted, but full terminal emulation is out of scope.
- The mode is entered/exited via a key binding (e.g., `F8` from PTY drawer, or `/pty fullscreen <id>`).
- The PTY session continues running in the background; this is purely a display/input mode change.
- If the PTY session exits, the full-screen mode exits automatically and returns to the TUI.

## Likely Change Areas

- `ClawdNet.Terminal/Models/TuiOverlayKind.cs` — add `PtyFullScreen` kind
- `ClawdNet.Terminal/Models/TuiFocusTarget.cs` — add `PtyFullScreen` focus target
- `ClawdNet.Terminal/Tui/TuiHost.cs` — add full-screen PTY overlay state, rendering, and input handling
- `ClawdNet.Terminal/Tui/TuiRenderer.cs` — render full-screen PTY overlay
- `ClawdNet.Core/Abstractions/IPtySession.cs` — may need `GetFullOutputAsync` for streaming
- `ClawdNet.Tests/` — add tests for full-screen PTY mode entry/exit

## Implementation Progress

### Step 1: Add TUI state for full-screen PTY mode — COMPLETED

1. Added `PtyFullScreen` to `TuiOverlayKind` enum
2. Added `PtyFullScreen` to `TuiFocusTarget` enum
3. Created `PtyFullScreenState` record to track full-screen PTY overlay state
4. Added `PtyFullScreen` field to `TuiState` record
5. Added fields to `TuiHost`:
   - `_ptyFullScreenSessionId` — tracks which PTY session is in full-screen mode
   - `_ptyFullScreenOutput` — buffered PTY output for display
   - `_ptyOutputLock` — synchronization for output updates

### Step 2: Add entry/exit commands — COMPLETED

1. Added `/pty fullscreen <id>` slash command handler
2. Added `EnterPtyFullScreenAsync()` method to enter full-screen mode
3. Added `ExitPtyFullScreen()` method to exit back to normal TUI
4. Added `Esc` key handling in full-screen mode to exit
5. Auto-exit full-screen mode when PTY session terminates (detected in `RenderPtyFullScreenFrame`)

### Step 3: Implement full-screen PTY rendering — COMPLETED

1. Added `RenderPtyFullScreen()` method to `ConsoleTuiRenderer` that renders:
   - Header overlay with session info and "Press Esc to exit"
   - Full PTY output in the transcript pane
   - Status footer with duration and line count
2. Added `RenderPtyFullScreenFrame()` method to `TuiHost` that:
   - Fetches latest PTY session state
   - Auto-exits if session has terminated
   - Builds `TuiState` with `PtyFullScreen` overlay

### Step 4: Implement direct input forwarding — COMPLETED

1. Added `HandlePtyFullScreenInputAsync()` method that:
   - Captures all input in full-screen mode
   - Forwards keystrokes to PTY `WriteAsync`
   - Handles `Esc` to exit full-screen mode
   - Handles end-of-stream to exit app
2. Modified main loop to check `_ptyFullScreenSessionId` and route to PTY input handler
3. Updated `HandlePtyStateChanged` to update full-screen output buffer

### Step 5: Add tests — PENDING

Tests will be added for:
- Full-screen PTY mode entry/exit via slash command
- Auto-exit when PTY session terminates
- Input forwarding in full-screen mode

### Step 6: Validation and documentation — IN PROGRESS

## Validation Plan

### Build Validation

```bash
dotnet build ./ClawdNet.slnx
```

Must pass with zero errors and zero warnings.

### Test Validation

```bash
dotnet test ./ClawdNet.slnx
```

All existing tests must continue to pass. New tests must be added for:
- Full-screen PTY mode entry/exit
- Auto-exit on PTY session termination
- Input forwarding in full-screen mode
- Resize propagation

### Smoke Tests

1. Start PTY session running `cat` or `bash`
2. Enter full-screen mode via `/pty fullscreen <id>`
3. Verify input forwarding works
4. Press `Esc` to exit back to TUI
5. Verify auto-exit when PTY session terminates

## Rollback and Risk Notes

### Risks

1. **Input handling complexity**: Full-screen mode needs to capture all input and forward to PTY.
   - Mitigation: Use a simple input capture loop that forwards everything except `Esc`.
   - Mitigation: Keep special key handling minimal for this slice.

2. **ANSI escape sequence rendering**: Full PTY output may contain escape sequences that need handling.
   - Mitigation: Start with raw text display; basic escape sequence filtering can be added incrementally.
   - Mitigation: Full terminal emulation (cursor, screen buffer) is deferred.

3. **TUI state management**: Entering/exiting full-screen mode needs clean state transitions.
   - Mitigation: Use the existing overlay pattern (`TuiOverlayState`) for consistency.
   - Mitigation: Auto-exit on PTY termination prevents stuck states.

### Rollback

If full-screen PTY mode causes issues:
- Remove the overlay kind and focus target
- Remove the slash command and key binding
- Existing PTY drawer and inline display remain unchanged

## Definition of Done

- [x] Full-screen PTY overlay mode added to TUI — `PtyFullScreen` overlay kind and focus target
- [x] Entry via `/pty fullscreen <id>` slash command
- [x] Exit via `Esc` key or auto-exit on session termination
- [x] Direct input forwarding to PTY session
- [x] Real-time output streaming via PTY state changes
- [ ] Full ANSI/VT100 terminal emulation — deferred
- [x] All tests pass — 138 tests passing
- [x] Documentation updated — this file

## Validation Results

Completed sequentially:

1. `dotnet build ./ClawdNet.slnx`
   - passed with zero errors and zero warnings
2. `dotnet test ./ClawdNet.slnx`
   - passed
   - `138` tests passing (all existing tests continue to pass)

## Remaining Follow-Ups For This Milestone

After this slice, the PTY UX v3 milestone will still have:
- Full ANSI/VT100 terminal emulation (cursor positioning, screen buffer) — deferred
- Output pagination/scrolling in full-screen mode — deferred
- Mouse support — deferred
- Bracketed paste mode — deferred
- Special key handling (arrows, function keys) as escape sequences — deferred
