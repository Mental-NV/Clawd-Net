# migration-doc-sync

## Purpose

Keep the repo's stable migration documentation aligned with the actual implementation state.

## When to use

Use this skill after changes that affect:

- command surface
- parity status
- architecture, defaults, or assumptions
- roadmap or milestone state
- user-facing configuration or usage guidance

## Inputs

- the code change set
- validation results
- current authoritative docs:
  - `../../README.md`
  - `../../docs/ARCHITECTURE.md`
  - `../../docs/PLAN.md`
  - `../../docs/PARITY.md`
  - `../../AGENTS.md`

## Expected outputs

- only the docs that need updates are changed
- status changes are reflected consistently
- no stale commands, flags, or file paths remain in docs
- no unsupported claims are introduced

## Workflow

1. Read the authoritative docs before editing any of them.
2. Classify the change:
   - user-facing command/config change
   - parity change
   - project-wide architecture/default change
   - roadmap or milestone-status change
   - repo-wide agent-operating-rule change
3. Update the minimum correct document set:
   - `../../README.md` for current supported behavior and config locations
   - `../../docs/PARITY.md` for migration status and compatibility
   - `../../docs/PLAN.md` for milestone progress and execution order
   - `../../docs/ARCHITECTURE.md` for stable defaults, assumptions, and constraints
   - `../../AGENTS.md` only for durable repo-wide operating rules
4. Cross-check command names, flags, file paths, and status labels across the changed docs.
5. If a behavior difference is intentional and approved, record it in `../../docs/PARITY.md`.

## Guardrails

- Do not copy roadmap detail into `../../AGENTS.md`.
- Do not duplicate architecture content in `../../docs/PLAN.md`.
- Do not update docs from intent alone; reflect actual implementation state.
- Do not mark milestones or parity items complete without corresponding validation evidence.
- Do not add issue-specific or temporary debugging notes to stable docs.

## Definition of done

- every changed doc has a clear reason to change
- no authoritative docs contradict each other for the edited area
- migration status and implementation state are aligned
- any skipped doc update is intentional, not accidental
