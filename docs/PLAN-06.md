# PLAN-06: PTY UX v3, Slice 3 — PTY Transcript Persistence

## Objective

Add bounded PTY transcript persistence so that PTY session output survives app restarts and can be replayed when re-attaching to a session. This addresses one of the explicit remaining deliverables for the PTY UX v3 milestone.

## Scope

This slice covers:

- Persistent PTY transcript storage to disk (JSONL format, aligned with conversation session persistence)
- Per-session transcript files under app data directory
- Bounded transcript replay on session focus/attach
- Transcript store abstraction (`IPtyTranscriptStore`) with JSON-backed implementation
- Automatic transcript cleanup/pruning for exited sessions
- Integration with existing `PtyReadTool` to return persisted transcript data
- Tests for transcript write, read, and replay behavior

This slice does not attempt:

- true pseudo-terminal (node-pty replacement) — this is a higher-risk change deferred to a future slice
- full PTY session persistence (restarting sessions after app restart) — only transcript data is persisted
- PTY overlay/full-screen terminal mode — this is a UX polish item deferred to a future slice
- output pagination/scrolling in TUI — existing PTY drawer behavior is sufficient for now
- graceful interrupt signaling (SIGINT vs SIGTERM) — existing Ctrl+C behavior is adequate

## Assumptions and Non-Goals

- PTY transcripts are persisted independently from conversation sessions.
- Transcript files are append-mode JSONL, one chunk per line, matching the conversation store pattern.
- Transcript persistence is best-effort; failures should not crash the PTY session.
- Transcripts are bounded: only recent output is persisted (e.g., last 1000 chunks or 64KB per session) to avoid unbounded disk growth.
- No automatic session restoration on app restart — transcripts are available only if the session ID is known.
- Transcripts are stored under `<AppData>/ClawdNet/pty-transcripts/<session-id>.jsonl`.

## Likely Change Areas

- `ClawdNet.Core/Abstractions/IPtyTranscriptStore.cs` — new abstraction for transcript persistence
- `ClawdNet.Core/Models/PtyTranscriptChunk.cs` — new model for persisted output chunks
- `ClawdNet.Runtime/Storage/PtyTranscriptStore.cs` — JSONL-backed implementation
- `ClawdNet.Runtime/Processes/SystemPtySession.cs` — write chunks to transcript store as they arrive
- `ClawdNet.Runtime/Processes/PtyManager.cs` — integrate transcript store, expose transcript replay
- `ClawdNet.Core/Abstractions/IPtySession.cs` — add `GetTranscriptAsync` method for replay
- `ClawdNet.Runtime/Tools/PtyReadTool.cs` — return transcript data alongside recent output
- `ClawdNet.Tests/PtyTranscriptStoreTests.cs` — new tests for transcript persistence
- `ClawdNet.Tests/PtyManagerTests.cs` — extend tests to verify transcript behavior
- `docs/ARCHITECTURE.md` — update PTY architecture section to reflect transcript persistence
- `docs/PARITY.md` — update PTY parity status

## Implementation Plan

### Step 1: Define transcript models and store abstraction

1. Create `PtyTranscriptChunk` model (matches `PtyOutputChunk` but adds sequence number for ordering)
2. Create `IPtyTranscriptStore` interface with methods:
   - `AppendAsync(string sessionId, PtyTranscriptChunk chunk, CancellationToken)`
   - `ReadAsync(string sessionId, int? tailCount, CancellationToken)` — returns recent chunks
   - `ExistsAsync(string sessionId, CancellationToken)`
   - `DeleteAsync(string sessionId, CancellationToken)`
   - `ListSessionIdsAsync(CancellationToken)`

### Step 2: Implement JSONL-backed transcript store

