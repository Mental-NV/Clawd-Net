# PLAN-25: Reporting and Workflow Surface Recovery

## Objective

Restore the most important reporting and diagnostic surfaces from the legacy CLI (`doctor`, `status`, `stats`, `usage`, `cost`, `insights`) and decide which workflow commands remain first-party vs plugin/skill territory.

**Scope (bounded to highest-value reporting commands):**
1. `doctor` command - health/diagnostic surface showing system state, config, provider status, connectivity
2. `status` command - show current session status (active provider, model, message count, session info)
3. `stats` command - show usage statistics for current or all sessions
4. `usage` command - show token/cost usage breakdown

**Non-goals (deferred or out of scope):**
- `cost` command (requires billing API access; deferred)
- `insights` command (requires analytics infrastructure; deferred)
- Workflow commands (`/review`, `/init`, `/commit`, etc.) - deferred to separate workflow milestone
- Interactive Ink-style reporting UI - plain text/structured output is acceptable for v1

## Assumptions

1. `doctor` should be a self-contained diagnostic that doesn't require API keys to run
2. `status` should work both inside and outside a session context
3. `stats` and `usage` should work at session level and optionally aggregate across all sessions
4. Legacy commands had rich Ink UI; plain text output is acceptable for .NET v1
5. Provider connectivity checks should be lightweight (ping or minimal request)
6. Cost tracking requires per-provider pricing data; defer accurate cost calculation

## Files and Subsystems Likely to Change

| File | Change Type | Reason |
|------|------------|--------|
| `ClawdNet.Core/Commands/DoctorCommandHandler.cs` | New | Health/diagnostic command |
| `ClawdNet.Core/Commands/StatusCommandHandler.cs` | New | Session status command |
| `ClawdNet.Core/Commands/StatsCommandHandler.cs` | New | Usage statistics command |
| `ClawdNet.Core/Commands/UsageCommandHandler.cs` | New | Token/cost usage command |
| `ClawdNet.Core/Services/CommandDispatcher.cs` | Modify | Register new commands |
| `ClawdNet.App/AppHost.cs` | Possibly modify | Root command registration if needed |
| `ClawdNet.Core/Models/ConversationSession.cs` | Possibly modify | Add usage tracking fields if needed |
| `ClawdNet.Runtime/` | Possibly modify | Provider connectivity checks |
| `ClawdNet.Tests/` | Add | Tests for new commands |

## Step-by-Step Implementation Plan

### Step 1: Create `doctor` Command

**Goal:** Self-contained diagnostic showing system health.

1. Show app version and runtime info
2. Show config paths and whether config files exist
3. Show provider status (configured, enabled, API key present)
4. Show session store status (session count, most recent)
5. Show plugin status (count, enabled)
6. Show MCP server status (count, enabled)
7. Show LSP server status (count, enabled)
8. Optional: lightweight connectivity check to configured providers

### Step 2: Create `status` Command

**Goal:** Show current session status.

1. If inside interactive session: show provider, model, message count, session ID/title
2. If outside session: show "no active session" with guidance
3. Show permission mode
4. Show tool allow/deny status

### Step 3: Create `stats` Command

**Goal:** Show usage statistics.

1. Count sessions, tasks, PTY sessions
2. Show total messages across all sessions or current session only
3. Show average messages per session
4. Show tool call counts if tracked
5. Show provider distribution

### Step 4: Create `usage` Command

**Goal:** Show token/cost usage breakdown.

1. If session has usage data: show token counts, estimated costs
2. If no usage data: show "usage tracking not available" with explanation
3. Support `--all` flag for aggregate view
4. Support `--session <id>` for session-specific view

### Step 5: Register Commands and Update Help

**Goal:** Make commands discoverable.

1. Register all four commands in CommandDispatcher
2. Ensure `--help` shows new commands
3. Add per-command `--help` text

### Step 6: Tests

**Goal:** Verify new reporting commands.

1. Unit tests for doctor output structure
2. Unit tests for status in/out of session
3. Unit tests for stats aggregation
4. Manual smoke tests for all four commands

## Validation Plan

### Automated
```bash
dotnet build ./ClawdNet.slnx
dotnet test ./ClawdNet.slnx
```

