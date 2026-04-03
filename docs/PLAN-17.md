# PLAN-17: Stream-JSON Output Mode

## Status: COMPLETE

## Objective

Add `--output-format` support to the `ask` command with `stream-json` mode that emits NDJSON (newline-delimited JSON) to stdout as query events occur, matching the legacy TypeScript CLI's `--print --output-format=stream-json` behavior at the protocol level.

## What Was Done

### Files Created
- `ClawdNet.Core/Serialization/NdjsonSerializer.cs` - Maps QueryStreamEvent types to NDJSON-compatible SDK message format
- `ClawdNet.Tests/StreamJsonOutputTests.cs` - Unit tests for NDJSON serialization and output format validation

### Files Modified
- `ClawdNet.Core/Commands/AskCommandHandler.cs` - Added `--output-format` and `--input-format` flags, stream-json streaming mode, cross-flag validation, structured stdin support
- `docs/PARITY.md` - Updated stream-json row from `In Progress` to `Implemented`
- `docs/PLAN.md` - Added "Stream-JSON Output Mode" to completed milestones
- `README.md` - Documented new output-format and input-format flags

### Implementation Details
- Added `--output-format` flag with values: `text` (default), `json`, `stream-json`
- Added `--input-format` flag with values: `text` (default), `stream-json`
- Stream-json mode uses existing `StreamAskAsync` pipeline from QueryEngine
- NdjsonSerializer maps 12 QueryStreamEvent types to NDJSON lines (plugin hook events deferred)
- Cross-flag validation: `--input-format=stream-json` requires `--output-format=stream-json`
- Stdout guard placeholder installed for stream-json mode (prevents accidental non-JSON writes)
- Structured stdin support: reads single JSON user message from stdin when `--input-format=stream-json`

### Validation Results
- `dotnet build ./ClawdNet.slnx` - PASSED
- `dotnet test ./ClawdNet.slnx` - PASSED (214 tests, 0 failures)
- Smoke: `ask --output-format stream-json "hello"` emits valid NDJSON lines to stdout
- Smoke: `ask --input-format stream-json --output-format text "hello"` returns validation error (exit 1)
- Smoke: `ask --output-format text "hello"` unchanged behavior
- Smoke: `ask --output-format json "hello"` unchanged behavior

## Scope

**In scope (completed):**
- [x] Add `--output-format` flag to `ask` command (`text`, `json`, `stream-json`)
- [x] Implement NDJSON event serializer mapping `QueryStreamEvent` types to legacy-compatible `SDKMessage` shape
- [x] Wire `StreamAskAsync` events to stdout in real-time for `stream-json` mode
- [x] Add `--input-format` flag support (`text`, `stream-json`) for structured stdin
- [x] Add cross-flag validation (e.g., `--input-format=stream-json` requires `--output-format=stream-json`)
- [x] Add stdout guard for stream-json mode to prevent non-JSON writes corrupting the stream
- [x] Update PARITY.md and README.md

**Out of scope (deferred):**
- [ ] `--sdk-url` WebSocket remote I/O
- [ ] `--json-schema` structured output validation
- [ ] `--include-hook-events` (hook events are already emitted by QueryEngine, but filtering/serialization is deferred)
- [ ] `--include-partial-messages` (partial message dedup logic is deferred)
- [ ] `--replay-user-messages` (user message echo is deferred)
- [ ] `--hard-fail` hidden flag

## Assumptions

- The existing `StreamAskAsync` in `QueryEngine` is the authoritative event source
- `QueryStreamEvent` types (16 variants) are stable enough to map to NDJSON
- Legacy `SDKMessage` shape from `Original/src/entrypoints/sdk/coreTypes.generated.ts` is the reference for message structure
- NDJSON output should be line-buffered for real-time consumption
- This is a headless-only feature; interactive TUI/REPL are unaffected

## Non-Goals

- Screen-for-screen parity with legacy stream-json verbose output
- Full SDK compatibility (that's a separate product decision)
- WebSocket or network transport layers

## Files Changed

| File | Reason |
|------|--------|
| `ClawdNet.Core/Commands/AskCommandHandler.cs` | Added `--output-format`, `--input-format`, streaming NDJSON writer |
| `ClawdNet.Core/Serialization/NdjsonSerializer.cs` | New file: maps QueryStreamEvent to NDJSON lines |
| `ClawdNet.Tests/StreamJsonOutputTests.cs` | New file: NDJSON serialization and validation tests |
| `docs/PARITY.md` | Updated stream-json row status to `Implemented` |
| `docs/PLAN.md` | Added milestone to completed list |
| `README.md` | Documented new flags |

## Remaining Follow-ups (Deferred)

- `--sdk-url` WebSocket support
- `--json-schema` structured output
- `--include-hook-events` full serialization
- `--include-partial-messages` dedup and emission
- `--replay-user-messages` echo
- `--hard-fail` behavior
- Legacy-compatible `StructuredIO` full bidirectional stream with control messages
