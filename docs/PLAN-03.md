# PLAN-03: Advanced Orchestration v3, Slice 3

## Objective

Complete the `Advanced Orchestration v3` milestone by expanding task graph support beyond single-level delegation, adding task dependency tracking, implementing non-blocking supervision, and surfacing richer task orchestration state across CLI and TUI.

## Scope

This slice covers:

- multi-level task graph support (increase delegation depth to 2 levels: root -> child -> grandchild)
- task dependency declarations (a task can declare explicit dependencies on other tasks)
- non-blocking supervision (parent can monitor children without synchronous waiting)
- task dependency resolution (tasks with unmet dependencies wait before executing)
- richer task-tree visualization in TUI
- task graph inspection via CLI

This slice does not attempt:

- a general DAG task engine with arbitrary dependencies
- durable live-task resume after restart
- interactive attach into worker sessions
- task retry logic
- cross-session task orchestration

## Assumptions and Non-Goals

- Active execution remains process-local.
- Task graph is still tree-shaped (no cross-parent dependencies).
- Dependencies are declared at task creation time, not dynamically added.
- Existing single-worker flows remain backward-compatible.
- MaxDelegationDepth increases from 1 to 2 (root + 2 child levels).

## Likely Change Areas

- `ClawdNet.Core/Models/TaskRecord.cs` — add `DependsOnTaskIds`
- `ClawdNet.Core/Models/TaskRequest.cs` — add `DependsOnTaskIds`
- `ClawdNet.Runtime/Tasks/TaskManager.cs` — increase depth limit, resolve dependencies, non-blocking supervision
- `ClawdNet.Runtime/Tools/TaskStartTool.cs` — accept dependency declarations
- `ClawdNet.Runtime/Tools/TaskInspectTool.cs` — surface dependency info
- `ClawdNet.Runtime/Tools/TaskStatusTool.cs` — surface dependency info
- `ClawdNet.Runtime/Tools/TaskListTool.cs` — surface dependency info
- `ClawdNet.Terminal/Tui/TuiHost.cs` — richer task-tree rendering
- `ClawdNet.Terminal/Rendering/ConsoleTuiRenderer.cs` — show dependency chains
- `ClawdNet.Core/Commands/TaskCommandHandler.cs` — graph inspection output
- `ClawdNet.Tests/TaskManagerTests.cs` — multi-level delegation, dependency resolution, supervision

## Implementation Plan

1. **Add `DependsOnTaskIds` field** to `TaskRecord` and `TaskRequest`
2. **Increase `MaxDelegationDepth`** from 1 to 2 in `TaskManager`
3. **Update `RunWorkerAsync`**:
   - check dependencies before executing
   - if dependencies are unmet (pending/running), wait for them non-blockingly using `TaskCompletionSource`
   - if any dependency failed, fail the dependent task with clear message
4. **Update task tools** to surface dependency data
5. **Update TUI** to render multi-level task trees with dependency arrows
6. **Update CLI task commands** to show dependency chains
7. **Add tests**:
   - multi-level task creation (root -> child -> grandchild)
   - dependency resolution (task waits for dependency, fails if dependency fails)
   - depth enforcement at new limit (level 3 rejected)
   - non-blocking supervision
8. Run sequential validation and smoke tests.
9. Update authoritative docs.

## Implementation Results

- Increased `MaxDelegationDepth` from 1 to 2 (root -> child -> grandchild)
- Added `DependsOnTaskIds` field to `TaskRecord` and `TaskRequest`
- Added dependency resolution in `RunWorkerAsync`:
  - tasks with unmet dependencies wait before executing
  - failed dependencies cause dependent task to fail with clear message
  - uses `TaskCompletionSource` pattern for non-blocking wait
- Updated all task tools to surface dependency data:
  - `task_start` accepts `dependsOnTaskIds` array input
  - all tools return `dependsOnTaskIds` and `dependencyCount`
- Updated TUI task detail view to show `dependsOn` field
- Updated context renderer to show dependency chains in task list
- Added tests for multi-level delegation (3 levels) and dependency storage

## Validation Results

Completed sequentially:

1. `dotnet build ./ClawdNet.slnx`
   - passed
2. `dotnet test ./ClawdNet.slnx`
   - passed
   - `117` tests passing (115 existing + 2 new)
   - new tests: `Task_manager_supports_multi_level_delegation`, `Task_manager_stores_dependency_information`
3. Smoke checks
   - `dotnet run --project ./ClawdNet.App -- task list`
     - passed

## Remaining Follow-Ups For This Milestone

- broader task-graph and orchestration supervision work continues beyond this milestone
- consider task retry logic for failed dependencies
- consider cross-session task orchestration
- continue with next milestone: Full TUI Parity v3
