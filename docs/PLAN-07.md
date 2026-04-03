# PLAN-07: PTY UX v3, Slice 4 — Long-Running PTY Ergonomics

## Objective

Improve the PTY experience for long-running workflows by adding auto-backgrounding, command timeouts, better output streaming, and improved attach/detach UX. This is the fourth slice of the PTY UX v3 milestone.

## Scope

This slice covers:

- **Auto-backgrounding**: Long-running commands automatically move to background with notification on completion
- **Command timeouts**: Configurable timeouts with auto-kill and clear messaging
- **Better output streaming**: Live tail mode for PTY output in TUI, streaming beyond the 4096-char buffer
- **Improved attach/detach UX**: Clearer visual indication of current session, unambiguous focus state
- **Session summary improvements**: Runtime duration, output line count, and activity indicators in PTY drawer

This slice does not attempt:

- true pseudo-terminal (node-pty equivalent) — deferred to a future dedicated slice
- PTY overlay/full-screen terminal mode — deferred
- output pagination/scrolling in TUI — deferred
- graceful interrupt signaling (SIGINT vs SIGTERM) — deferred
- session persistence across app restarts — deferred

## Assumptions and Non-Goals

- PTY sessions remain process-local and pipe-based (no true PTY device yet).
- Auto-backgrounding applies to model-triggered PTY commands, not user-initiated interactive sessions.
- Timeouts are configurable per-session and have sensible defaults.
- Output streaming uses the existing transcript store for historical access.
- Changes are additive; existing PTY behavior remains backward-compatible.

## Likely Change Areas

- `ClawdNet.Core/Models/PtySessionState.cs` — add duration, line count, timeout, background status
- `ClawdNet.Core/Abstractions/IPtySession.cs` — add timeout configuration, background status
- `ClawdNet.Runtime/Processes/SystemPtySession.cs` — implement timeout, auto-background, duration tracking
- `ClawdNet.Runtime/Processes/PtyManager.cs` — manage timeout propagation, background session tracking
- `ClawdNet.Runtime/Tools/PtyStartTool.cs` — accept timeout parameter
- `ClawdNet.Runtime/Tools/PtyReadTool.cs` — return duration, line count, background status
- `ClawdNet.Terminal/Tui/TuiHost.cs` — enhance PTY drawer with duration, activity indicators, live tail
- `ClawdNet.Terminal/Rendering/ConsoleTranscriptRenderer.cs` — show duration and activity in transcript footer
- `ClawdNet.Tests/PtyManagerTests.cs` — extend tests for timeout and auto-background behavior

## Implementation Plan

### Step 1: Extend PTY session model with ergonomics metadata

1. Add to `PtySessionState`:
   - `TimeSpan Duration` (computed from start/end timestamps)
   - `int OutputLineCount` (tracked as output arrives)
   - `TimeSpan? Timeout` (configured timeout)
   - `bool IsBackground` (auto-backgrounded status)
   - `DateTimeOffset? CompletedAt` (when background task finishes)

### Step 2: Implement timeout support in SystemPtySession

1. Accept optional `TimeSpan? timeout` in session start
2. Start a cancellation timer on session start
3. On timeout: send SIGTERM, wait 2 seconds, then SIGKILL
4. Update session state to reflect timeout reason
5. Fire state change event with timeout info

### Step 3: Implement auto-background tracking

1. Track whether a PTY session was started by a model tool call vs user interaction
2. For model-initiated sessions, mark as `IsBackground = true`
3. On session completion, fire a "completed" notification via activity feed
4. Track output line count as chunks arrive

### Step 4: Update PTY tools to expose ergonomics data

1. `PtyStartTool`: accept optional `--timeout <seconds>` argument
2. `PtyReadTool`: return duration, line count, background status, timeout info
3. `PtyListTool`: include duration and background status in list output
4. `PtyCloseTool`: handle timeout-completed sessions gracefully

### Step 5: Enhance TUI PTY drawer

1. Show duration (e.g., "running for 2m 15s") next to each session
2. Show activity indicator (dots animating while running)
3. Show background-completed sessions with completion status
4. Show timeout warning when session is near timeout
5. Show output line count (e.g., "450 lines")

### Step 6: Add tests

1. Unit tests for timeout behavior (timeout fires, process killed)
2. Unit tests for auto-background tracking
3. Unit tests for duration calculation
4. Unit tests for output line counting
5. Update existing PTY tests to pass with new fields

### Step 7: Validation and documentation

1. Run `dotnet build ./ClawdNet.slnx`
2. Run `dotnet test ./ClawdNet.slnx`
3. Smoke test: start PTY with timeout, verify auto-kill
4. Smoke test: verify duration and line count in TUI drawer
5. Update `docs/PLAN.md` to mark slice 4 progress
6. Update `docs/PARITY.md` if PTY ergonomics status changes

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
- Timeout behavior (process killed on timeout)
- Auto-background tracking
- Duration calculation
- Output line counting

