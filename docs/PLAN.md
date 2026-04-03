# ClawdNet Execution Plan

This is the living execution roadmap for `ClawdNet`.

Use this file to decide what to build next. Use [ARCHITECTURE.md](./ARCHITECTURE.md) for current architecture, defaults, assumptions, and system constraints. Do not duplicate that material here unless a milestone changes it.

## How To Maintain This Plan

- Keep future milestones in execution priority order.
- Mark exactly one milestone as `[/]` when active.
- Mark a milestone as `[v]` only after implementation is complete and local verification has passed.
- Keep executing roadmap items until all remaining milestones are complete, unless the user explicitly redirects or stops the work.
- After each completed execution slice, commit the validated slice changes on the current branch before starting the next slice.
- If a milestone changes a project-wide default or architectural constraint, update [ARCHITECTURE.md](./ARCHITECTURE.md) in the same change.
- Keep this file execution-focused: priority, dependencies, concrete deliverables, risks, and exit criteria.

## Status Key

- `[ ]` not implemented
- `[/]` in progress
- `[v]` implemented

## Completed Milestones

These areas are already landed and should be treated as the current foundation, not near-term roadmap items.

- [v] Core runtime and session foundation
- [v] Rich interactive terminal and REPL foundation
- [v] True streaming query execution
- [v] Reviewable patch-based edit workflow
- [v] PTY runtime and PTY UX v1-v2
- [v] Task orchestration v1-v2
- [v] Plugin platform v2-v3
- [v] Full TUI program v1-v2
- [v] Provider and platform expansion v1

## Current Execution Order

Unless explicitly redirected, execute future work in this order:

1. Advanced Orchestration v3
2. Full TUI Parity v3
3. PTY UX v3
4. Plugin Platform v4
5. Provider and Platform Expansion v2

## Active Milestone

- [ ] Advanced Orchestration v3
  - slice 1 landed: `docs/PLAN-01.md` (parent-child delegation, hierarchy inspection)
  - slice 2 landed: `docs/PLAN-02.md` (busy-poll replacement, timeout, progress tracking, stream events)

## Next Milestones

### [/] Advanced Orchestration v3

- Priority: `P1`
- Effort: `4-6 weeks`
- Risk: `High`
- Why next:
  - this is the highest-leverage remaining capability gap
  - it builds directly on the existing task, PTY, plugin, and TUI foundations
- Deliverables:
  - richer task graph support beyond single-worker flows
  - explicit parent-child task relationships
  - stronger delegation and background supervision
  - clearer orchestration progress and status visibility in CLI and TUI
- Dependencies:
  - current task orchestration v2 foundation
- Main risks:
  - concurrency and cancellation edge cases
  - unclear UX for parent-child task state
  - persistence complexity as task relationships become deeper
- Exit criteria:
  - orchestration is materially richer than current single-worker execution
  - parent-child relationships are inspectable and understandable
  - task lifecycle remains stable across CLI, TUI, and persisted records
  - existing task, PTY, plugin, and query flows still pass regression coverage
- Current progress:
  - slice 1 landed bounded parent-child task delegation and hierarchy inspection
  - slice 2 landed busy-poll replacement, task timeout, progress tracking, and stream event coverage
  - broader task-graph and orchestration supervision work remains

### [ ] Full TUI Parity v3

- Priority: `P2`
- Effort: `4-6 weeks`
- Risk: `High`
- Why this is second:
  - the TUI already exists as the primary shell, but still needs deeper workflow polish and parity
  - stronger orchestration surfaces will make this work more concrete
- Deliverables:
  - richer workflow ergonomics for sessions, tasks, approvals, activity, and navigation
  - fewer fallbacks to slash-command-only flows
  - stronger conversation-first interaction polish in the default shell
- Dependencies:
  - no hard dependency, but benefits from orchestration improvements landing first
- Main risks:
  - hidden parity scope
  - UI complexity outpacing shared runtime models
  - regressions across TUI, fallback REPL, and headless commands
- Exit criteria:
  - the TUI handles the main workflows without awkward fallback paths
  - interaction polish improves without breaking shared runtime behavior
  - session, task, PTY, and approval flows remain coherent inside the TUI

### [ ] PTY UX v3

- Priority: `P3`
- Effort: `3-4 weeks`
- Risk: `High`
- Why this is third:
  - PTY works today, but long-running terminal workflows still have meaningful UX depth left
- Deliverables:
  - richer attach and detach semantics
  - better long-running PTY ergonomics
  - clearer terminal-mode behavior beyond the current bounded context and overlay model
- Dependencies:
  - current PTY multi-session manager and PTY TUI surfaces
- Main risks:
  - process lifecycle edge cases
  - ambiguous current-session focus
  - interrupt behavior regressions
- Exit criteria:
  - PTY sessions feel robust for longer-lived coding workflows
  - focus and attach semantics are clear
  - interrupt and transcript behavior remain predictable

### [ ] Plugin Platform v4

- Priority: `P4`
- Effort: `3-5 weeks`
- Risk: `Medium-High`
- Why this is fourth:
  - plugin extensibility should expand only after orchestration and TUI surfaces settle further
- Deliverables:
  - richer plugin capability beyond current subprocess commands, hooks, and tools
  - better compatibility and lifecycle handling for a broader extension surface
  - extension points that remain aligned with the shared runtime and permission model
- Dependencies:
  - benefits from more stable orchestration and TUI surfaces
- Main risks:
  - exposing unstable host APIs too early
  - compatibility burden growing faster than the core product
  - security and permission ambiguity for richer extension behaviors
- Exit criteria:
  - plugin capability expands without breaking existing plugin contributions
  - extension points remain disciplined and testable

### [ ] Provider and Platform Expansion v2

- Priority: `P5`
- Effort: `2-4 weeks`
- Risk: `Medium-High`
- Why this is fifth:
  - it is useful, but less central than orchestration, TUI, and PTY depth
- Deliverables:
  - broader provider coverage and/or deeper platform integration
  - stronger editor or native integration beyond the current open-path and open-URL actions
- Dependencies:
  - no hard dependency
- Main risks:
  - provider API drift
  - OS and editor integration variance
  - growing platform-specific complexity in shared runtime code
- Exit criteria:
  - added providers or platform actions integrate cleanly with the existing provider-neutral runtime
  - session and task behavior remains explicit and stable

## Execution Notes

- Prefer finishing `Advanced Orchestration v3` before starting deeper TUI polish work.
- `Full TUI Parity v3` and `PTY UX v3` should be coordinated so terminal interaction changes do not fight each other.
- `Plugin Platform v4` should follow stabilization of orchestration and TUI surfaces rather than defining new extension points too early.
- `Provider and Platform Expansion v2` is the easiest milestone to reprioritize if a user explicitly needs it sooner.