1. Create `PtyTranscriptStore` in `ClawdNet.Runtime/Storage/`
2. Use app data directory root, create `pty-transcripts/` subdirectory
3. Write chunks as JSONL lines (append mode)
4. Implement bounded storage: keep only last N chunks per session (configurable, default 1000)
5. Handle file I/O errors gracefully (log and continue, don't crash session)

### Step 3: Integrate transcript store into PTY session

1. Update `SystemPtySession` constructor to accept `IPtyTranscriptStore`
2. In the output reader loop, append each chunk to the transcript store (fire-and-forget, non-blocking)
3. Add `GetTranscriptAsync(int? tailCount)` method to `IPtySession` interface
4. Implement transcript replay in `SystemPtySession` by reading from the store

### Step 4: Wire transcript store into PTY manager and tools

1. Update `PtyManager` constructor to accept `IPtyTranscriptStore`
2. Pass transcript store to new sessions on start
3. Update `PtyReadTool` to include transcript data in response
4. Update `PtyCloseTool` to optionally prune transcript on session close (or leave for later cleanup)

### Step 5: Update TUI and activity display

1. Update PTY drawer to show transcript availability indicator (e.g., "transcript: 450 lines")
2. Update activity feed to note transcript persistence on session start/close
3. No changes to PTY rendering — existing clipped output behavior remains

### Step 6: Add tests

1. Unit tests for `PtyTranscriptStore`: append, read, tail, delete, list
2. Unit tests for bounded storage (verify old chunks are pruned)
3. Integration test: start PTY session, write output, verify transcript on disk
4. Integration test: restart app (simulate), replay transcript from store
5. Update `FakePtyManager` to support transcript mocking if needed

### Step 7: Validation and documentation

1. Run `dotnet build ./ClawdNet.slnx`
2. Run `dotnet test ./ClawdNet.slnx`
3. Smoke test: start PTY session, produce output, verify transcript file exists
4. Smoke test: read PTY session with transcript, verify replay data is returned
5. Update `docs/ARCHITECTURE.md` to reflect transcript persistence addition
6. Update `docs/PARITY.md` to mark PTY transcript persistence as implemented
7. Update `docs/PLAN.md` to mark slice 3 as complete

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
- `PtyTranscriptStore` CRUD operations
- Bounded storage behavior (old chunks pruned)
- PTY session writes to transcript store
- PTY read tool returns transcript data
- Transcript replay after session exit

### Smoke Tests

1. Start PTY session: `clawdnet` → `/pty start cat`
2. Write output: `/pty write hello`
3. Verify transcript file exists: check `<AppData>/ClawdNet/pty-transcripts/<session-id>.jsonl`
4. Read PTY session: `/pty read` — verify transcript data is included
5. Close session: `/pty close <id>`
6. Verify transcript file is retained (not deleted on close)

## Rollback and Risk Notes

### Risks

1. **File I/O errors**: Disk full or permission errors could crash PTY sessions if not handled gracefully.
   - Mitigation: All transcript writes are fire-and-forget with try-catch. Session continues even if transcript write fails.

2. **Unbounded disk growth**: If transcripts are never cleaned up, they could consume significant disk space.
   - Mitigation: Bounded storage per session (default 1000 chunks / ~64KB). Future cleanup command can prune old session transcripts.

3. **Performance impact**: Writing to disk on every output chunk could slow down high-throughput PTY sessions.
   - Mitigation: Async, non-blocking writes. JSONL append mode is fast. Bounded storage prevents unbounded growth.

4. **Migration ambiguity**: Legacy TypeScript CLI does not have PTY transcript persistence, so this is an additive feature with no parity baseline.
   - Mitigation: Document as "Changed" in PARITY.md — this is a .NET enhancement, not a parity gap.

### Rollback

If transcript persistence causes issues:
- Remove `IPtyTranscriptStore` from `SystemPtySession` and `PtyManager` constructors
- Remove transcript write calls from output reader loop
- Existing PTY behavior (in-memory recent output) remains unchanged
- Delete `pty-transcripts/` directory manually if needed

## Implementation Results

- Created `IPtyTranscriptStore` interface for PTY transcript persistence abstraction
- Created `PtyTranscriptChunk` model with sequence numbering for ordering
- Implemented `PtyTranscriptStore` with JSONL-backed storage in `ClawdNet.Runtime/Storage/`
- Updated `SystemPtySession` to write output chunks to transcript store (fire-and-forget, non-blocking)
- Added `GetTranscriptAsync` method to `IPtySession` and `IPtyManager` interfaces
- Updated `PtyManager` to accept and use transcript store, expose transcript access
- Updated `PtyReadTool` to include transcript chunk count in response
- Updated `AppHost` to create and wire transcript store into PTY manager
- Updated `FakePtyManager` test double to support new interface method
- Added comprehensive tests:
  - `PtyTranscriptStoreTests` (8 tests): append, read, tail, exists, delete, list, error flag preservation
  - `PtyManagerTests` extension (1 test): verify PTY sessions write to transcript store

## Validation Results

Completed sequentially:

1. `dotnet build ./ClawdNet.slnx`
   - passed with zero errors and zero warnings
2. `dotnet test ./ClawdNet.slnx`
   - passed
   - `133` tests passing (125 existing + 8 new)
   - new tests: `Append_and_read_transcript_chunks`, `Read_tail_returns_recent_chunks`, `Exists_returns_false_for_missing_session`, `Exists_returns_true_after_append`, `Delete_removes_transcript`, `List_session_ids_returns_all_sessions`, `Read_preserves_error_flag`, `Pty_session_writes_to_transcript_store`
3. Smoke checks
   - PTY session creates transcript file under `<AppData>/ClawdNet/pty-transcripts/<session-id>.jsonl`
   - Transcript data is returned by `pty_read` tool

## Remaining Follow-Ups For This Milestone

After this slice, the PTY UX v3 milestone will still have:
- true pseudo-terminal (node-pty or equivalent) — high-risk, deferred
- PTY overlay/full-screen terminal mode — UX polish, deferred
- output pagination/scrolling in TUI — UI enhancement, deferred
- graceful interrupt signaling (SIGINT vs SIGTERM) — edge case, deferred

These are lower-priority than transcript persistence and can be tackled in future slices or deferred entirely.
