# PLAN-10: PTY UX v3, Slice 7 — Special Key Handling and Output Pagination

## Objective

Improve the full-screen PTY mode by adding proper special key handling (arrows, function keys as escape sequences) and output pagination/scrolling for navigating large amounts of PTY output.

## Scope

This slice covers:

- **Special key handling**: Arrow keys, function keys, and other special keys forwarded as proper ANSI escape sequences to the PTY
- **Output pagination/scrolling**: Scroll through PTY output history using PageUp/PageDown in full-screen mode
- **Scroll indicator**: Visual indicator showing scroll position in PTY output

This slice does **not** attempt:

- Full ANSI/VT100 terminal emulation (cursor positioning, screen buffer) — deferred
- Mouse support — deferred
- Bracketed paste mode — deferred

## Assumptions and Non-Goals

- Special keys (arrows, Home, End, PgUp, PgDn, etc.) are forwarded as standard ANSI escape sequences
- PageUp/PageDown in full-screen mode scroll the PTY output view instead of exiting
- Esc still exits full-screen mode
- The output buffer stores more than the 4096 chars currently visible to enable scrolling

## Likely Change Areas

- `ClawdNet.Terminal/Tui/TuiHost.cs` — special key handling, scroll state
- `ClawdNet.Terminal/Models/PtyFullScreenState.cs` — add scroll offset field
- `ClawdNet.Terminal/Rendering/ConsoleTuiRenderer.cs` — render scroll indicator

## Implementation Progress

### Step 1: Add scroll state to PtyFullScreenState — COMPLETED

1. Added `ScrollOffset` and `TotalOutputLength` fields to `PtyFullScreenState`
2. Added `_ptyFullScreenScrollOffset` and `_ptyFullScreenOutputHistory` fields to `TuiHost`

### Step 2: Implement special key handling — COMPLETED

1. Arrow keys forwarded as ANSI escape sequences (`\x1b[A/B/C/D`)
2. Home/End forwarded as `\x1b[H` / `\x1b[F`
3. Function keys F1-F5 forwarded as `\x1b[11~` - `\x1b[15~`
4. Tab and Backspace handled correctly

### Step 3: Implement output pagination/scrolling — COMPLETED

1. PageUp/PageDown scroll the output buffer by 10 lines
2. ScrollBottom (End key) returns to live output
3. Scroll position shown in status line (e.g., "Scroll: 20 lines up" or "Scroll: live")

### Step 4: Update rendering — COMPLETED

1. Footer shows scroll position indicator

### Step 5: Validation and documentation — COMPLETED

## Validation Results

1. `dotnet build ./ClawdNet.slnx` — passed
2. `dotnet test ./ClawdNet.slnx` — 138 tests passing

## Remaining Follow-Ups

- Full ANSI/VT100 terminal emulation — deferred
- Mouse support — deferred
- Bracketed paste mode — deferred
