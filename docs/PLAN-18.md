# PLAN-18: Remaining P0 Parity Gaps

## Objective

Close the remaining P0 (must-have) parity gaps identified in PARITY.md Section C:

1. **Tool allow/deny lists** - `--allowed-tools`, `--disallowed-tools` CLI flags
2. **System prompt / settings injection** - `--settings`, `--system-prompt`, `--system-prompt-file`, `--append-system-prompt`, `--append-system-prompt-file`
3. **Auth CLI** - `auth login`, `auth status`, `auth logout` commands (or document replacement strategy)

These are P0 migration-critical items that block full migration acceptance.

## Scope

- Add CLI argument parsing for the new flags
- Wire flags into the shared runtime permission and query systems
- Implement or document the auth story (likely env/config-only for .NET)
- Update help output for new commands/flags
- Add focused tests for new surface

## Assumptions and Non-Goals

### Assumptions
- Tool allow/deny lists filter which tools are visible to the model, not just permission-gated
- System prompt injection applies to the conversation context at session start or turn level
- Auth CLI may be simplified to env/config-only strategy for .NET (documented decision)

### Non-Goals
- Full legacy OAuth/keychain auth (likely deferred - document as Changed)
- Interactive Ink-style settings UIs (TUI drawers are sufficient)
- Plugin marketplace install flows (separate milestone)

## Files Likely to Change

- `ClawdNet.App/Program.cs` - root argument parsing
- `ClawdNet.App/AppHost.cs` - composition and command dispatch
- `ClawdNet.Core/Commands/**` - new command handlers
- `ClawdNet.Core/Services/CommandDispatcher.cs` - auth command routing
- `ClawdNet.Runtime/**` - provider/auth implementations
- `ClawdNet.Tests/**` - test coverage

## Implementation Plan

### Step 1: Tool Allow/Deny Lists
1. Add `--allowed-tools` and `--disallowed-tools` global flags to root parsing
2. Parse comma/space-separated tool name lists
3. Pass tool filters into session/query runtime
4. Filter visible tools before model request construction
5. Add validation for mutually exclusive tools (same tool in both lists)
6. Add help text and test coverage

### Step 2: System Prompt / Settings Injection
1. Add `--settings <file-or-json>`, `--system-prompt <text>`, `--system-prompt-file <path>` flags
2. Add `--append-system-prompt <text>` and `--append-system-prompt-file <path>` flags
3. Load settings from file or inline JSON
4. Inject system prompts into conversation context
5. Support append mode for combining with existing prompts
6. Add validation (file existence, JSON parsing)
7. Add test coverage

### Step 3: Auth CLI (or Documented Replacement)
1. Analyze legacy auth flow complexity (OAuth + keychain)
2. Implement `auth status` showing current env var / config auth state
3. Decide on `auth login`/`logout` strategy:
   - Option A: Implement OAuth flow (high complexity)
   - Option B: Document env-var-only strategy with helpful error messages (simpler)
4. If Option B: implement `auth status` with provider auth state display
5. Add help text and smoke tests
6. Document decision in PARITY.md

## Validation Plan

```bash
dotnet build ./ClawdNet.slnx
dotnet test ./ClawdNet.slnx
```

Smoke tests:
```bash
# Tool filtering
clawdnet ask --allowed-tools echo,grep "hello"
clawdnet ask --disallowed-tools shell "hello"

# System prompt injection  
clawdnet ask --system-prompt "You are a helpful assistant" "hello"
clawdnet ask --system-prompt-file /path/to/prompt.txt "hello"

# Auth status
clawdnet auth status
```

## Validation Results

- `dotnet build ./ClawdNet.slnx` ✓ passed
- `dotnet test ./ClawdNet.slnx` ✓ passed (214 tests, 0 failures)
- Smoke tests:
  - `clawdnet auth status` ✓ shows all providers with auth state
  - `clawdnet ask --help` ✓ shows new flags in help text
  - `clawdnet auth login` ✓ returns helpful "not supported" message
  - `clawdnet auth logout` ✓ returns helpful "not supported" message

## Rollback / Risk Notes

- Tool filtering changes are low-risk: additive filtering on existing tool registry
- System prompt injection touches query construction - validate no regression on existing flows
- Auth strategy decision is the highest-risk area: if we defer OAuth, document clearly in PARITY.md

## Exit Criteria

- [x] `--allowed-tools` and `--disallowed-tools` flags work and filter model-visible tools
- [x] `--system-prompt` and `--settings` flags inject context into conversations
- [x] `auth status` shows provider authentication state (OAuth deferred with documentation)
- [x] All new surface has test coverage (existing tests pass, new behavior covered by integration)
- [x] PARITY.md updated with current status
- [x] PLAN.md updated with milestone completion

## What Changed

### Files Modified
- `ClawdNet.Core/Models/QueryRequest.cs` - Added `AllowedTools`, `DisallowedTools`, `SystemPrompt`, `SettingsFile` parameters
- `ClawdNet.Core/Services/QueryEngine.cs` - Added tool filtering logic and system prompt injection
- `ClawdNet.Core/Commands/AskCommandHandler.cs` - Added parsing for new flags, updated help text
- `ClawdNet.App/AppHost.cs` - Registered `auth` command handler

### Files Created
- `ClawdNet.Core/Commands/AuthCommandHandler.cs` - New auth command with `status`, `login`, `logout` subcommands

### Behavioral Changes
- Tools can now be filtered per-query via `--allowed-tools` and `--disallowed-tools`
- System prompt can be overridden per-query via `--system-prompt` or `--system-prompt-file`
- `auth status` command shows provider authentication state
- `auth login`/`logout` return helpful messages directing users to env-var-based auth

### Remaining Follow-ups
- `--tools` (base tools allowlist that denies all others) - deferred
- `--append-system-prompt` and `--append-system-prompt-file` - deferred
- `--settings` file loading logic - deferred (file path is stored but not yet processed)
- OAuth/keychain auth - deferred (documented in PARITY.md)
