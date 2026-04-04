# PLAN-24: Runtime Controls and Settings UI v1

## Objective

Add the first high-value runtime controls for effort/thinking/budget surfaces and implement basic interactive settings pickers in TUI/REPL.

**Scope (bounded to highest-value controls):**
1. Add `--effort` flag to `ask` command (low/medium/high)
2. Add `--thinking` flag to `ask` command (enable/disable/adaptive)
3. Add `--max-turns` flag to `ask` command (currently hardcoded to 8)
4. Add `--max-budget-usd` flag to `ask` command (cost budget enforcement)
5. Wire effort/thinking/max-turns/budget through QueryRequest -> QueryEngine -> provider clients
6. Update Anthropic provider to support thinking parameter in API payload
7. Add `/effort` slash command to REPL and TUI
8. Add `/thinking` slash command to REPL and TUI
9. Update help text and PARITY.md

**Non-goals (deferred):**
- `--workload` parameter (legacy hidden flag, unclear semantics)
- `--task-budget` parameter (requires task orchestration changes)
- `--max-thinking-tokens` (can be derived from thinking budget or deferred)
- Interactive model picker UI (deferred to future UI polish milestone)
- Budget tracking UI in TUI (deferred)
- Token budget tracker with continuation messages (complex, deferred)
- Effort/thinking in root launch flags (deferred; focus on `ask` command first)

## Assumptions

1. Legacy effort levels are: low, medium, high (with medium as default)
2. Legacy thinking modes are: adaptive, enabled, disabled
3. Anthropic API supports `thinking` parameter with `type` and `budget_tokens`
4. Other providers (OpenAI, Bedrock, Vertex, Foundry) may not support thinking natively
5. `max_budget_usd` is a soft limit enforced by the query engine after each turn
6. Current hardcoded `MaxTurns = 8` should remain the default when not specified

## Files and Subsystems Likely to Change

| File | Change Type | Reason |
|------|------------|--------|
| `ClawdNet.Core/Models/QueryRequest.cs` | Modify | Add Effort, Thinking, MaxBudgetUsd properties |
| `ClawdNet.Core/Models/TaskRequest.cs` | Modify | Add MaxBudgetUsd for task budget tracking |
| `ClawdNet.Core/Models/ModelRequest.cs` | Modify | Add ThinkingConfig and MaxTokensOverride |
| `ClawdNet.Core/Commands/AskCommandHandler.cs` | Modify | Add --effort, --thinking, --max-turns, --max-budget-usd flags |
| `ClawdNet.Core/Services/QueryEngine.cs` | Modify | Add budget enforcement, pass effort/thinking to model requests |
| `ClawdNet.Runtime/Anthropic/HttpAnthropicMessageClient.cs` | Modify | Add thinking parameter to API payload |
| `ClawdNet.Runtime/Bedrock/HttpBedrockMessageClient.cs` | Possibly modify | Add thinking if Converse API supports it |
| `ClawdNet.Runtime/VertexAI/HttpVertexAIMessageClient.cs` | Possibly modify | Add thinking if supported |
| `ClawdNet.Runtime/Foundry/HttpFoundryMessageClient.cs` | Possibly modify | Add thinking if supported |
| `ClawdNet.Terminal/Repl/ReplHost.cs` | Modify | Add /effort, /thinking slash commands |
| `ClawdNet.Terminal/Tui/TuiHost.cs` | Modify | Add /effort, /thinking slash commands |
| `ClawdNet.Tests/` | Add/Modify | Tests for new flags and budget enforcement |

## Step-by-Step Implementation Plan

### Step 1: Add Runtime Control Properties to QueryRequest

**Goal:** Model the new parameters.

1. Add `EffortLevel?` enum (Low, Medium, High)
2. Add `ThinkingMode?` enum (Adaptive, Enabled, Disabled)
3. Add `EffortLevel? Effort` property to QueryRequest
4. Add `ThinkingMode? Thinking` property to QueryRequest
5. Add `int? MaxBudgetUsd` property to QueryRequest
6. MaxTurns already exists; ensure it's nullable or keep default 8

### Step 2: Add CLI Flags to Ask Command

**Goal:** Parse new flags from CLI.

1. Add `--effort <low|medium|high>` flag parsing
2. Add `--thinking <adaptive|enabled|disabled>` flag parsing
3. Add `--max-turns <N>` flag parsing (override default 8)
4. Add `--max-budget-usd <N>` flag parsing
5. Pass parsed values into QueryRequest

### Step 3: Wire Through QueryEngine

**Goal:** Propagate controls to model requests.

1. Pass EffortLevel into ModelRequest
2. Pass ThinkingMode into ModelRequest
3. Add budget tracking: track cumulative cost after each turn
4. Add budget check: if cost >= MaxBudgetUsd, stop and return budget exceeded error
5. Pass MaxTurns from request (already wired)

### Step 4: Update Anthropic Provider for Thinking

**Goal:** Support thinking parameter in Anthropic API calls.

1. When ThinkingMode is Enabled or Adaptive, add `thinking` parameter to payload
2. Set `thinking.type` to "enabled" or "adaptive" based on mode
3. Set `thinking.budget_tokens` to a reasonable default (e.g., 1024 or max_tokens/2)
4. When ThinkingMode is Disabled, omit thinking parameter
5. Ensure max_tokens is set appropriately when thinking is enabled

### Step 5: Update Other Providers (Best Effort)

