# PLAN-22: Session Branching and Resume Parity v2

## Objective

Address remaining P0 session gaps for session branching and resume parity.

**Scope:**
1. Add `--fork-session` flag support - when resuming, create a NEW session ID with copied history instead of reusing the existing one
2. Add `--name` / `-n` root flag for setting session name at launch
3. Add `session rename <id> <new-name>` CLI subcommand
4. Add `/rename` slash command to REPL (already exists in TUI)
5. Add `Tags` field to `ConversationSession` model and basic tag storage/retrieval
6. Add `/tag <tag-name>` slash command to TUI and REPL

**Non-goals (deferred to later milestones):**
- `--from-pr` - fork/resume from PR context (complex, requires external integration)
- `--resume-session-at <message-id>` - rewind to specific message (requires message selector UI)
- `--rewind-files` - hidden legacy flag
- `--no-session-persistence` - disable session persistence
- Message selector UI for rewind
- Session fork provenance tracking (parent/child relationships)

## Assumptions

1. `JsonSessionStore` is the authoritative session persistence layer
2. `ConversationSession` model can be extended with new fields
3. Sessions are serialized to `sessions.json`
4. `TuiHost.cs` and `ReplHost.cs` both have session loading logic that needs updating
5. `AppHost.cs` handles root flag parsing via `TryParseReplLaunchOptions`
6. Tag functionality in legacy was ant-only (`USER_TYPE === 'ant'`), but we'll implement it generally
7. The `--fork-session` behavior: copy message history to a new session ID, leaving original intact

## Files and Subsystems Likely to Change

| File | Change Type | Reason |
|------|------------|--------|
| `ClawdNet.Core/Models/ConversationSession.cs` | Modify | Add `Tags` property |
| `ClawdNet.Core/Commands/SessionCommandHandler.cs` | Modify | Add `rename` and `tag` subcommands |
| `ClawdNet.Runtime/Sessions/JsonSessionStore.cs` | Modify | Add `ForkAsync`, `RenameAsync`, tag management methods |
| `ClawdNet.Runtime/Sessions/IConversationStore.cs` | Modify | Add interface methods for fork/rename/tag |
| `ClawdNet.App/AppHost.cs` | Modify | Add `--fork-session` and `--name` root flags |
| `ClawdNet.Terminal/Tui/TuiHost.cs` | Modify | Handle fork-session logic, pass name to session creation |
| `ClawdNet.Terminal/Repl/ReplHost.cs` | Modify | Add `/rename` slash command, handle fork-session logic |
| `ClawdNet.Core/Models/ReplLaunchOptions.cs` | Modify | Add `ForkSession` and `Name` properties |
| `ClawdNet.Tests/` | Add/Modify | Tests for new session commands |

## Step-by-Step Implementation Plan

### Step 1: Extend ConversationSession Model

**Goal:** Add `Tags` property to support session tagging.

1. Add `Tags` property (List<string> or string[]) to `ConversationSession.cs`
2. Ensure JSON serialization handles the new field gracefully
3. Add backward-compat note: existing sessions without tags should load with empty list

### Step 2: Extend IConversationStore Interface

**Goal:** Add fork, rename, and tag methods to the store interface.

1. Add `ForkAsync(string sessionId, string? newTitle = null)` - creates new session with copied history
2. Add `RenameAsync(string sessionId, string newTitle)` - updates session title
3. Add `AddTagAsync(string sessionId, string tag)` / `RemoveTagAsync(string sessionId, string tag)` / `GetTagsAsync(string sessionId)`
4. Or simpler: `UpdateTagsAsync(string sessionId, IReadOnlyList<string> tags)` - set entire tag list

### Step 3: Implement Store Methods in JsonSessionStore

**Goal:** Implement the new store methods.

1. `ForkAsync`: Load source session, create new session with new ID, copy messages/provider/model, set title (override or copy), save
2. `RenameAsync`: Load session, update title, save
3. Tag methods: Load session, update tags array, save

### Step 4: Add Root Flags to AppHost.cs

**Goal:** Parse `--fork-session` and `--name` at root level.

1. Add `ForkSession` bool and `Name` string to `ReplLaunchOptions`
2. Parse `--fork-session` and `--name`/`-n` in `TryParseReplLaunchOptions`
3. Pass these options through to TUI/REPL host initialization

### Step 5: Wire Fork-Session Logic into TUI/REPL Hosts

**Goal:** When `--fork-session` is set with `--continue` or `--resume`, fork instead of reusing.

