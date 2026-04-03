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
- [v] Advanced Orchestration v3
- [v] Full TUI Parity v3
- [v] PTY UX v3
- [v] Plugin Platform v4

## Current Execution Order

Unless explicitly redirected, execute future work in this order:

1. Provider and Platform Expansion v2

## Active Milestone

- [/] Provider and Platform Expansion v2
  - Slice 1 (PLAN-13): AWS Bedrock provider — in progress
  - Slice 2: Google Vertex AI provider — pending
  - Slice 3: Azure Foundry provider — pending

## Next Milestones

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

- All high-priority milestones (P1-P4) are now complete.
- `Provider and Platform Expansion v2` is the easiest milestone to reprioritize if a user explicitly needs it sooner.
- Future work beyond the current roadmap should be scoped and added as new milestones in this file.
