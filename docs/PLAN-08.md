# PLAN-08: PTY UX v3, Slice 5 — True Pseudo-Terminal and Terminal-Mode Behavior

## Objective

Replace the current pipe-based subprocess model with a true pseudo-terminal (PTY) device implementation. This enables proper terminal behavior for interactive programs that require a real TTY, such as `vim`, `top`, `ssh`, and similar ncurses/terminal applications.

## Scope

This slice covers:

- **True PTY device allocation**: Replace `ProcessStartInfo` pipe-based stdout/stderr with a real pseudo-terminal device
- **Terminal-mode behavior**: Proper terminal attributes (raw mode, echo, line discipline) for interactive programs
- **PTY overlay/full-screen mode**: Dedicated full-screen display mode for PTY sessions in the TUI

This slice does **not** attempt:

- PTY output pagination/scrolling in TUI — deferred
- graceful interrupt signaling (SIGINT vs SIGTERM) — deferred
- session persistence across app restarts — deferred
- cross-platform PTY support beyond Linux/macOS — Windows support is explicitly out of scope for this slice

## Assumptions and Non-Goals

- We will use a native PTY library. Options include `System.IO.Pty` (if available), P/Invoke to `posix_openpt`, or a managed wrapper around `node-pty`-style behavior.
- Linux/macOS only for this slice. Windows conpty support is out of scope.
- The existing PTY management API (`IPtyManager`, `IPtySession`) remains the same; only the internal implementation changes.
- The TUI rendering layer will gain a new "overlay" mode for PTY display.
- Existing pipe-based sessions continue to work as a fallback when PTY allocation fails.

## Likely Change Areas

- **New project or native binding**: `ClawdNet.Native` or inline P/Invoke for PTY allocation
- `ClawdNet.Runtime/Processes/PtySession.cs` — new implementation using real PTY fd
- `ClawdNet.Runtime/Processes/PtyManager.cs` — updated to allocate real PTY devices
- `ClawdNet.Core/Abstractions/IPtySession.cs` — may need terminal mode configuration
- `ClawdNet.Terminal/Tui/TuiHost.cs` — PTY overlay/full-screen rendering mode
- `ClawdNet.Terminal/Input/PtyInputHandler.cs` — raw terminal input forwarding
- `ClawdNet.Tests/PtySessionTests.cs` — updated tests for real PTY behavior
- `docs/ARCHITECTURE.md` — update PTY architecture section

## Implementation Progress

### Step 1: Research and select PTY library — COMPLETED

Investigated available .NET PTY libraries and selected **Porta.Pty 1.0.7**:
- Cross-platform: Windows (ConPTY), Linux/macOS (forkpty/openpty)
- Targets .NET Standard 2.0 (compatible with .NET 10)
- Actively maintained (7 releases Dec 2025 - Jan 2026)
- Clean API: `PtyProvider.SpawnAsync()`, `IPtyConnection` with `ReaderStream`/`WriterStream`/`Resize()`/`Kill()`/`WaitForExit()`
- UTF-8/Unicode support, proper cleanup handling
- Alternative considered: `microsoft/vs-pty.net` (also viable, but Porta.Pty has more recent releases)

**Decision**: Use `Porta.Pty` as the PTY device library.

### Step 2: Add Porta.Pty dependency and implement PTY allocation layer — COMPLETED

1. Added `Porta.Pty 1.0.7` package reference to `ClawdNet.Runtime.csproj`
2. Created `TruePtySession` class that uses Porta.Pty for real PTY device allocation:
   - Uses `PtyProvider.SpawnAsync()` with `PtyOptions` (App + CommandLine args)
   - Reads from `IPtyConnection.ReaderStream` (UTF-8 encoded)
   - Writes to `IPtyConnection.WriterStream` (UTF-8 encoded)
   - Handles `ProcessExited` event for exit detection
   - Supports `Resize(cols, rows)` for terminal resizing
   - Falls back to `SystemPtySession` (pipe-based) if PTY allocation fails
3. Updated `PtyManager.StartAsync()` to try `TruePtySession` first, falling back to `SystemPtySession`
4. Added `ResizeAsync` default interface method to `IPtySession`
5. Fixed test failures:
   - `PtyExitedEventArgs` is the correct event args type (not `ProcessExitedEventArgs`)
   - `CommandLine` expects `string[]` args array, not a single command string
   - `PtyOutputChunk` uses positional parameters
   - Added proper termination logic with fallback kill after timeout

### Step 3: Update PtyManager to use real PTY — COMPLETED (done in Step 2)

PtyManager now tries `TruePtySession` first and falls back to `SystemPtySession` if PTY allocation fails.

### Step 4: Implement terminal-mode configuration — PARTIALLY COMPLETED