1. In `TuiHost.LoadOrCreateSessionAsync`, check `ForkSession` flag
2. If fork + resume/continue: call `ForkAsync` instead of direct session load
3. Same for `ReplHost.LoadOrCreateSessionAsync`

### Step 6: Wire --name Flag into Session Creation/Loading

**Goal:** When `--name` is provided, use it as the session title.

1. In session resolution flow, if `Name` is provided:
   - For new sessions: use as title
   - For resumed sessions: rename the session
   - For forked sessions: use as title (override copy)

### Step 7: Add `session rename` CLI Subcommand

**Goal:** Add rename as a first-class session CLI command.

1. In `SessionCommandHandler.cs`, add `rename <id> <new-name>` subcommand
2. Call `_conversationStore.RenameAsync(id, newName)`
3. Output success/error message

### Step 8: Add `session tag` CLI Subcommands

**Goal:** Add tag management as session CLI commands.

1. In `SessionCommandHandler.cs`, add:
   - `tag <id> <tag-name>` - add a tag
   - `untag <id> <tag-name>` - remove a tag  
   - Or: `tag <id> [--add|--remove] <tag-name>`
2. Simplest: just `tag <id> <tag-name>` toggles the tag (like legacy)

### Step 9: Add `/rename` to REPL

**Goal:** REPL parity with TUI for rename.

1. In `ReplHost.cs`, add `/rename <new-name>` slash command
2. Update `_currentSession.Title` and save
3. Show confirmation in activity area

### Step 10: Add `/tag` to TUI and REPL

**Goal:** Add tag toggle command to both interactive surfaces.

1. In `TuiHost.cs`, add `/tag <tag-name>` slash command
2. In `ReplHost.cs`, add `/tag <tag-name>` slash command
3. Toggle behavior: if tag exists, remove it; otherwise add it
4. Show confirmation in activity/overlay

### Step 11: Update Help Text

**Goal:** Document new commands.

1. Update `session --help` to include rename and tag
2. Update TUI `/help` to include `/tag`
3. Update REPL help text to include `/rename` and `/tag`

### Step 12: Tests

**Goal:** Verify new session commands.

1. Unit tests for `ForkAsync`, `RenameAsync`, tag operations
2. Unit tests for `session rename` and `session tag` command handlers
3. Manual smoke tests for interactive flows

## Validation Plan

### Automated
```bash
dotnet build ./ClawdNet.slnx
dotnet test ./ClawdNet.slnx
```

### Manual smoke tests
```bash
# Test --fork-session
dotnet run --project ClawdNet.App -- --continue --fork-session
# Should create new session with copied history

# Test --name
dotnet run --project ClawdNet.App -- --name "My Custom Session"
# Should create session with specified name

# Test session rename
dotnet run --project ClawdNet.App -- session list
dotnet run --project ClawdNet.App -- session rename <id> "New Name"
dotnet run --project ClawdNet.App -- session show <id>

# Test session tag
dotnet run --project ClawdNet.App -- session tag <id> "work"
dotnet run --project ClawdNet.App -- session show <id>
# Tag should appear

# Test /rename in REPL
dotnet run --project ClawdNet.App -- --feature legacy-repl
# Type: /rename "New Session Name"

# Test /tag in TUI
dotnet run --project ClawdNet.App --
# Type: /tag work
```

## Rollback / Risk Notes

- **Risk:** Breaking existing sessions.json format. **Mitigation:** Add Tags as optional field with default empty list; test with existing sessions.json
- **Risk:** Fork session message history copy could be large. **Mitigation:** Deep copy messages carefully; bounded by existing session size limits
- **Risk:** Tag toggle logic edge cases. **Mitigation:** Clear test coverage for add/remove/toggle scenarios
- **Rollback:** All changes are additive; no existing behavior is modified. Safe to revert if issues arise.

## Exit Criteria

- [ ] `--fork-session` flag works with `--continue` and `--resume`
- [ ] `--name` / `-n` flag works at root level
- [ ] `session rename <id> <new-name>` works
- [ ] `/rename` works in REPL (already works in TUI)
- [ ] Session model has `Tags` field
- [ ] `session tag <id> <tag>` works (toggle behavior)
- [ ] `/tag` works in TUI and REPL
- [ ] `dotnet build` passes
- [ ] `dotnet test` passes
- [ ] `PARITY.md` updated for session rows
- [ ] `PLAN.md` milestone status updated