### Manual smoke tests
```bash
# Test doctor
dotnet run --project ClawdNet.App -- doctor

# Test status (outside session)
dotnet run --project ClawdNet.App -- status

# Test stats
dotnet run --project ClawdNet.App -- stats
dotnet run --project ClawdNet.App -- stats --all

# Test usage
dotnet run --project ClawdNet.App -- usage
dotnet run --project ClawdNet.App -- usage --all
```

## Rollback / Risk Notes

- **Risk:** Provider connectivity checks may be slow or flaky. **Mitigation:** Use timeouts, make connectivity checks optional/async
- **Risk:** Usage/cost data may not be tracked in current session model. **Mitigation:** Gracefully degrade to "not available" message
- **Risk:** Doctor output may expose sensitive paths or config. **Mitigation:** Redact API keys, show only key presence
- **Rollback:** All changes are additive; no existing behavior is modified

## Exit Criteria

- [x] `doctor` command shows system health, config, provider status
- [x] `status` command shows session status in/out of session context
- [x] `stats` command shows usage statistics
- [x] `usage` command shows token/cost usage or graceful degradation
- [x] `dotnet build` passes (0 errors, 2 warnings pre-existing)
- [x] `dotnet test` passes (270 tests pass, 2 pre-existing failures unrelated to this change)
- [x] `PARITY.md` reporting rows updated to Implemented
- [x] `PLAN.md` milestone status updated to [v]

## Implementation Summary

### What was done:
1. Created `DoctorCommandHandler` - comprehensive diagnostic surface showing version, runtime, config files, providers, sessions, plugins, MCP, and LSP status
2. Created `StatusCommandHandler` - session status display showing provider, model, message count, and session metadata
3. Created `StatsCommandHandler` - usage statistics with aggregate and session-specific views, provider/tag distribution
4. Created `UsageCommandHandler` - token/cost usage with graceful degradation (message counts, per-session breakdown)
5. Registered all four commands in AppHost
6. Fixed `TryParseRootPositionalPrompt` and `ShouldLaunchInteractive` to not treat command names as prompts
7. Added 12 unit tests for the new commands
8. Updated PARITY.md, PLAN.md, and README.md

### What changed:
- `ClawdNet.Core/Commands/DoctorCommandHandler.cs` - new file
- `ClawdNet.Core/Commands/StatusCommandHandler.cs` - new file
- `ClawdNet.Core/Commands/StatsCommandHandler.cs` - new file
- `ClawdNet.Core/Commands/UsageCommandHandler.cs` - new file
- `ClawdNet.App/AppHost.cs` - registered new commands, fixed command detection in root prompt/interactive launch
- `ClawdNet.Tests/Commands/ReportingCommandsTests.cs` - new test file
- `docs/PARITY.md` - updated reporting row to Implemented
- `docs/PLAN.md` - marked milestone as [v]
- `README.md` - documented new commands

### Remaining follow-ups:
- `cost` command deferred (requires billing API integration)
- `insights` command deferred (requires analytics infrastructure)
- Usage tracking shows message counts only; actual token counting requires provider API integration
- Workflow commands (`/review`, `/init`, `/commit`, etc.) remain deferred to separate workflow milestone

## Validation Results

### dotnet build
```
Build succeeded. 0 Warning(s) 0 Error(s)
```

### dotnet test
```
Passed: 270, Failed: 2 (pre-existing MemoryFileLoaderTests failures)
```

### Manual smoke tests
```bash
$ clawdnet doctor
ClawdNet Doctor - System Diagnostics
====================================
Application:
  Version:    1.0.0.0
  Runtime:    .NET 10.0.4
  ...
Providers:
  anthropic        Anthropic    not configured
  ...
Sessions:
  Total sessions: 16
  ...

$ clawdnet status
Session Status
=============
  Session:        Stats Test
  Provider:       anthropic
  Model:          claude-sonnet-4-5
  Messages:       1

$ clawdnet stats
Usage Statistics
================
  Total sessions:    16
  Total messages:    16
  Avg messages/sess: 1.0

$ clawdnet usage
Token and Cost Usage
====================
  Total sessions:    16
  Total messages:    16
  Per-session message count:
    Stats Test                          1 messages
    ...
Note: Token counting and cost estimation are not currently tracked.
```