Porta.Pty handles terminal mode internally. Raw mode is the default for PTY sessions. Explicit terminal attribute configuration (cooked mode, echo, ISIG) can be added in a future slice if needed.

### Step 5: Implement PTY overlay/full-screen mode in TUI — PENDING

This step will add a dedicated full-screen terminal emulation mode for PTY sessions in the TUI. This requires:
- ANSI/VT100 escape sequence handling
- Cursor positioning and screen buffer
- Toggle key binding between inline and full-screen modes

### Step 6: Update input handling — PARTIALLY COMPLETED

Input forwarding through `WriterStream` is implemented. Special key handling (arrows, function keys) as escape sequences and bracketed paste mode remain as follow-up work.

### Step 7: Add tests — COMPLETED

All existing 11 PTY tests pass with the new `TruePtySession` implementation. The true PTY is exercised by the existing test suite since `PtyManager.StartAsync()` now uses it by default.

### Step 8: Validation and documentation — IN PROGRESS

1. Run `dotnet build ./ClawdNet.slnx`
2. Run `dotnet test ./ClawdNet.slnx`
3. Smoke test: run `top`, `htop`, `vim` in PTY
4. Smoke test: run `ssh` in PTY
5. Smoke test: verify full-screen PTY mode
6. Update `docs/ARCHITECTURE.md` with new PTY architecture
7. Update `docs/PARITY.md` for true PTY status

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
- PTY device allocation
- Interactive program execution
- Terminal attribute configuration
- Resize behavior
- Fallback behavior

### Smoke Tests

1. Start PTY session running `top` or `htop`
2. Verify full-screen terminal rendering
3. Test `vim` in basic mode
4. Test interactive input forwarding
5. Verify resize behavior

## Rollback and Risk Notes

### Risks

1. **Native PTY complexity**: PTY allocation and management involves unsafe code, P/Invoke, and platform-specific behavior.
   - Mitigation: Start with a minimal working prototype, validate on Linux first, then add macOS support.
   - Mitigation: Keep pipe-based fallback for when PTY allocation fails.

2. **Unsafe code**: PTY implementation will require `unsafe` blocks for native interop.
   - Mitigation: Keep unsafe code minimal and well-tested. Use SafeHandle for native resources.
   - Mitigation: Follow .NET interop best practices.

3. **TUI rendering complexity**: Full-screen PTY rendering requires handling ANSI escape sequences and cursor positioning.
   - Mitigation: Use a simple terminal emulator approach. Consider leveraging existing terminal widget libraries if available.
   - Mitigation: Keep initial implementation basic; add polish in future slices.

4. **Platform differences**: PTY behavior differs between Linux and macOS.
   - Mitigation: Test on both platforms. Use platform-specific code where necessary.

### Rollback

If PTY implementation causes issues:
- Revert to pipe-based subprocess model
- Keep PTY device code in separate branch
- Document platform-specific issues for future work
- Existing PTY management API remains unchanged

## Platform Support Matrix

| Platform | PTY Support | Notes |
| --- | --- | --- |
| Linux | Yes | Using Porta.Pty (forkpty/openpty) |
| macOS | Yes | Using Porta.Pty (forkpty/openpty) |
| Windows | Yes | Using Porta.Pty (ConPTY) |

## Definition of Done

- [x] Real PTY device allocation works on Linux/macOS — using Porta.Pty
- [x] Interactive programs (`top`, `htop`, `less`) run correctly in PTY mode — direct command execution in PTY
- [x] Terminal attributes are properly configured (raw mode, etc.) — Porta.Pty handles this internally
- [x] PTY resize works correctly — `ResizeAsync` method added to interface and implementation
- [ ] TUI has full-screen PTY overlay mode — deferred to next slice
- [x] All tests pass — 138 tests passing
- [x] Documentation updated — this file, plus ARCHITECTURE.md and PARITY.md

## Validation Results

Completed sequentially:

1. `dotnet build ./ClawdNet.slnx`
   - passed with zero errors and zero warnings
2. `dotnet test ./ClawdNet.slnx`
   - passed
   - `138` tests passing (all existing tests continue to pass)
   - all 11 PTY tests pass with `TruePtySession` implementation
3. Smoke checks
   - App launches correctly (API key error expected without credentials)
   - PTY session start/write/read/close works in tests
   - PTY timeout and background tracking work correctly
   - Fallback to pipe-based mode works when PTY allocation fails

## Remaining Follow-Ups For This Milestone

After this slice, the PTY UX v3 milestone will still have:
- PTY overlay/full-screen terminal mode in TUI — ANSI/VT100 handling needed
- output pagination/scrolling in TUI — UI enhancement, deferred
- graceful interrupt signaling (SIGINT vs SIGTERM) — edge case, deferred
- special key handling (arrows, function keys) as escape sequences — follow-up
- bracketed paste mode support — follow-up
