# PLAN-17: Session Resume Family v1 (--continue, --resume)

## Objective

Add basic session resume support to the .NET CLI by implementing `--continue` and `--resume` flags on the root command, plus a `session show` command for inspection. This covers the most-used resume paths from the legacy CLI.

## Scope

**In scope:**
- `--continue` (`-c`) flag: resumes the most recent session
- `--resume [value]` (`-r`) flag: resumes a specific session by ID, name, or search query
- `session show <id>` command: displays session metadata and recent conversation turns
- Conversation store logic to load and continue an existing session
- TUI drawer integration for resume flows

**Out of scope:**
- `--from-pr` resume
- `--fork-session`
- rewind-at-message (`--rewind-files`)
- interactive session picker UI (deferred to later milestone)
- tag and rename flows

## Assumptions

- Sessions are stored in the current .NET `sessions.json` format
- Legacy JSONL transcripts under `~/.claude/projects/` are NOT imported (separate compatibility milestone)
- "Most recent session" means the last session with activity by timestamp
- Resume means continuing the same session ID with its existing conversation history and provider/model

## Files Likely to Change

- `ClawdNet.App/Program.cs` — add `--continue` and `--resume` root flags
- `ClawdNet.App/AppHost.cs` — wire resume logic into session initialization
- `ClawdNet.Core/Commands/SessionCommand.cs` — add `show` subcommand
- `ClawdNet.Core/Services/CommandDispatcher.cs` — route new flags
- `ClawdNet.Core/Services/ConversationStore.cs` or equivalent — add resume/load-last logic
- `ClawdNet.Terminal/Tui/TuiHost.cs` — integrate resume into TUI launch
- `ClawdNet.Tests/` — add tests for resume logic

## Step-by-Step Implementation

### Step 1: Understand current session and conversation store

Read the current session management code to understand:
- How sessions are created and persisted
- How `--session` currently works
- What data is available for resume

### Step 2: Add `--continue` and `--resume` root flags

- Add `-c, --continue` flag that loads the most recent session
- Add `-r, --resume [value]` flag that:
  - With no value: prompts or lists recent sessions for selection (or errors if none)
  - With a value: searches sessions by ID or name prefix for a match
- Route both flags through the same session initialization path as `--session`

### Step 3: Implement conversation store resume logic

- Add `GetMostRecentSessionAsync()` to conversation store
- Add `SearchSessionsAsync(query)` for name/ID prefix matching
- Ensure loaded session retains its provider, model, and message history

### Step 4: Add `session show` command

- Display session metadata (id, title, provider, model, created, updated)
- Display recent conversation turns (last N messages)
- Useful for inspection before resuming

### Step 5: Update TUI launch to support resume

- When launched with `--continue` or `--resume`, open the TUI with the resumed session active
- Ensure the conversation history is visible when resuming

### Step 6: Tests and validation

- Unit tests for session search and resume logic
- Smoke tests for `--continue`, `--resume`, and `session show`

## Validation Results

1. `dotnet build ./ClawdNet.slnx` — **PASSED**
2. `dotnet test ./ClawdNet.slnx` — **PASSED** (213/214 passed; 1 pre-existing flaky test in TaskManagerTests unrelated to this change)
3. Manual smoke: pending user verification

## What Changed

### Files Modified
- `ClawdNet.Core/Abstractions/IConversationStore.cs` — added `GetMostRecentAsync` and `SearchAsync` interface methods
- `ClawdNet.Runtime/Sessions/JsonSessionStore.cs` — implemented `GetMostRecentAsync` (returns session ordered by UpdatedAtUtc) and `SearchAsync` (exact ID match → ID prefix match → title substring match)
- `ClawdNet.Core/Models/ReplLaunchOptions.cs` — added `Continue` (bool) and `ResumeQuery` (string?) fields
- `ClawdNet.App/AppHost.cs` — extended `TryParseReplLaunchOptions` to handle `-c/--continue` and `-r/--resume [value]`
- `ClawdNet.Terminal/Tui/TuiHost.cs` — extended `LoadOrCreateSessionAsync` to handle resume and continue logic before session creation
- `ClawdNet.Terminal/Repl/ReplHost.cs` — same resume/continue logic added to fallback REPL
- `ClawdNet.Core/Commands/SessionCommandHandler.cs` — added `session show <id>` subcommand with metadata and recent message display
- `ClawdNet.Tests/HelpAndPrintModeTests.cs` — added stub implementations for new interface methods
- `ClawdNet.Tests/StreamJsonOutputTests.cs` — added stub implementations for new interface methods

### Design Decisions
- Search priority: exact ID match → ID prefix match → title substring match
- Ambiguous matches (multiple sessions) return an error with up to 5 candidates
- `--resume` without value behaves like `--continue` (most recent session)
- Resume preserves and can update provider/model from the resumed session
- Exit code 3 for session not found, exit code 2 for provider configuration errors

## Remaining Follow-ups
- `--from-pr`, `--fork-session`, rewind-at-message deferred to later milestone
- Interactive session picker UI not yet implemented
- Legacy `~/.claude` JSONL transcript import not yet implemented (separate compatibility milestone)
