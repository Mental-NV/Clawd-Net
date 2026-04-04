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

## Active Milestone

### [v] Legacy Context and Config Compatibility v1

- Priority: P0
- Why now: this is the largest cluster of currently documented parity drift in the live runtime. `--settings` and `--add-dir` are advertised but not functionally wired, and the staged legacy `.claude` / `.mcp.json` / JSONL compatibility helpers are not part of the active ask, TUI, or resume flow.
- Deliverables:
  - wire `--settings` into the active query path or stop exposing it as supported
  - wire `--add-dir` into the active settings / memory / MCP compatibility path or remove the surface
  - decide and implement the active compatibility behavior for legacy `.claude` settings, `CLAUDE.md`, project `.mcp.json`, and legacy JSONL transcript resume/import
  - update `README.md`, `ARCHITECTURE.md`, and `PARITY.md` to match the real compatibility contract
- Main risks:
  - accidentally over-merging legacy config into the new runtime
  - changing model behavior in ways that are hard to observe without targeted regression tests
  - expanding compatibility semantics without a clean precedence model
- Exit criteria:
  - no advertised context/config compatibility flag is parser-only
  - active runtime compatibility behavior is verified end-to-end and documented consistently
  - `PARITY.md` no longer describes this area as parser-only or inactive helper-only behavior

## Next Milestones

### [v] Safety and Approval UX Parity v1

- Priority: P0
- Depends on: Legacy Context and Config Compatibility v1
- Deliverables:
  - broaden TUI safety surfaces beyond the current approval/edit overlays
  - add visible trust / permissions / hook-config flows where parity requires them
  - reconcile the current stronger `.NET` edit-review flow with legacy user expectations, documenting any accepted deviation
- Main risks: TUI scope creep and unclear boundary between parity vs additive UX improvement
- Exit criteria: `PARITY.md` no longer lists safety UI as only partially implemented

## Next Milestones

### [v] Session Branching and Resume Parity v2

- Priority: P0
- Depends on: Legacy Context and Config Compatibility v1
- Deliverables:
  - `--fork-session` flag implemented: creates new session with copied history when used with `--continue` or `--resume`
  - `--name` / `-n` root flag implemented: sets session title at launch
  - `session rename <id> <new-name>` CLI subcommand added
  - `session tag <id> <tag-name>` CLI subcommand added (toggle behavior)
  - `session fork <id> [title]` CLI subcommand added
  - `/rename` slash command added to REPL (already existed in TUI)
  - `/tag` slash command added to TUI and REPL
  - `Tags` field added to `ConversationSession` model
  - `session show` now displays tags
- Main risks: session-history mutation semantics and compatibility with persisted transcripts
- Exit criteria: unresolved P0 session parity rows are either implemented and verified or moved to intentional deviations

### [v] MCP Parity v2

- Priority: P0/P1
- Depends on: Legacy Context and Config Compatibility v1
- Deliverables:
  - `mcp get <server>` shows detailed server info (transport, command, args, env, headers, runtime state)
  - `mcp add <name> <command> [args...]` adds stdio MCP servers with `-e/--env` and `--read-only-tools` flags
  - `mcp remove <name>` removes servers from config
  - `mcp add-json <name> <json>` adds servers from JSON string
  - `McpServerDefinition` model extended with `Transport`, `Url`, `Headers` fields
  - `McpConfigurationLoader` supports read/write with file locking
  - `IMcpClient` extended with `GetServerDefinitionsAsync`, `AddServerAsync`, `RemoveServerAsync`
- Main risks: config precedence conflicts between app-data config and project-local config
- Exit criteria: `PARITY.md` MCP rows are either verified or intentionally deviated
  - Deferred: `mcp serve`, `mcp add-from-claude-desktop`, `mcp reset-project-choices`, `mcp xaa`, `--mcp-config` root flag, `/mcp` slash command

### [/] Auth and Migration Compatibility Decision

- Priority: P0
- Depends on: Legacy Context and Config Compatibility v1
- Deliverables:
  - decide whether `.NET` will remain env-var auth only or add OAuth/keychain parity
  - implement the chosen path or record an explicit accepted deviation in `PARITY.md`
  - ensure auth docs and command help are aligned with the chosen migration contract
- Main risks: opening a large auth surface without full product intent
- Exit criteria: auth is no longer an unresolved P0 changed area

### [ ] Runtime Controls and Settings UI v1

- Priority: P1
- Depends on: Safety and Approval UX Parity v1
- Deliverables:
  - add missing high-value runtime controls such as effort/thinking/budget surfaces where migration requires them
  - add the first high-value config/model/settings interactive pickers in TUI
- Main risks: adding knobs without clear provider/runtime support underneath
- Exit criteria: the highest-value `P1` model/runtime/config UI gaps are no longer not-started

### [ ] Reporting and Workflow Surface Recovery

- Priority: P1
- Depends on: Safety and Approval UX Parity v1
- Deliverables:
  - restore or intentionally replace the most important reporting surfaces (`doctor`, `status`, `stats`, `usage`, `cost`, `insights`)
  - decide which workflow commands remain first-party vs plugin/skill territory
- Main risks: large surface area with mixed product ownership
- Exit criteria: the major reporting/workflow parity rows are either implemented, deferred with rationale, or explicitly deviated

## Execution Notes

- Keep completed execution-slice detail in the corresponding `docs/PLAN-XX.md` files, not here.
- When a milestone is completed, mark it `[v]`, promote the next highest-priority unresolved item to `[/]`, and keep the ordering aligned with unresolved `P0` then `P1` rows in [PARITY.md](./PARITY.md).