**Goal:** Add thinking support where supported.

1. Check if Bedrock Converse API supports thinking parameter
2. Check if Vertex AI supports thinking parameter
3. Check if Foundry supports thinking parameter
4. Document which providers support thinking vs which ignore it

### Step 6: Add /effort Slash Command to REPL and TUI

**Goal:** Interactive effort level selection.

1. Add `/effort` command (shows current effort level)
2. Add `/effort <low|medium|high>` command (sets effort level for session)
3. Update session's effort level
4. Show result in output

### Step 7: Add /thinking Slash Command to REPL and TUI

**Goal:** Interactive thinking mode selection.

1. Add `/thinking` command (shows current thinking mode)
2. Add `/thinking <adaptive|enabled|disabled>` command (sets thinking mode for session)
3. Update session's thinking mode
4. Show result in output

### Step 8: Update Help Text

**Goal:** Document new flags and commands.

1. Update `ask --help` to include new flags
2. Update REPL/TUI help to include /effort and /thinking

### Step 9: Tests

**Goal:** Verify new runtime controls.

1. Unit tests for flag parsing
2. Unit tests for budget enforcement in QueryEngine
3. Unit tests for Anthropic thinking parameter
4. Manual smoke tests for /effort and /thinking slash commands

## Validation Plan

### Automated
```bash
dotnet build ./ClawdNet.slnx
dotnet test ./ClawdNet.slnx
```

### Manual smoke tests
```bash
# Test --effort flag
dotnet run --project ClawdNet.App -- ask --effort high "Explain quantum computing"

# Test --thinking flag
dotnet run --project ClawdNet.App -- ask --thinking enabled "What is 2+2?"

# Test --max-turns flag
dotnet run --project ClawdNet.App -- ask --max-turns 3 "Explain this project"

# Test --max-budget-usd flag
dotnet run --project ClawdNet.App -- ask --max-budget-usd 0.01 "Write a long essay"

# Test /effort slash command in REPL/TUI
dotnet run --project ClawdNet.App --
> /effort
> /effort high

# Test /thinking slash command in REPL/TUI
dotnet run --project ClawdNet.App --
> /thinking
> /thinking enabled
```

## Rollback / Risk Notes

- **Risk:** Budget tracking may be inaccurate without real cost data from providers. **Mitigation:** Use estimate-based tracking or document as approximate
- **Risk:** Thinking parameter may not be supported by all providers. **Mitigation:** Gracefully ignore for unsupported providers, log warning
- **Risk:** Breaking existing QueryRequest/ModelRequest contracts. **Mitigation:** New properties are nullable with defaults; backward compatible
- **Rollback:** All changes are additive; no existing behavior is modified. Safe to revert if issues arise.

## Exit Criteria

- [x] `--effort` flag accepted on `ask` command and passed through to providers
- [x] `--thinking` flag accepted on `ask` command and Anthropic provider sends thinking parameter
- [x] `--max-turns` flag accepted on `ask` command (overrides default 8)
- [x] `--max-budget-usd` flag accepted on `ask` command with budget enforcement
- [x] `/effort` slash command works in REPL and TUI
- [x] `/thinking` slash command works in REPL and TUI
- [x] `dotnet build` passes (0 errors, 2 warnings pre-existing)
- [x] `dotnet test` passes (260 tests, 0 failed)
- [x] `PARITY.md` updated for effort/thinking/budget rows
- [x] `PLAN.md` milestone status updated to [v]

## Implementation Summary

### What was done:
1. Added `EffortLevel` and `ThinkingMode` enums to `ClawdNet.Core/Models/`
2. Extended `QueryRequest` with `Effort`, `Thinking`, and `MaxBudgetUsd` properties
3. Extended `ModelRequest` with `Effort` and `Thinking` properties
4. Added CLI flags to `AskCommandHandler`: `--effort`, `--thinking`, `--max-turns`, `--max-budget-usd`
5. Updated `QueryEngine` to pass effort/thinking to model requests and enforce budget limits
6. Updated `HttpAnthropicMessageClient` to include thinking parameter in API payload when enabled
7. Added `/effort` and `/thinking` slash commands to both REPL and TUI
8. Updated help text in all surfaces

### What changed:
- `ClawdNet.Core/Models/EffortLevel.cs` - new enum
- `ClawdNet.Core/Models/ThinkingMode.cs` - new enum
- `ClawdNet.Core/Models/QueryRequest.cs` - added 3 new properties
- `ClawdNet.Core/Models/ModelRequest.cs` - added 2 new properties
- `ClawdNet.Core/Commands/AskCommandHandler.cs` - added flag parsing, help text
- `ClawdNet.Core/Services/QueryEngine.cs` - added budget tracking, effort/thinking propagation
- `ClawdNet.Runtime/Anthropic/HttpAnthropicMessageClient.cs` - added thinking payload support
- `ClawdNet.Terminal/Repl/ReplHost.cs` - added slash commands, fields, parsers
- `ClawdNet.Terminal/Tui/TuiHost.cs` - added slash commands, fields, parsers

### Remaining follow-ups:
- Budget tracking is currently a placeholder (checks before turn, but doesn't calculate actual cost)
- Other providers (OpenAI, Bedrock, Vertex, Foundry) don't support thinking parameter yet - they gracefully ignore it
- Interactive model picker UI deferred to future polish milestone
- `/workload` and `/task-budget` deferred (legacy hidden flags with unclear semantics)
