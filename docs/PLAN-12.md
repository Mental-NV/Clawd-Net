# PLAN-12: Plugin Platform v4, Slice 2 — Additional Hook Kinds and Plugin Health Reporting

## Objective

Expand the plugin hook system with additional hook kinds that cover more lifecycle events, and add basic plugin health/status reporting so users can see plugin state and invocation statistics.

## Scope

This slice covers:

- **Additional hook kinds**:
  - `BeforeToolCall` — fires before a tool is executed (can intercept or modify tool calls)
  - `AfterToolCall` — fires after a tool completes (can observe results)
  - `OnStartup` — fires when the app starts (plugins can initialize)
  - `OnShutdown` — fires when the app is exiting (plugins can clean up)
  - `OnSessionCreated` — fires when a new conversation session is created

- **Plugin health/status reporting**:
  - Track invocation counts and last-invoked timestamps for tools, commands, and hooks
  - Track success/failure counts for plugin subprocess executions
  - Add `plugin status <name>` command to show plugin health metrics
  - Show health metrics in `plugin list` output

This slice does **not** attempt:

- Plugin configuration API (user-editable settings per plugin)
- Plugin dependency resolution
- Plugin signature verification
- Resource limits or sandboxing for plugin subprocesses

## Assumptions and Non-Goals

- Hook invocations are fire-and-forget for non-blocking hooks; blocking hooks can affect query flow.
- Health metrics are in-memory only (not persisted across restarts).
- Invocation tracking adds minimal overhead — only incrementing counters and recording timestamps.
- `plugin status` shows detailed metrics for a single plugin.
- `plugin list` shows a summary health indicator (healthy/degraded/errors).

## Likely Change Areas

- `ClawdNet.Core/Models/PluginHookKind.cs` — add new hook kinds
- `ClawdNet.Runtime/Plugins/PluginCatalog.cs` — parse new hook kinds
- `ClawdNet.Runtime/Plugins/PluginRuntime.cs` — track invocation metrics
- `ClawdNet.Core/Models/PluginDefinition.cs` — add health metrics
- `ClawdNet.Core/Commands/PluginCommandHandler.cs` — add `plugin status` command
- `ClawdNet.Runtime/QueryEngine.cs` or similar — fire new hooks at appropriate lifecycle points
- `ClawdNet.Tests/` — add tests for new hooks and health reporting

## Implementation Plan

### Step 1: Add new hook kinds

1. Add to `PluginHookKind` enum:
   - `BeforeToolCall`
   - `AfterToolCall`
   - `OnStartup`
   - `OnShutdown`
   - `OnSessionCreated`

2. Update `TryParseHookKind` in `PluginCatalog` to recognize new kinds

3. Fire hooks at appropriate lifecycle points:
   - `OnStartup` — when `AppHost` initializes
   - `OnShutdown` — when app is exiting
   - `OnSessionCreated` — when a new session is created
   - `BeforeToolCall` — before tool execution in the query engine
   - `AfterToolCall` — after tool execution completes

### Step 2: Add plugin health tracking

1. Create `PluginHealthMetrics` record:
   - `ToolInvocationCount`, `LastToolInvocationUtc`
   - `CommandInvocationCount`, `LastCommandInvocationUtc`
   - `HookInvocationCount`, `HookSuccessCount`, `HookFailureCount`
   - `LastActivityUtc`

2. Update `PluginRuntime` to record metrics on each invocation

3. Add `Health` property to `PluginDefinition`

### Step 3: Add `plugin status` CLI command

1. Show detailed health metrics for a plugin
2. Show recent hook activity
3. Show invocation counts for tools and commands

### Step 4: Update `plugin list` to show health summary

1. Add health indicator: ✓ healthy, ⚠ degraded, ✗ errors
2. Show total invocation count

### Step 5: Add tests

1. New hook kind parsing
2. Health metrics tracking
3. `plugin status` command output

### Step 6: Validation and documentation

1. Run `dotnet build ./ClawdNet.slnx`
2. Run `dotnet test ./ClawdNet.slnx`
3. Smoke test: create plugin with new hook kinds, verify they fire
4. Smoke test: `plugin status` shows metrics
5. Update docs

## Definition of Done

- [x] New hook kinds implemented and fired at appropriate lifecycle points
- [x] Plugin health metrics tracked in-memory
- [x] `plugin status` command shows detailed metrics
- [x] `plugin list` shows health summary
- [x] All tests pass
- [x] Documentation updated
