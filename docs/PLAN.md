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

### [v] Plugin Lifecycle Parity v2

- Priority: P1
- Depends on: Edit Workflow Parity Decision v1
- Why next: plugin inspect/reload and local lifecycle flows exist, but `PARITY.md` still shows plugin lifecycle parity as partial because validate, update, and marketplace behavior are unresolved.
- Deliverables:
  - [x] decided the supported local plugin lifecycle contract for install, uninstall, enable, disable, status, reload, and validation
  - [x] implemented `plugin validate <path>` command (medium effort, high parity value)
  - [x] implemented `plugin disable --all` flag (low effort)
  - [x] implemented `plugin uninstall --keep-data` flag (low effort)
  - [x] documented marketplace and update behavior as out of scope (intentional deviations)
  - [x] documented single-scope (app-data plugins directory) as the supported model
  - [x] updated PARITY.md, ARCHITECTURE.md, and PLAN.md accordingly
- Main risks:
  - opening a large distribution surface accidentally
  - mixing local-dev plugin workflows with end-user marketplace expectations
- Exit criteria:
  - [x] plugin lifecycle parity rows are no longer `In Progress` or `Not Started` without an explicit reason
  - [x] `plugin validate <path>` implemented and tested
  - [x] `plugin disable --all` implemented
  - [x] `plugin uninstall --keep-data` implemented
  - [x] marketplace/update/scope documented as deferred/intentional deviations
  - [x] PARITY.md plugin lifecycle rows resolved
  - [x] ARCHITECTURE.md updated
  - [x] dotnet build passes
  - [x] dotnet test passes

### [v] Legacy Transcript Import Decision v1

- Priority: P1
- Depends on: Own Settings Only Refactor v1
- Why now: legacy settings compatibility is no longer a goal, and legacy JSONL transcript compatibility has now been explicitly dropped rather than kept as an open migration question.
- Deliverables:
  - [x] decided that legacy transcript import and resume are not migration goals
  - [x] removed transcript compatibility from the active parity target set
  - [x] documented the decision as an intentional deviation in [PARITY.md](./PARITY.md) and [ARCHITECTURE.md](./ARCHITECTURE.md)
- Main risks:
  - conflating session import with configuration compatibility
  - keeping dead transcript compatibility code without a product outcome
- Exit criteria:
  - [x] transcript compatibility removed from the open migration ledger

### [v] Auth Parity and Provider Defaults v1

- Priority: P0
- Depends on: Own Settings Only Refactor v1
- Why now: auth is now the last unresolved `P0` migration area. Env-var-only auth is no longer the accepted end state, and the explicit provider model is accepted but still needs smoother defaults where practical.
- Deliverables:
  - [x] add OAuth-capable auth support without regressing current env-var-based provider auth
  - [x] revise `auth login` and `auth logout` so they no longer frame OAuth as an intentional non-goal
  - [x] preserve explicit provider selection while smoothing default provider and model behavior where it materially improves usability
  - [x] update [PARITY.md](./PARITY.md), [ARCHITECTURE.md](./ARCHITECTURE.md), and [README.md](../README.md) to match the implemented auth contract
- Main risks:
  - OAuth flow complexity, including callback handling and token refresh
  - choosing a token persistence model that is secure enough without dragging in unnecessary platform complexity
  - regressing existing env-var-based and CI-friendly provider flows while broadening auth support
- Exit criteria:
  - [x] the auth parity rows in [PARITY.md](./PARITY.md) are no longer unresolved
  - [x] provider selection remains explicit, with smoother defaults documented and tested
  - [x] `auth login`, `auth status`, and `auth logout` reflect the supported auth contract

### [v] Config UI and Interactive Settings Parity v1

- Priority: P1
- Depends on: Auth Parity and Provider Defaults v1
- Why next: the TUI and REPL now expose `/config`, `/permissions`, `/effort`, and `/thinking`, but only the highest-value remaining picker flows should be migrated.
- Deliverables:
  - [x] promote only the highest-value remaining interactive settings flows into TUI drawers or overlays
  - [x] focus picker work on provider/model, runtime controls, and closely related safety/config surfaces
  - [x] leave lower-value theme/color/output-style parity as plain-text or deferred unless strong migration value emerges
  - [x] keep slash-command help and TUI help overlays aligned with the actual interactive surface
- Main risks:
  - TUI scope creep
  - implementing visually richer settings flows without clear parity value
- Exit criteria:
  - [x] config and settings picker rows in [PARITY.md](./PARITY.md) are either implemented, deferred with rationale, or intentionally changed

### [v] PTY and Task Workflow Parity v1

- Priority: P1
- Depends on: Config UI and Interactive Settings Parity v1
- Why next: PTY and task orchestration are both ahead of the legacy CLI in some ways, but the remaining parity work should be limited to targeted workflow mapping, not model replacement.
- Deliverables:
  - [x] map only a small number of high-value legacy terminal workflows onto the current PTY model
  - [x] keep the worker-task model as the baseline and add only the minimum scheduled/background behavior that proves necessary
  - [x] close the highest-value UX and inspection gaps that block migration acceptance
  - [x] document any accepted deviations where the `.NET` model intentionally replaces the legacy flow
- Main risks:
  - trying to force old task semantics onto the new orchestration model
  - broadening PTY scope into a separate terminal product
- Exit criteria:
  - [x] PTY and task parity rows are either verified or explicitly deviated with rationale

### [ ] Workflow Command Recovery v1

- Priority: P1
- Depends on: Auth Parity and Provider Defaults v1
- Why next: legacy workflow commands such as `/review`, `/init`, `/commit`, `/branch`, and `/diff` are still absent from the first-party `.NET` surface, and the chosen direction is now to restore most of them rather than only a minimal subset.
- Deliverables:
  - restore most legacy workflow commands that still belong in the first-party CLI
  - draw and document the boundary between first-party workflow commands and plugin or skill territory
  - keep any remaining workflow scope aligned with plugins and skills instead of ad hoc built-ins
- Main risks:
  - importing too much product-specific workflow behavior without clear ownership
  - overlapping built-ins with plugin or skill territory
- Exit criteria:
  - workflow UI rows in [PARITY.md](./PARITY.md) no longer remain untriaged

### [ ] Alias Compatibility v1

- Priority: P2
- Depends on: Workflow Command Recovery v1
- Why next: a small number of legacy aliases are worth keeping for migration smoothness, but broad alias parity is not.
- Deliverables:
  - identify the small high-value alias set worth preserving
  - implement only those aliases
  - document intentionally dropped aliases in [PARITY.md](./PARITY.md)
- Main risks:
  - cluttering the command dispatcher with low-value compatibility shims
  - expanding alias scope beyond what materially helps migration
- Exit criteria:
  - top-level alias parity is no longer an untriaged gap in [PARITY.md](./PARITY.md)

### [v] Distribution and Bootstrap Surface Decision v1

- Priority: P1
- Depends on: Workflow Command Recovery v1
- Why now: the product direction is now explicit: distribution stays outside the CLI for now, and shell completion is the only lightweight follow-up worth reconsidering later.
- Deliverables:
  - [x] decided that ClawdNet will not ship first-party install, update, or setup-token commands in the current migration scope
  - [x] documented distribution as external to the CLI for now
  - [x] kept shell completion as an optional future follow-up only if it is cheap and clearly valuable
- Main risks:
  - tying runtime migration work to packaging/distribution choices prematurely
  - documenting end-user promises that the repo does not actually support
- Exit criteria:
  - [x] distribution and bootstrap parity rows moved out of the open decision set and documented with rationale
