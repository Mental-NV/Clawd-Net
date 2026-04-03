# PLAN-02: Advanced Orchestration v3, Slice 2

## Objective

Improve task orchestration supervision and progress reporting by replacing busy-poll child waiting, adding task timeout support, enabling structured progress tracking, completing task stream event coverage, and surfacing better orchestration status in the TUI activity feed.

## Scope

This slice covers:

- replace busy-poll `WaitForChildTasksAsync` with `Task.WhenAll`-based completion tracking
- add optional task timeout (`TaskRequest.MaxDurationSeconds`, default unlimited for backward compatibility)
- use `Pending` status for newly created tasks before execution begins
- emit stream events for `task_list` and `task_inspect` tools
- add structured progress fields to task records (`ProgressPercent`, `ProgressMessage`)
- show aggregate orchestration status in TUI activity drawer

This slice does not attempt:

- a general task graph engine
- arbitrary worker-to-worker recursion
- durable live-task resume after restart
- interactive attach into worker sessions
- retry logic for failed child tasks

## Assumptions and Non-Goals

- Active execution remains process-local.
- Timeout is advisory: when elapsed, the task is canceled with a clear reason.
- `Pending` status is transitional — tasks move from `Pending` to `Running` before any work begins.
- Existing task tool payloads remain backward-compatible; new fields are additive.
- The busy-poll replacement must not change the observable behavior of parent tasks waiting for children.

## Likely Change Areas

- `ClawdNet.Core/Models/TaskRecord.cs` — add `ProgressPercent`, `ProgressMessage`
- `ClawdNet.Core/Models/TaskRequest.cs` — add `MaxDurationSeconds`
- `ClawdNet.Runtime/Tasks/TaskManager.cs` — replace busy-poll, add timeout, use `Pending` status, update progress
- `ClawdNet.Runtime/Tools/TaskStatusTool.cs` — surface progress fields
- `ClawdNet.Runtime/Tools/TaskStartTool.cs` — pass through timeout
- `ClawdNet.Runtime/Tools/TaskInspectTool.cs` — surface progress fields
- `ClawdNet.Runtime/Tools/TaskListTool.cs` — surface progress fields
- `ClawdNet.Core/Services/QueryEngine.cs` — emit stream events for `task_list` and `task_inspect`
- `ClawdNet.Terminal/Tui/TuiHost.cs` — show orchestration status in activity drawer
- `ClawdNet.Terminal/Rendering/ConsoleTuiRenderer.cs` — render progress in context panel
- `ClawdNet.Tests/TaskManagerTests.cs` — tests for timeout, pending status, progress, stream events

## Implementation Plan

1. **Add progress fields to `TaskRecord`**: `ProgressPercent?` (0-100), `ProgressMessage?`
2. **Add `MaxDurationSeconds` to `TaskRequest`**: default null (no timeout)
3. **Update `TaskManager.StartAsync`**: create task with `Pending` status, then transition to `Running` before execution
4. **Replace `WaitForChildTasksAsync`**: use `TaskCompletionSource` per child + `Task.WhenAll` instead of 50ms polling
5. **Add timeout support in `RunWorkerAsync`**: when `MaxDurationSeconds` is set, start a cancellation token that fires after the duration
6. **Update task tools**: surface `ProgressPercent` and `ProgressMessage` in JSON output
7. **Add stream events for `task_list` and `task_inspect`**: emit `TaskUpdatedStreamEvent` with appropriate messages
8. **Update TUI activity drawer**: show aggregate orchestration status (e.g., "2 running, 1 completed")
9. **Update context renderer**: show progress percentage when available
10. **Add tests**: timeout cancellation, pending->running transition, progress field propagation, stream event coverage
11. Run sequential validation and targeted smoke tests.
12. Update `docs/PARITY.md`, `docs/PLAN.md`, and `docs/PLAN-02.md` as needed.

## Implementation Results

- Added progress tracking fields to `TaskRecord`:
  - `ProgressPercent` (nullable int, 0-100)
  - `ProgressMessage` (nullable string)
- Added `MaxDurationSeconds` to `TaskRequest` for optional task timeout
- Updated `TaskManager.StartAsync`:
  - tasks now start with `Pending` status before transitioning to `Running`
  - clear Pending -> Running transition event for UI visibility
- Replaced busy-poll `WaitForChildTasksAsync`:
  - now uses `TaskCompletionSource` per child + `Task.WhenAll`-based waiting
  - eliminates 50ms polling loop
  - parent waiting is notified when children complete or are canceled
- Added task timeout support in `RunWorkerAsync`:
  - when `MaxDurationSeconds` is set, a cancellation token fires after the duration
  - timeout produces a clear "timed out after Xs" message
  - integrates cleanly with existing cascade cancellation
- Updated all task tools to surface progress fields:
  - `task_start`, `task_status`, `task_list`, `task_inspect` all include `progressPercent` and `progressMessage`
  - `task_start` accepts `maxDurationSeconds` input parameter
- Added stream events for `task_list` and `task_inspect` tools
- Updated TUI activity drawer to show aggregate orchestration status (running/pending/completed counts)
- Updated context renderer to show progress percentage and progress message when available
- Updated `ParseTaskRecord` in QueryEngine to handle progress fields safely

## Validation Results

Completed sequentially:

1. `dotnet build ./ClawdNet.slnx`
   - passed
2. `dotnet test ./ClawdNet.slnx`
   - passed
   - `115` tests passing (113 existing + 2 new)
   - new tests: `Task_manager_records_timeout_cancellation`, `Task_manager_transitions_from_pending_to_running`
3. Smoke checks
   - `dotnet run --project ./ClawdNet.App -- task list`
     - passed

## Remaining Follow-Ups For This Milestone

- widen orchestration beyond the current one-level delegation bound only if the task model and UI stay understandable
- improve orchestration-specific status reporting beyond direct parent-child visibility
- consider adding structured progress update mechanism (e.g., model can report progress percentage during long tasks)
- continue refining TUI orchestration ergonomics under the still-active `Advanced Orchestration v3` milestone
