# PLAN-01: Advanced Orchestration v3, Slice 1

## Objective

Land the first bounded implementation slice of `Advanced Orchestration v3` by adding explicit parent-child task relationships, delegated subtask creation, and better orchestration visibility across task persistence, tools, CLI, and TUI.

## Scope

This slice covers:

- persisted parent-child task metadata
- bounded delegated child-task spawning
- richer task inspection and task-list visibility for orchestration trees
- TUI and CLI visibility for parent-child relationships

This slice does not attempt:

- a general task graph engine
- arbitrary worker-to-worker recursion
- durable live-task resume after restart
- interactive attach into worker sessions

## Assumptions and Non-Goals

- Active execution remains process-local.
- Parent-child task relationships are a tree, not a general DAG.
- Delegation is bounded conservatively so orchestration depth stays understandable.
- Existing task commands and tools should remain backward-compatible.

## Likely Change Areas

- `ClawdNet.Core/Models/*Task*`
- `ClawdNet.Core/Abstractions/ITaskManager.cs`
- `ClawdNet.Runtime/Tasks/TaskManager.cs`
- `ClawdNet.Runtime/Sessions/JsonTaskStore.cs`
- `ClawdNet.Runtime/Tools/Task*.cs`
- `ClawdNet.Core/Commands/TaskCommandHandler.cs`
- `ClawdNet.Core/Services/QueryEngine.cs`
- `ClawdNet.Terminal/Tui/TuiHost.cs`
- `ClawdNet.Terminal/Rendering/ConsoleTuiRenderer.cs`
- `ClawdNet.Tests/*Task*`
- `ClawdNet.Tests/*Tui*`
- `ClawdNet.Tests/*QueryEngine*`

## Implementation Plan

1. Extend task models with parent-child metadata and bounded orchestration depth.
2. Update task persistence and normalization for the new fields.
3. Teach `TaskManager` to:
   - create child tasks
   - track direct children on parent tasks
   - emit parent-visible progress for delegated work
   - expose richer inspection and list data
4. Extend task tools and CLI output to surface hierarchy data without breaking existing callers.
5. Update TUI task drawer/detail rendering to show parent-child relationships and child progress clearly.
6. Add tests for:
   - child-task creation and linkage
   - bounded delegation rules
   - richer inspection/list payloads
   - TUI task visibility for hierarchy data
7. Run sequential validation and targeted smoke tests.
8. Update `docs/PARITY.md`, `docs/PLAN.md`, and other authoritative docs as needed.

## Validation Plan

Run sequentially:

1. `dotnet build ./ClawdNet.slnx`
2. `dotnet test ./ClawdNet.slnx`
3. Targeted smoke tests:
   - `dotnet run --project ./ClawdNet.App -- task list`
   - `dotnet run --project ./ClawdNet.App -- task show <id>` when a task fixture or generated task is available

Manual checks if needed:

- confirm TUI task drawer shows parent-child relationship detail
- confirm delegated child-task updates do not corrupt parent-session activity

## Risks and Rollback Notes

- The main risk is overreaching into a full task-graph engine. Keep this slice tree-shaped and depth-bounded.
- Another risk is breaking existing task tooling payloads. Add fields conservatively instead of replacing current ones.
- If delegation behavior destabilizes worker execution, keep parent-child metadata and disable worker-spawned child tasks rather than forcing a bigger redesign.

## Implementation Results

- Added persisted parent-child task metadata:
  - `ParentTaskId`
  - `RootTaskId`
  - `Depth`
  - `ChildTaskIds`
- Added bounded delegated child-task spawning:
  - worker sessions can create child tasks
  - delegation is currently bounded to one additional level
- Updated task supervision behavior:
  - parent tasks track direct children
  - parent tasks wait for direct child completion before finalizing
  - parent cancellation cascades to running children
- Extended task visibility:
  - richer task tool payloads
  - richer `task list` and `task show`
  - TUI task drawer/detail now shows hierarchy data

## Validation Results

Completed sequentially:

1. `dotnet build ./ClawdNet.slnx`
   - passed
2. `dotnet test ./ClawdNet.slnx`
   - passed
   - `113` tests passing
3. Smoke checks
   - `dotnet run --project ./ClawdNet.App -- --version`
     - passed
   - `dotnet run --project ./ClawdNet.App -- task list`
     - passed
     - output: `No tasks found.`

## Remaining Follow-Ups For This Milestone

- widen orchestration beyond the current one-level delegation bound only if the task model and UI stay understandable
- improve orchestration-specific status reporting beyond direct parent-child visibility
- continue refining TUI orchestration ergonomics under the still-active `Advanced Orchestration v3` milestone