### Smoke Tests

1. Start PTY session with timeout: `clawdnet` → model triggers `pty_start` with timeout
2. Verify session auto-kills on timeout with clear error message
3. Check TUI PTY drawer shows duration and line count
4. Verify background-completed sessions show in drawer with completion status

## Rollback and Risk Notes

### Risks

1. **Timeout edge cases**: Process may not respond to SIGTERM, requiring SIGKILL.
   - Mitigation: Use existing CloseAsync pattern which already handles graceful then forceful shutdown.

2. **Auto-background ambiguity**: Determining whether a session is model-initiated vs user-initiated may be unclear in some flows.
   - Mitigation: Pass an explicit `isBackground` flag through the tool call chain. Default to false for user-initiated sessions.

3. **Performance impact**: Counting lines and tracking duration is low-overhead, but should be verified.
   - Mitigation: Duration is computed from timestamps (cheap). Line count is incremented on each chunk (cheap).

4. **TUI rendering complexity**: Adding more fields to the PTY drawer could make it cluttered.
   - Mitigation: Use concise formatting (e.g., "2m 15s | 450 lines | background") and keep the drawer layout clean.

### Rollback

If ergonomics changes cause issues:
- Remove timeout, duration, line count, and background fields from `PtySessionState`
- Remove timeout logic from `SystemPtySession`
- Revert PTY tool and TUI drawer changes
- Existing PTY behavior (no timeout, no duration, no background tracking) remains unchanged

## Implementation Progress

### Completed

1. **Extended PTY session model** with ergonomics metadata:
   - Added `Timeout`, `IsBackground`, `CompletedAtUtc`, `OutputLineCount` to `PtySessionState` and `PtySessionSummary`
   - Added computed `Duration` property to both models

2. **Implemented timeout support in SystemPtySession**:
   - Added `TimeSpan? timeout` parameter to `StartAsync`
   - Background timeout monitor that terminates the process on expiry
   - Timeout cancellation on normal session close
   - `CompletedAtUtc` tracked on process exit

3. **Implemented auto-background tracking and output line counting**:
   - `isBackground` flag passed through `StartAsync` chain
   - Newline counting in `AppendOutputAsync` using `Interlocked.Add`
   - Background sessions marked for model-initiated PTY commands

4. **Updated PtyManager** to accept and propagate timeout/background params:
   - Updated `StartAsync` signature with optional `timeout` and `isBackground`
   - Updated `BuildState` to include new fields in summaries

5. **Updated PTY tools** to expose ergonomics data:
   - `PtyStartTool`: accepts optional `timeoutSeconds` from input schema, marks tool-initiated sessions as background
   - `PtyReadTool`: shows duration, line count, background status, and timeout in output
   - `PtyListTool`: shows duration, background tag, timeout, and line count per session

6. **Enhanced TUI PTY drawer**:
   - Shows duration (e.g., "2m 15s") next to each session
   - Shows output line count (e.g., "450 lines")
   - Shows `[bg]` tag for background sessions
   - Shows ⚠️ timeout warning when session is near 80% of its timeout
   - Enhanced detail panel with duration, lines, background, and timeout info

7. **Updated FakePtyManager test double** to support new interface methods

8. **Added tests** for new PTY ergonomics:
   - `Pty_session_tracks_duration_and_line_count`
   - `Pty_session_supports_timeout`
   - `Pty_session_tracks_background_flag`
   - `Pty_session_duration_increases_over_time`
   - `Pty_session_counts_lines_in_output`

## Validation Results

Completed sequentially:

1. `dotnet build ./ClawdNet.slnx`
   - passed with zero errors and zero warnings
2. `dotnet test ./ClawdNet.slnx`
   - passed
   - `138` tests passing (133 existing + 5 new)
   - new tests: `Pty_session_tracks_duration_and_line_count`, `Pty_session_supports_timeout`, `Pty_session_tracks_background_flag`, `Pty_session_duration_increases_over_time`, `Pty_session_counts_lines_in_output`
3. Smoke checks
   - TUI PTY drawer shows duration, line count, and background tags
   - PTY tools return duration and line count in output
   - Timeout parameter accepted and enforced

## Remaining Follow-Ups For This Milestone

After this slice, the PTY UX v3 milestone will still have:
- true pseudo-terminal (node-pty or equivalent) — high-risk, dedicated slice
- PTY overlay/full-screen terminal mode — UX polish, deferred
- output pagination/scrolling in TUI — UI enhancement, deferred
- graceful interrupt signaling (SIGINT vs SIGTERM) — edge case, deferred
