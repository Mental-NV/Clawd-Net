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

## Current Execution Order

- The roadmap is now driven by unresolved `P0` and then `P1` gaps in [PARITY.md](./PARITY.md).
- Finish one milestone at a time, in order, unless the user explicitly redirects.

## Roadmap

### [v] Own Settings Only Refactor v1

- Priority: P0
- Why now: the architecture and parity decision is now explicit: legacy settings compatibility is not a goal. The runtime still contained transitional `.claude` / `CLAUDE.md` / `CLAUDE_CONFIG_DIR` / project `.mcp.json` compatibility paths. These have been removed so the app has only its own settings model.
- Deliverables:
  - [x] removed legacy settings, memory, and project `.mcp.json` compatibility from the active ask and interactive runtime paths
  - [x] narrowed `--settings` to app-owned explicit settings input only
  - [x] removed `--add-dir` as a legacy settings compatibility surface
  - [x] removed transitional references to legacy settings env vars from the supported runtime path
  - [x] updated tests and docs so the supported contract is unambiguously app-owned configuration only
- Main risks:
  - leaving dead compatibility code connected to the runtime in subtle ways
  - breaking explicit `.NET` settings injection while removing legacy-scanning behavior
  - doc and help-text drift if parser surfaces remain after behavior changes
- Exit criteria:
  - [x] the live runtime no longer depends on legacy settings compatibility helpers
  - [x] `ARCHITECTURE.md` and `PARITY.md` describe app-owned configuration as the only supported contract
  - [x] `PARITY.md` no longer treats legacy settings compatibility as an unresolved migration target

### [v] Edit Workflow Parity Decision v1

- Priority: P0
- Depends on: Own Settings Only Refactor v1
- Why next: edit review is still marked as a `Changed` P0 area in [PARITY.md](./PARITY.md). The current patch-first workflow is deliberate, but the migration ledger still treats it as unresolved.
- Deliverables:
  - [x] compare legacy edit/review expectations with the current patch-batch approval model
  - [x] close any remaining approval/review UX gaps that are necessary for migration acceptance
  - [x] if the patch-first model is the accepted replacement, record it as an intentional deviation
- Main risks:
  - conflating product improvement with strict parity
  - over-expanding the edit surface beyond current runtime needs
- Exit criteria:
  - [x] the edit workflow row in [PARITY.md](./PARITY.md) is verified
  - [x] intentional deviation documented with rationale

### [ ] Plugin Lifecycle Parity v2

- Priority: P1
- Depends on: Edit Workflow Parity Decision v1
- Why next: plugin inspect/reload and local lifecycle flows exist, but `PARITY.md` still shows plugin lifecycle parity as partial because validate, update, and marketplace behavior are unresolved.
- Deliverables:
  - decide the supported local plugin lifecycle contract for install, uninstall, enable, disable, status, reload, and validation
  - either implement missing local lifecycle commands or explicitly document them as out of scope
  - decide the migration position for marketplace and update behavior
- Main risks:
  - opening a large distribution surface accidentally
  - mixing local-dev plugin workflows with end-user marketplace expectations
- Exit criteria:
  - plugin lifecycle parity rows are no longer `In Progress` or `Not Started` without an explicit reason

### [ ] Legacy Transcript Import Decision v1

- Priority: P1
- Depends on: Own Settings Only Refactor v1
- Why next: legacy settings compatibility is no longer a goal, but legacy JSONL transcript import or resume is still a separate open question in [PARITY.md](./PARITY.md).
- Deliverables:
  - decide whether legacy transcript import or resume is needed at all
  - if yes, design it as an explicit import or migration surface rather than implicit settings compatibility
  - if no, record the decision as a documented non-goal or intentional deviation
- Main risks:
  - conflating session import with configuration compatibility
  - keeping dead transcript compatibility code without a product outcome
- Exit criteria:
  - transcript compatibility is either implemented as an explicit workflow or removed from the open migration ledger

### [ ] Config UI and Interactive Settings Parity v1

- Priority: P1
- Depends on: Edit Workflow Parity Decision v1
- Why next: the TUI and REPL now expose `/config`, `/permissions`, `/effort`, and `/thinking`, but settings-picker parity is still incomplete.
- Deliverables:
  - promote the highest-value remaining interactive settings flows into TUI drawers or overlays
  - decide which legacy picker-style flows are worth preserving vs simplifying to plain text
  - keep slash-command help and TUI help overlays aligned with the actual interactive surface
- Main risks:
  - TUI scope creep
  - implementing visually richer settings flows without clear parity value
- Exit criteria:
  - config and settings picker rows in [PARITY.md](./PARITY.md) are either implemented, deferred with rationale, or intentionally changed

### [ ] PTY and Task Workflow Parity v1

- Priority: P1
- Depends on: Config UI and Interactive Settings Parity v1
- Why next: PTY and task orchestration are both ahead of the legacy CLI in some ways, but [PARITY.md](./PARITY.md) still marks them as changed because the mapping from legacy terminal/task workflows is unresolved.
- Deliverables:
  - decide which legacy `/tasks` and terminal workflows must map onto current PTY and worker-task features
  - close the highest-value UX and inspection gaps that block migration acceptance
  - document any accepted deviations where the `.NET` model intentionally replaces the legacy flow
- Main risks:
  - trying to force old task semantics onto the new orchestration model
  - broadening PTY scope into a separate terminal product
- Exit criteria:
  - PTY and task parity rows are either verified or explicitly deviated with rationale

### [ ] Workflow Command Recovery v1

- Priority: P1
- Depends on: PTY and Task Workflow Parity v1
- Why next: legacy workflow commands such as `/review`, `/init`, `/commit`, `/branch`, and `/diff` are still absent from the first-party `.NET` surface.
- Deliverables:
  - decide which workflow commands remain first-party migration targets
  - implement the smallest high-value workflow set or move them to explicit defer/deviation status
  - keep any remaining workflow scope aligned with plugins and skills instead of ad hoc built-ins
- Main risks:
  - importing too much product-specific workflow behavior without clear ownership
  - overlapping built-ins with plugin or skill territory
- Exit criteria:
  - workflow UI rows in [PARITY.md](./PARITY.md) no longer remain untriaged

### [ ] Distribution and Bootstrap Surface Decision v1

- Priority: P1
- Depends on: Workflow Command Recovery v1
- Why next: legacy `install`, `update`, `setup-token`, and shell-completion surfaces still have no `.NET` migration decision.
- Deliverables:
  - decide whether ClawdNet will ship first-party install and update commands
  - decide the migration position for token/bootstrap helpers and shell completion
  - document accepted non-goals if distribution remains external to the CLI
- Main risks:
  - tying runtime migration work to packaging/distribution choices prematurely
  - documenting end-user promises that the repo does not actually support
- Exit criteria:
  - distribution and bootstrap parity rows are either implemented, deferred with rationale, or intentionally dropped
