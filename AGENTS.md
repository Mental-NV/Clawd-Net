# AGENTS.md

## Project purpose

`ClawdNet` is the .NET 10 replatforming workspace for the legacy TypeScript CLI now preserved under `Original/`. The repo exists to migrate the important command surface and interactive terminal experience into a maintainable .NET implementation without losing the behaviors users depend on.

The source system is a TypeScript + React + Ink CLI. The target system is a .NET 10 CLI with shared runtime layers, a headless command surface, and a full-screen interactive terminal UI. The working goal is functional parity for the important command surface unless a difference is explicitly documented.

## Core working rules

- Read the authoritative documents before coding.
- Prefer small, reversible changes over broad rewrites.
- Preserve existing behavior unless a deliberate deviation is recorded.
- Do not claim completion without running the required validation commands.
- Commit validated execution-slice changes on the current branch before moving on.
- Update documentation when behavior, architecture, or migration status changes.

## Code quality rules

- Prefer simple, readable code.
- Avoid unnecessary abstractions.
- Avoid speculative rewrites outside the current scope.
- Keep changes bounded to the current task.

## Validation rules

- Run validations sequentially, not in parallel.
- Treat failing validation commands as blockers.
- Do not declare success when validations are skipped or blocked.
- If a task is docs-only and you do not run validation, say that explicitly.

## Documentation rules

- [PLAN.md](./docs/PLAN.md) is the living execution plan.
- [PARITY.md](./docs/PARITY.md) is the source of truth for migration parity status.
- Work through [PLAN.md](./docs/PLAN.md) milestone by milestone until all roadmap items are complete, unless the user explicitly redirects.
- If task status, migration status, or roadmap state changes, update [PLAN.md](./docs/PLAN.md).
- If behavior, parity, or compatibility changes, update [PARITY.md](./docs/PARITY.md).
- If project-wide rules, assumptions, or defaults change, update [ARCHITECTURE.md](./docs/ARCHITECTURE.md).

## Authoritative documents

Read these before making non-trivial changes:

- [README.md](./README.md)
  - current supported command surface, local setup, and config file locations
- [ARCHITECTURE.md](./docs/ARCHITECTURE.md)
  - stable architectural decisions, assumptions, defaults, and constraints
- [PLAN.md](./docs/PLAN.md)
  - current execution order, active priorities, and milestone-level work planning
- [PARITY.md](./docs/PARITY.md)
  - migration ledger for legacy TypeScript CLI parity, compatibility, and verification

## When in doubt

- Choose the simplest interpretation consistent with the docs.
- Preserve parity.
- Document intentional deviations.
