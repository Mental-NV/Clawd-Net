# ClawdNet Parity Ledger

This document is the operational migration ledger between the legacy TypeScript `claude` CLI now preserved under `Original/` and the new .NET `clawdnet` CLI at the repository root.

Use this file to answer four questions before changing behavior:

1. What does the legacy CLI actually do today?
2. What does the .NET CLI already implement?
3. Where is parity incomplete, intentionally changed, or deferred?
4. How should an agent verify parity after a change?

This file is intentionally source-driven. The current snapshot was derived from:

- Legacy TypeScript entrypoints and command registry:
  - `Original/src/entrypoints/cli.tsx`
  - `Original/src/main.tsx`
  - `Original/src/commands.ts`
  - `Original/src/commands/**`
  - `Original/src/utils/envUtils.ts`
  - `Original/src/utils/sessionStorage.ts`
- Current .NET entrypoints and command/runtime surface:
  - `ClawdNet.App/Program.cs`
  - `ClawdNet.App/AppHost.cs`
  - `ClawdNet.Core/Commands/**`
  - `ClawdNet.Core/Services/CommandDispatcher.cs`
  - `ClawdNet.Terminal/Repl/ReplHost.cs`
  - `ClawdNet.Terminal/Tui/TuiHost.cs`
  - `ClawdNet.Terminal/Console/ConsoleTerminalSession.cs`
  - `ClawdNet.Runtime/**`

Do not update statuses from memory. Re-check the code or run the verification steps in Section G first.

## A. Overview

In this repo, parity means preserving migration-critical user outcomes from the legacy TypeScript CLI in the .NET CLI, either:

- by matching the old command and behavior closely, or
- by shipping an explicit replacement surface and recording the behavioral delta here until it is accepted or corrected.

Parity is broader than command names. It includes:

- invocation shape
- flags and positional arguments
- interactive terminal flows
- config and state layout
- env var and credential assumptions
- output formats
- exit-code and error behavior
- external integration expectations

### Priority Levels

- `P0` = must-have for migration acceptance
- `P1` = important
- `P2` = nice-to-have or deferrable

### Status Values

- `Not Started` = no meaningful .NET implementation exists yet
- `In Progress` = partial implementation exists, but parity is incomplete
- `Implemented` = behavior exists in .NET, but still needs parity hardening or explicit verification
- `Verified` = implemented and explicitly verified by tests and/or smoke checks
- `Deferred` = intentionally not being worked right now
- `Dropped` = intentionally removed from migration scope
- `Changed` = .NET behavior exists, but differs materially from the legacy CLI and is not a strict parity match

## B. CLI Inventory

### B1. Legacy TypeScript CLI Inventory (`claude`)

#### Invocation Modes

The legacy CLI is not a single-mode shell. It has multiple entry paths:

- Default interactive mode:
  - `claude [prompt]`
  - launches the Ink UI by default
- Headless print mode:
  - `claude -p [prompt]`
  - supports `text`, `json`, and `stream-json`
- Fast-path entrypoint flags in `Original/src/entrypoints/cli.tsx`:
  - `--version`, `-v`, `-V`
  - `--dump-system-prompt`
  - `--claude-in-chrome-mcp`
  - `--chrome-native-host`
  - `--computer-use-mcp` when feature-gated on
  - `--daemon-worker <kind>`
  - bridge / remote-control aliases
  - daemon-style entrypoints such as `daemon`, `ps`, `logs`, `attach`, `kill`, `--bg`, `--background`
  - template-job entrypoints such as `new`, `list`, `reply`
  - `environment-runner`
  - `self-hosted-runner`
  - `--worktree --tmux`
  - `--update` and `--upgrade` rewrite to `update`
- Commander-managed subcommand mode:
  - available when not in `-p/--print` mode

#### Command Model

The legacy slash-command surface is not static. It is composed from:

- built-in commands from `Original/src/commands.ts`
- plugin commands
- skill-backed and bundled commands
- workflow-provided commands

Built-in commands also carry a type:

- `prompt` = converts the command into model-facing prompt flow
- `local` = local side effect / local output flow
- `local-jsx` = Ink UI flow

Remote and bridge modes also filter the available command set through separate allowlists.

#### Top-Level Commands and Subcommands

| Entry | Shape | Notes |
| --- | --- | --- |
| Root interactive shell | `claude [prompt]` | Interactive Ink shell by default; positional prompt is accepted |
| Headless shell | `claude -p [prompt]` | Adds structured output/input formats and print-only flags |
| `mcp` | `serve`, `add`, `remove <name>`, `list`, `get <name>`, `add-json <name> <json>`, `add-from-claude-desktop`, `reset-project-choices` | Legacy CLI includes MCP management, not just inspection |
| `auth` | `login`, `status`, `logout` | Separate auth surface |
| `plugin` / `plugins` | `validate <path>`, `list`, marketplace subcommands, `install`, `uninstall`, `enable`, `disable`, `update` | Includes marketplace/install lifecycle |
| `doctor` | no subcommand | Top-level health/diagnostic surface |
| `update` / `upgrade` | no subcommand | Top-level self-update surface |
| `install [target]` | no subcommand | Installer/bootstrap entry |
| `setup-token` | no subcommand | Token/bootstrap helper |
| `agents` | no subcommand | Agent management surface |
| Feature-gated | `server`, `ssh <host> [dir]`, `open <cc-url>`, `auto-mode`, `remote-control`, `assistant` | Present in source, not always active |
| Ant-only / internal | `up`, `rollback`, `log`, `error`, `export`, `task`, hidden `completion` | Not a public migration baseline by default |

#### Known Top-Level Aliases

- `update` <-> `upgrade`
- `plugin` <-> `plugins`
- `remote-control` <-> `rc`
- bridge / remote-control fast-path aliases include `remote`, `sync`, and `bridge`

#### Major Global Flags and Options

The root program in `Original/src/main.tsx` currently exposes at least the following user-visible flags:

| Category | Flags / Options | Notes |
| --- | --- | --- |
| Help and debug | `-h --help`, `-d --debug [filter]`, `--debug-file <path>`, `--verbose`, hidden `--debug-to-stderr` | Top-level CLI diagnostics |
| Headless / structured I/O | `-p --print`, `--output-format <text|json|stream-json>`, `--input-format <text|stream-json>`, `--json-schema <schema>`, `--include-hook-events`, `--include-partial-messages`, `--replay-user-messages`, hidden `--sdk-url <url>`, hidden `--hard-fail` | Print mode is much richer than the current .NET headless path |
| Safety and permissions | `--permission-mode <mode>`, `--dangerously-skip-permissions`, `--allow-dangerously-skip-permissions`, `--allowed-tools`, `--tools`, `--disallowed-tools`, hidden `--permission-prompt-tool <tool>` | Includes tool allow/deny filtering |
| Session / resume | `-c --continue`, `-r --resume [value]`, `--fork-session`, `--from-pr [value]`, `--session-id <uuid>`, `-n --name <name>`, `--no-session-persistence`, hidden `--resume-session-at <message-id>`, hidden `--rewind-files <user-message-id>` | Resume UX is much broader than current .NET session handling |
| Model / runtime | `--model <model>`, `--effort <level>`, `--agent <agent>`, `--betas <betas...>`, `--fallback-model <model>`, hidden `--thinking`, hidden `--max-thinking-tokens`, hidden `--max-turns`, hidden `--max-budget-usd`, hidden `--task-budget`, hidden `--workload` | Legacy runtime has more turn-budget controls |
| Context / configuration | `--settings <file-or-json>`, `--system-prompt`, `--system-prompt-file`, `--append-system-prompt`, `--append-system-prompt-file`, `--add-dir <directories...>`, `--mcp-config <configs...>`, `--strict-mcp-config`, `--plugin-dir <path>` repeatable, `--disable-slash-commands`, `--agents <json>`, `--setting-sources <sources>`, `--file <specs...>` | Important migration gap area |
| Integrations | `--ide`, `--chrome`, `--no-chrome` | Integration toggles |
| Misc hidden / feature gated | deep-link flags, teammate identity flags, `--assistant`, `--channels`, `--remote`, `--remote-control`, `--teleport` | Not baseline migration scope unless enabled |

#### Selected Built-In Slash / Local Command Families

The built-in slash-command surface is large. The most important command families currently visible in `Original/src/commands.ts` and `Original/src/commands/**` are:

- Conversation and session:
  - `help`
  - `clear` with aliases `reset`, `new`
  - `exit` with alias `quit`
  - `session` with alias `remote`
  - `resume` with alias `continue`
  - `rename`
  - `tag`
  - `compact`
  - `context`
  - `status`
  - `diff`
  - `copy`
  - `thinkback`
  - `thinkbackPlay`
- Config, safety, and model control:
  - `config` with alias `settings`
  - `model`
  - `effort`
  - `permissions` with alias `allowed-tools`
  - `hooks`
  - `color`
  - `theme`
  - `output-style`
  - `statusline`
  - `sandboxToggle`
  - `advisor`
  - `fast`
- Diagnostics and reporting:
  - `doctor`
  - `cost`
  - `stats`
  - `usage`
  - `extraUsage`
  - `insights`
  - `rateLimitOptions`
  - `release-notes`
  - `feedback` with alias `bug`
  - `heapdump`
- Integrations and workflow surfaces:
  - `mcp`
  - `plugin` with aliases `plugins`, `marketplace`
  - `ide`
  - `chrome`
  - `desktop` with alias `app`
  - `mobile` with aliases `ios`, `android`
  - `remote-env`
  - `add-dir`
  - `files`
  - `skills`
  - `agents`
  - `memory`
  - `tasks` with alias `bashes`
  - `plan`
  - `passes`
  - `terminalSetup`
  - `keybindings`
  - `vim`
  - `install-github-app`
  - `install-slack-app`
- Prompt-style slash commands:
  - `init`
  - `init-verifiers`
  - `commit`
  - `commit-push-pr`
  - `review`
  - `statusline`
  - `insights`
- Feature-gated or internal:
  - `remote-control`
  - `assistant`
  - `voice`
  - `workflows`
  - `bridge`
  - `fork`
  - `buddy`
  - `peers`
  - `torch`

#### Interactive Flows Backed by Ink

The legacy CLI uses Ink for more than the main chat shell. Source inspection shows `local-jsx` flows for at least:

- the default interactive shell
- help
- session / resume flows
- plan / passes / tasks
- model / effort / config / theme / color / output-style
- permissions / hooks / rate limits / privacy settings
- doctor / stats / usage / feedback / desktop / mobile
- plugin, MCP, skills, agents, memory, branch, diff, status, remote-env, tag, rename
- install, login, logout, terminal setup, Chrome, IDE, add-dir

These are parity-sensitive because a large amount of legacy functionality is UI-shaped rather than raw command-output shaped.

#### Config Sources

Legacy TypeScript config and state are spread across several places:

- Global config root:
  - `CLAUDE_CONFIG_DIR` override, otherwise `~/.claude`
- User-level settings and memory:
  - `~/.claude/settings.json`
  - `~/.claude/CLAUDE.md`
- Project-local settings:
  - `.claude/settings.json`
  - `.claude/settings.local.json`
- MCP configuration:
  - project `.mcp.json`
  - CLI `--mcp-config`
- Plugin and marketplace data:
  - `~/.claude/plugins/...`
  - CLI `--plugin-dir`
- Session transcripts and project state:
  - `~/.claude/projects/.../*.jsonl`
  - subagent transcripts under project/session subdirectories

#### Relevant Environment Variables

Not every legacy env var is migration-relevant. The most important user-visible and behavior-shaping variables surfaced by inspection are:

- `CLAUDE_CONFIG_DIR`
- `CLAUDE_CODE_SIMPLE` and `--bare`
- `ANTHROPIC_API_KEY`
- `ANTHROPIC_BASE_URL`
- `ANTHROPIC_MODEL`
- provider switches:
  - `CLAUDE_CODE_USE_BEDROCK`
  - `CLAUDE_CODE_USE_VERTEX`
  - `CLAUDE_CODE_USE_FOUNDRY`
- provider-region helpers:
  - `AWS_REGION`
  - `AWS_DEFAULT_REGION`
  - `CLOUD_ML_REGION`
- editor interaction:
  - `$EDITOR`
  - `$VISUAL`

Legacy auth also relies on OAuth and keychain-backed behavior in interactive mode, especially outside bare mode.

#### Output and Report Formats

Legacy output surface includes:

- interactive Ink text UI
- `--print` text output
- `--print --output-format=json`
- `--print --output-format=stream-json`
- optional JSON schema validation
- optional hook event inclusion
- optional partial message streaming
- optional stream-json stdin in SDK / bridge style flows

#### File Inputs / Outputs and External Integrations

Legacy CLI reads or writes:

- prompt and settings files
- MCP JSON files or inline JSON
- downloaded startup files via `--file`
- project-local `.claude` config
- JSONL transcripts
- plugin manifests, marketplace metadata, and plugin cache

Legacy external integrations visible from the current implementation include:

- Anthropic API
- optional Bedrock / Vertex / Foundry provider flows
- MCP servers
- LSP / IDE integration
- Chrome integration
- desktop and mobile companion flows
- auth / token setup
- remote / bridge / daemon / SSH / server flows
- GitHub and Slack app install flows

#### Error / Exit Behavior

Discoverable legacy behavior from source inspection:

- many validation failures call `process.exit(1)`
- fast-path entrypoints often use `process.exit(0)` on success and `process.exit(1)` on invalid usage
- Commander usage/help handles some invalid subcommand cases
- print-mode validation errors explicitly reject incompatible flag combinations such as:
  - `--input-format=stream-json` without `--output-format=stream-json`
  - `--sdk-url` without stream-json both ways
  - `--replay-user-messages` without stream-json both ways
  - `--include-partial-messages` without `--print` and `stream-json`
  - `--no-session-persistence` outside print mode

There is no small, consistent public exit-code taxonomy comparable to the current .NET command handlers.

### B2. Current .NET CLI Inventory (`clawdnet`)

#### Invocation Modes

The .NET CLI currently has a much smaller entry surface:

- Default interactive mode:
  - `clawdnet`
  - launches the full-screen TUI by default
- Interactive launch with root flags:
  - `clawdnet --session <id> --provider <name> --model <name> --permission-mode <mode>`
  - `clawdnet --continue`
  - `clawdnet --resume [query]`
  - `clawdnet --continue --fork-session`
  - `clawdnet --name <title>`
- Root shorthand headless flows:
  - `clawdnet [prompt]`
  - `clawdnet -p [prompt]`
  - `clawdnet --print [prompt]`
- Fallback REPL:
  - available only behind the internal `legacy-repl` feature gate
- Headless command mode:
  - explicit top-level commands only
- Plugin-defined top-level commands:
  - dispatched after built-in commands

#### Built-In Top-Level Commands

| Command | Shape | Current Notes |
| --- | --- | --- |
| `--help`, `-h` | no subcommand | Root help and `<command> --help` are supported |
| `--version`, `-v`, `-V` | no subcommand | Returns `<version> (ClawdNet)` |
| `ask` | `ask [--session <id>] [--provider <name>] [--model <name>] [--permission-mode <mode>] [--json] [--output-format <text|json|stream-json>] [--input-format <text|stream-json>] [--allowed-tools <tools...>] [--disallowed-tools <tools...>] [--system-prompt <text>|--system-prompt-file <path>] [--settings <file-or-json>] [--add-dir <paths...>] [--effort <level>] [--thinking <mode>] [--max-turns <N>] [--max-budget-usd <amount>] <prompt>` | Main headless query entry; `--settings` is the app-owned explicit settings input; `--add-dir` still exists in the implementation as transitional legacy scanning and is slated for removal from the supported contract |
| `auth` | `status`, `login`, `logout` | Env-var auth inspection plus guidance; no OAuth/keychain flow |
| `provider` | `list`, `show <name>` | Additive .NET provider surface |
| `platform` | `open <path> [--line N] [--column N]`, `browse <url>` | Additive .NET platform surface |
| `task` | `list`, `show <id>`, `cancel <id>` | Task inspection and control only |
| `plugin` | `list`, `show <name-or-id>`, `status <name>`, `install <path>`, `uninstall <name>`, `enable <name>`, `disable <name>`, `reload` | Local plugin lifecycle exists; marketplace, validate, and update flows are still missing |
| `mcp` | `list`, `ping <server>`, `tools [server]`, `get <server>`, `add <name> <command> [args...]`, `remove <name>`, `add-json <name> <json>` | Inspection plus local config management |
| `lsp` | `list`, `ping <server>`, `diagnostics <path>` | Inspection / diagnostics only |
| `session` | `new [title]`, `list`, `show <id>`, `rename <id> <new-name>`, `tag <id> <tag-name>`, `fork <id> [new-title]` | Session inspection plus metadata and branch management |
| `doctor` | no subcommand | System diagnostics for runtime, providers, config, sessions, plugins, MCP, and LSP |
| `status` | `[--session <id>]` | Session-oriented status summary |
| `stats` | `[--session <id>]` | Aggregate or per-session usage statistics |
| `usage` | `[--session <id>]` | Message-count-based usage surface with graceful degradation for token/cost tracking |
| `tool` | `tool <toolName> [args...]` | Passes joined args as raw text and `{"text": "..."}` |

#### Current Top-Level Aliases

Current built-in aliases are minimal:

- `--help` / `-h`
- `--version`
- `-v`
- `-V`
- `--continue` / `-c`
- `--resume` / `-r`

Legacy aliases such as `plugins`, `upgrade`, and `rc` are not currently mirrored by the built-in .NET dispatcher.

#### Built-In Tools Registered in the .NET Runtime

The current built-in tool registry in `AppHost.cs` contains:

- `echo`
- `file_read`
- `glob`
- `grep`
- `shell`
- `pty_start`
- `pty_focus`
- `pty_list`
- `pty_write`
- `pty_read`
- `pty_close`
- `open_path`
- `open_url`
- `apply_patch`
- `file_write`
- `task_start`
- `task_status`
- `task_list`
- `task_inspect`
- `task_cancel`
- `lsp_definition`
- `lsp_references`
- `lsp_hover`
- `lsp_diagnostics`
- plugin-defined tools
- MCP-proxied tools

#### Current Interactive Slash Commands

REPL slash commands in `ReplHost.cs`:

- `/help`
- `/session`
- `/provider`
- `/provider <name> [model]`
- `/permissions`
- `/config`
- `/rename <name>`
- `/tag <tag>`
- `/effort [level]`
- `/thinking [mode]`
- `/clear`
- `/pty`
- `/tasks`
- `/bottom`
- `/open <path> [line] [column]`
- `/browse <url>`
- `/exit`
- plain `exit`
- plain `quit`

TUI slash commands in `TuiHost.cs`:

- `/help`
- `/session`
- `/provider`
- `/provider <name> [model]`
- `/status`
- `/context`
- `/permissions`
- `/config`
- `/rename <name>`
- `/tag <tag>`
- `/effort [level]`
- `/thinking [mode]`
- `/tasks`
- `/tasks <id>`
- `/pty`
- `/pty <id>`
- `/pty status <id>`
- `/pty attach <id>`
- `/pty detach`
- `/pty close <id>`
- `/pty close-all`
- `/pty close-exited`
- `/pty fullscreen [id]`
- `/activity`
- `/clear`
- `/bottom`
- `/open <path> [line] [column]`
- `/browse <url>`
- `/exit`
- plain `exit`
- plain `quit`

#### Current TUI Keybindings

The current TUI / terminal input layer supports at least:

- `Enter` submit
- modified `Enter` for multiline insertion
- `Backspace`
- `Tab` and `Shift+Tab` focus changes
- `Up` and `Down` prompt history
- `PageUp` and `PageDown` scrolling
- `End` jump to bottom
- `F1` help
- `F2` sessions drawer
- `F3` PTY drawer
- `F4` tasks drawer
- `F5` activity drawer
- `F6` and `F7` drawer selection movement
- `F8` open selected drawer detail
- `Esc` dismiss
- `Ctrl+C` with priority:
  1. current PTY
  2. active model turn
  3. app exit if idle

#### Current Config Sources

Current active `.NET` state and config are rooted under the app data directory:

- default root:
  - `<LocalApplicationData>/ClawdNet`
  - fallback to `<app base>/.clawdnet` when local app data is unavailable
- config:
  - `config/providers.json`
  - `config/platform.json`
  - `config/mcp.json`
  - `config/lsp.json`
- plugins:
  - `plugins/<plugin-id>/plugin.json`
- persisted state:
  - `sessions.json`
  - `tasks.json`
  - `pty-transcripts/<session-id>.jsonl`

The codebase still contains legacy settings and transcript helpers for
`.claude`, `CLAUDE.md`, `.mcp.json`, and legacy JSONL transcripts.

Those helpers are transitional implementation detail only. They are not part of
the supported `.NET` configuration contract, which is limited to the app-data
configuration files under `config/` plus persisted session/task state.

#### Relevant Environment Variables

The current .NET implementation visibly depends on:

- `ANTHROPIC_API_KEY`
- `ANTHROPIC_BASE_URL`
- `OPENAI_API_KEY`
- `OPENAI_BASE_URL`
- `AWS_ACCESS_KEY_ID`
- `AWS_SECRET_ACCESS_KEY`
- `AWS_SESSION_TOKEN`
- `AWS_REGION` / `AWS_DEFAULT_REGION`
- `AWS_BEARER_TOKEN_BEDROCK`
- `CLAUDE_CODE_SKIP_BEDROCK_AUTH`
- `ANTHROPIC_BEDROCK_BASE_URL`
- `GOOGLE_APPLICATION_CREDENTIALS`
- `ANTHROPIC_VERTEX_PROJECT_ID`
- `GOOGLE_CLOUD_PROJECT` / `GCLOUD_PROJECT`
- `CLOUD_ML_REGION`
- `CLAUDE_CODE_SKIP_VERTEX_AUTH`
- `VERTEX_REGION_CLAUDE_*` (per-model region overrides)
- `ANTHROPIC_FOUNDRY_API_KEY`
- `ANTHROPIC_FOUNDRY_RESOURCE`
- `ANTHROPIC_FOUNDRY_BASE_URL`
- `CLAUDE_CODE_SKIP_FOUNDRY_AUTH`
- `$VISUAL`
- `$EDITOR`

The source tree still references `CLAUDE_CONFIG_DIR` and
`CLAUDE_CODE_DISABLE_AUTO_MEMORY` in transitional compatibility code. They are
not part of the supported `.NET` configuration contract and should disappear as
legacy settings compatibility is removed.

#### Output and Report Formats

The current .NET command surface exposes:

- full-screen TUI for interactive use
- fallback REPL behind a feature gate
- root positional prompt shorthand (`clawdnet "prompt"`)
- root `-p/--print` headless mode
- plain-text command output for most subcommands
- `ask --json`
- `ask --output-format json`
- `ask --output-format stream-json`
- `ask --input-format stream-json --output-format stream-json`

The current .NET CLI does not expose:

- JSON schema output shaping
- hook-event or partial-message stream controls

#### File Inputs / Outputs and External Integrations

Current .NET file and integration surface includes:

- JSON config files under the app data root
- plugin manifests under `plugins/`
- `sessions.json` and `tasks.json`
- PTY transcript JSONL files under `pty-transcripts/`
- platform open/browse launchers
- MCP server integration
- LSP integration
- plugin subprocess commands, hooks, and tools
- PTY subprocess sessions
- Anthropic, OpenAI, Bedrock, Vertex AI, and Foundry provider clients

Legacy settings compatibility helpers still exist in the codebase, but they are
not part of the supported product contract and are scheduled for removal from
the active runtime.

#### Error / Exit Behavior

The .NET command surface has a clearer exit-code pattern than the legacy CLI:

- `0` = success
- `1` = generic failure, invalid usage, unknown command, or tool/platform failure
- `2` = provider configuration errors, unavailable MCP/LSP servers, and similar runtime availability failures
- `3` = not found or persistence-related failures such as missing session/task/provider/plugin or conversation-store failures

Notable examples:

- `ask`
  - provider config errors -> `2`
  - conversation store errors -> `3`
- `provider show`, `task show`, `task cancel`, `plugin show`, `mcp ping`, `lsp ping`
  - missing entity -> `3`
- `mcp ping`, `lsp ping`
  - known but unavailable server -> `2`

## C. Parity Matrix

This matrix compares the legacy TypeScript CLI behavior against the desired or current .NET target. Status reflects the current .NET repo state, not aspiration.

| Area | Command / Feature | Current behavior | Target .NET behavior | Priority | Status | Notes / edge cases | Verification method |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Invocation | Root interactive shell | `claude [prompt]` launches Ink UI by default | `clawdnet` launches TUI by default; keep no-arg interactive launch as primary surface | P0 | Implemented | Current .NET matches the user outcome, and now also supports root positional prompt shorthand | Manual: `dotnet run --project ClawdNet.App --` |
| Invocation | Root positional prompt | Legacy root accepts `[prompt]` directly | `clawdnet "prompt"` routes to ask behavior | P0 | Implemented | Root positional prompt shorthand is now supported | Smoke: `clawdnet "hello"` routes to ask |
| Headless | `-p/--print` text mode | Legacy supports headless one-shot text output from root command | `-p/--print` routes to ask behavior | P0 | Implemented | `-p` and `--print` flags extract the prompt and route to ask handler | Handler tests + manual smoke |
| Headless | JSON / stream-json output | Legacy supports `text`, `json`, `stream-json`, structured stdin, hook/partial streaming | Support `text`, `json`, and `stream-json` output plus structured stdin subset | P0 | Implemented | .NET now exposes `--output-format` (text, json, stream-json) and `--input-format` (text, stream-json) on the ask command; stream-json emits NDJSON event lines via the existing StreamAskAsync pipeline; cross-flag validation enforced (input-format=stream-json requires output-format=stream-json); minimum viable event set emitted (user turn accepted, assistant deltas, tool calls, permission decisions, task events, turn completed/failed); plugin hook events, partial message dedup, replay-user-messages, sdk-url, json-schema, and hard-fail are deferred | Compare stdout shape, exit codes, and NDJSON line structure |
| Help | Root help and subcommand help | Legacy Commander help is broad and discoverable | `--help`/`-h` at root and per-command level | P0 | Implemented | Root help lists all commands with summaries; `<command> --help` shows detailed usage | Smoke: `clawdnet --help`, `clawdnet ask --help` |
| Version | `--version`, `-v`, `-V` | Legacy fast-path version output | Preserve version flags | P0 | Implemented | Straightforward parity row | Smoke: `clawdnet --version` |
| Invocation | Top-level aliases | Legacy exposes aliases such as `plugins`, `upgrade`, and `rc` for selected commands | Preserve high-value aliases or explicitly drop them | P2 | Not Started | Current built-in `.NET` aliases are still limited to help/version and continue/resume flag forms; legacy command-family aliases are still missing | Smoke: alias invocation checks |
| Session | Session create / list | Legacy has persistent sessions plus interactive resume flows | Preserve create/list and expand to full resume/discovery parity | P0 | Implemented | `.NET` has `session new`, `session list`, `session show`, `--session`, `--continue`, and `--resume` | `session list`, `session show`, interactive `--continue`, `--resume`, regression tests |
| Session | `--continue`, `--resume` (v1) | Legacy supports `-c/--continue` for most recent session, `-r/--resume [value]` for search-based resume | `.NET` supports `-c/--continue` (most recent) and `-r/--resume [query]` (ID prefix or title substring match) | P0 | Implemented | Resume loads existing session with its provider/model/message history; ambiguous matches error with candidate list; no sessions errors guide user to create one first | Smoke: `clawdnet -c`, `clawdnet -r "query"`, `clawdnet --resume` |
| Session | `--from-pr`, `--fork-session`, rewind-at-message | Legacy supports fork/resume from PRs and rewind to specific messages | `--fork-session` implemented; `--from-pr` and rewind-at-message deferred | P0 | Implemented | `--fork-session` creates a new session with copied message history when used with `--continue` or `--resume`; `--from-pr` requires external PR integration; rewind-at-message requires message selector UI | `clawdnet --continue --fork-session`, `session fork <id>`, fixture-based resume tests |
| Session | Session naming and metadata | Legacy supports `--name` and rename/tag flows | Session naming, rename, tag, and fork implemented | P1 | Implemented | `.NET` now supports `--name`/`-n` root flag, `session rename <id> <name>`, `session tag <id> <tag>` (toggle), `session fork <id> [title]`, `/rename` and `/tag` slash commands in TUI and REPL, `Tags` field on `ConversationSession` model | Command + TUI/REPL slash command checks |
| Model / provider | Model selection | Legacy root supports `--model`; slash UI includes model picker | Preserve model override and expose model state interactively | P0 | Implemented | `.NET` supports `--model` and `/provider <name> [model]`, but no dedicated `/model` UI | `ask --model`, interactive `/provider` |
| Model / provider | Provider selection | Legacy is Anthropic-first with extra provider env toggles | Keep provider first-class in .NET; do not regress session-scoped provider behavior | P1 | Changed | `.NET` adds explicit `provider` concept and `provider list/show`; built-in providers are Anthropic, OpenAI, AWS Bedrock, Google Vertex AI, and Azure Foundry; Bedrock supports standard AWS credentials, bearer token auth, and skip-auth mode; Vertex AI supports GCP service account key auth and skip-auth mode; Foundry supports API key auth and skip-auth mode | `provider list`, `ask --provider`, session persistence checks |
| Model / runtime | Effort / thinking / budgets / max turns | Legacy exposes `--effort`, `--thinking`, turn and budget controls | Add runtime controls for migration parity | P1 | Implemented | `.NET` now supports `--effort` (low/medium/high), `--thinking` (adaptive/enabled/disabled), `--max-turns` (default 8), and `--max-budget-usd` on the `ask` command; effort/thinking are passed through to provider clients; Anthropic provider supports thinking parameter with budget_tokens; `/effort` and `/thinking` slash commands added to REPL and TUI for interactive selection; budget enforcement stops query when cumulative cost exceeds limit; other providers gracefully ignore unsupported thinking parameter | `ask --effort`, `ask --thinking`, `ask --max-turns`, `ask --max-budget-usd`, `/effort`, `/thinking` slash commands |
| Permissions | Permission mode | Legacy supports `--permission-mode` and dangerous skip variants | Preserve `default`, `accept-edits`, `bypass-permissions` semantics | P0 | Implemented | `.NET` covers these three modes; dangerous-skip variants are not exposed separately | `ask --permission-mode`, interactive launch flags |
| Permissions | Tool allow / deny lists | Legacy supports `--allowed-tools`, `--tools`, `--disallowed-tools` | Add explicit CLI control if required for migration | P0 | Implemented | `.NET` now supports `--allowed-tools` and `--disallowed-tools` on the ask command; tools are filtered from the model-visible tool list; comma or space-separated lists accepted; `--tools` (base tools allowlist) is deferred | `ask --allowed-tools`, `ask --disallowed-tools` handler tests |
| Context | System prompt / settings injection | Legacy supports `--settings`, `--system-prompt`, prompt files, append prompt files | Keep explicit `.NET` prompt and settings injection; do not promise legacy settings compatibility | P0 | Implemented | `.NET` supports `--system-prompt`, `--system-prompt-file`, and `--settings` (file or inline JSON); `--settings` should remain an app-owned input surface only; legacy append prompt variants are still deferred | `ask --system-prompt`, `ask --system-prompt-file`, `ask --settings` handler tests |
| Context | `--add-dir`, trust / workspace expansion | Legacy supports explicit extra directories for tool access | Remove legacy directory scanning and rely on app-owned settings only | P0 | Changed | current implementation still scans additional `.claude` directories through `--add-dir`; that behavior is now a non-goal and should be removed from the supported surface | parser tests + end-to-end smoke after refactor |
| Integrations | MCP inspection | Legacy has list/get/add/remove/serve/reset flows | Current minimum preserves list/ping/tools; management commands added | P0 | Verified | `.NET` now supports `mcp list`, `mcp ping`, `mcp tools`, `mcp get`, `mcp add`, `mcp remove`, `mcp add-json`; config loader supports read/write with file locking; McpServerDefinition includes Transport, Url, Headers fields | `mcp list`, `mcp get`, `mcp add`, `mcp remove`, `mcp add-json` |
| Integrations | MCP management | Legacy can add/remove/get/reset project choices and import desktop config | Management commands implemented; reset/import/deferred | P1 | Implemented | `.NET` now supports `mcp add/remove/get/add-json`; `mcp serve`, `mcp add-from-claude-desktop`, `mcp reset-project-choices`, and `mcp xaa` are deferred; scope model limited to user-scope (app-data config) | Fixture-based config file checks |
| Integrations | LSP inspection / diagnostics | Legacy has IDE-oriented flows rather than a dedicated public `lsp` CLI | Keep current `.NET` `lsp list/ping/diagnostics` as minimum inspection surface | P1 | Changed | .NET surface is additive rather than parity-shaped | `lsp list`, `lsp ping`, `lsp diagnostics` |
| Plugins | Plugin inspect / reload | Legacy has plugin UI plus CLI lifecycle commands | Preserve `list/show/reload` and keep dynamic command/tool registration stable | P0 | Implemented | `.NET` covers inspect and reload, but not marketplace/install lifecycle | `plugin list`, `plugin show`, `plugin reload` |
| Plugins | Plugin install / uninstall / enable / disable / update / validate / marketplace | Legacy has full lifecycle and marketplace commands plus `plugin validate <path>` | Add install/enable/disable/update/validate/marketplace parity or document a new plugin distribution model | P1 | In Progress | `.NET` now supports local install, uninstall, enable, disable, and status flows for app-data plugins; validate, update, and marketplace flows are still missing | CLI smoke for install/uninstall/enable/disable/status plus fixture checks |
| Plugins | Plugin commands / hooks / tools | Legacy dynamic command surface comes from plugins, skills, workflows | Keep plugin-contributed commands/hooks/tools working in `.NET` | P1 | Implemented | `.NET` is ahead in some areas, but not via legacy-compatible marketplace flow | Plugin fixture tests + reload smoke |
| Auth | `auth login/status/logout` | Legacy has dedicated auth command group and token helpers | `.NET` has `auth status` showing provider auth state; `auth login`/`logout` return helpful guidance for env-var auth | P0 | Implemented | `.NET` uses env-var-only auth; OAuth/keychain from legacy CLI is documented as an intentional deviation; `auth login`/`logout` now provide setup/teardown guidance with env var examples for all providers | `auth status`, `auth login`, `auth logout` smoke tests |
| Install / update | `install`, `update`, `setup-token`, completion | Legacy includes bootstrap/update surfaces | Add if the `.NET` CLI is intended to be standalone-distributed | P1 | Not Started | Not needed for inner-loop dev, but important for user migration | Installer smoke tests |
| Tooling | Direct tool invocation | Legacy user-facing surface is mostly slash-command/UI driven | Keep `.NET` `tool <name> [args...]` as additive operator/debug surface | P2 | Changed | No direct legacy root equivalent; useful for debugging parity gaps | `tool echo hello`, tool-specific fixture tests |
| Edit workflow | Reviewable patch flow | Legacy has edit and review-oriented permission flows, but not the same .NET `apply_patch` contract | Keep current .NET reviewable patch workflow; map legacy editing expectations onto it | P0 | Changed | `.NET` has a stronger explicit patch workflow than the legacy CLI | Query/tool tests + manual approval smoke |
| PTY | Interactive command sessions | Legacy relies more on shell/Bash tools and terminal UI than explicit PTY management commands | Keep PTY as a first-class .NET feature and decide how much legacy terminal behavior must map onto it | P1 | Changed | `.NET` PTY is additive and more explicit than the legacy public CLI; true pseudo-terminal device support via Porta.Pty (forkpty/openpty on Linux/macOS, ConPTY on Windows) with pipe-based fallback; transcript persistence added (JSONL-backed, bounded storage, replay support); long-running ergonomics added (timeout, duration tracking, line counting, background session flags, TUI drawer duration/line-count display, timeout warnings); PTY resize support | PTY runtime tests + TUI smoke |
| Tasks | Task / worker orchestration | Legacy has `tasks` UI and scheduled/background concepts; not the same worker orchestration model | Keep `.NET` task system, but decide what old `/tasks` semantics must be preserved | P1 | Changed | `.NET` now supports bounded parent-child worker delegation and hierarchy inspection, but legacy scheduled-task compatibility is still unresolved | `task list/show/cancel`, TUI task drawer checks |
| Config UI | `/config`, `/theme`, `/color`, `/output-style`, `/model`, `/effort` interactive pickers | Legacy exposes many settings as interactive Ink flows | Move the highest-value ones into TUI drawers/overlays; lower-value flows can fall back to plain text | P1 | In Progress | `/config` command shows active session configuration (provider, model, session, permission mode) as TUI overlay and REPL text; `/effort` slash command implemented for REPL and TUI; `/thinking` slash command also implemented; interactive pickers for theme/color remain deferred | Manual TUI checks |
| Safety UI | `/permissions`, trust dialog, hook config views, bypass-permissions warnings | Legacy uses Ink modals and menus heavily for safety flows | Preserve as TUI modal/drawer flows, not plain text | P0 | Implemented | `/permissions` command shows current mode, tool category counts, and approval status in both TUI (overlay) and REPL (text); bypass-permissions mode shows `!` warning suffix in footer and context pane plus explicit warning in `/permissions` overlay; `/config` command shows active session configuration (provider, model, session, permission mode); hook config views remain deferred | `/permissions` and `/config` slash commands in TUI + REPL, footer/context pane warning checks |
| Reporting | `doctor`, `status`, `stats`, `usage` | Legacy has rich local or Ink-based reporting flows | `.NET` now has `doctor` (system diagnostics), `status` (session status), `stats` (usage statistics), and `usage` (token/cost usage with graceful degradation); `cost` and `insights` deferred | P1 | Implemented | `doctor` shows app version, runtime info, config file presence, provider status, session/plugin/MCP/LSP status; `status` shows current session provider, model, message count; `stats` shows aggregate session/message counts with provider and tag distribution; `usage` shows per-session message counts with graceful degradation note about token tracking | `doctor`, `status`, `stats`, `usage` command smoke tests + fixture-based output checks |
| Workflow UI | `/review`, `/init`, `/init-verifiers`, `/commit`, `/commit-push-pr`, `/branch`, `/diff` | Legacy has workflow-oriented prompt/local commands | Decide which are core migration targets vs deferrable workflows | P1 | Not Started | Some may later be plugin or skill territory instead of built-ins | End-to-end workflow fixtures |
| Productivity UI | `/skills`, `/memory`, `/agents`, `/passes`, `/plan` | Legacy has many management UIs in Ink | Decide which remain first-party vs plugin/skill surfaces in `.NET` | P1 | Deferred | Useful, but not all are necessarily core migration blockers | TUI/workflow acceptance tests |
| External surfaces | `chrome`, `desktop`, `mobile`, `ide`, GitHub/Slack app install flows | Legacy integrates with companion tools and external apps | Keep only the surfaces that are still product-relevant; defer the rest explicitly | P2 | Deferred | High scope; not required for core CLI migration | Manual only |
| Remote / distributed | daemon, ps/logs/attach/kill, remote-control, bridge, server, `ssh`, `open <cc-url>` | Legacy has substantial remote/runtime infrastructure | Re-scope deliberately; do not accidentally promise parity here | P2 | Deferred | Large separate program; not present in current .NET CLI | Dedicated design and manual smoke |
| Config compatibility | `~/.claude`, `.claude/settings*.json`, `CLAUDE.md`, `.mcp.json` | Legacy uses user and project-local layout | `.NET` uses only its own config and state layout under the app data root; legacy settings compatibility is not a migration goal | P0 | Changed | current implementation still contains transitional compatibility code and some legacy scanning behavior; the supported contract is now app-owned settings only | docs review + smoke after refactor |
| Session transcript compatibility | legacy JSONL transcripts under `~/.claude/projects/...` | Legacy uses JSONL transcripts for resume and project state | Decide separately whether legacy transcript import or resume is needed; it is not part of the settings model | P1 | Not Started | transcript compatibility is now a separate decision from configuration ownership | session import decision + fixture-based checks |
| Secrets / auth assumptions | OAuth + keychain + env in legacy, env/config in `.NET` | Legacy auth model is richer than plain env vars | `.NET` remains env/config only; OAuth/keychain is an accepted deviation | P0 | Changed | Decision documented: env-var auth covers all providers (Anthropic, OpenAI, Bedrock, Vertex, Foundry); OAuth/keychain primarily serves claude.ai subscriber experience which is out of scope; `auth login`/`logout` provide setup/teardown guidance | Provider smoke with and without config |
| Additive .NET surface | `provider` command family | No direct legacy root equivalent | Keep as additive if provider abstraction stays first-class | P2 | Implemented | New surface should not block parity work | `provider list`, `provider show` |
| Additive .NET surface | `platform open` / `platform browse`, `/open`, `/browse` | No direct legacy root equivalent | Keep as additive lightweight platform integration | P2 | Implemented | Useful operator feature; not legacy parity | `platform open`, `platform browse` |

## D. Interactive Ink Flows

This section tracks the legacy UI-style terminal flows that were implemented with Ink and require an explicit migration decision. The goal is not to preserve Ink itself. The goal is to preserve the user workflow appropriately in the .NET terminal stack.

| Legacy Ink Flow | What it does today | Target .NET handling | Priority | Risk / ambiguity |
| --- | --- | --- | --- | --- |
| Main interactive shell | Conversation transcript, prompt entry, slash commands, approvals, status, streaming | Preserve as interactive terminal UI in the TUI | P0 | High: this is the anchor surface for the whole migration |
| Resume / continue / session picker | Select or resume prior sessions, search, continue/fork | Preserve as interactive TUI drawer / picker | P0 | High: `.NET` now has `--session`, `--continue`, `--resume`, `session show`, and a TUI sessions drawer, but not full legacy picker/fork flow parity |
| Permission dialogs and trust UI | Approval flows, trust warnings, bypass-permissions confirmation, file-permission scope decisions | Preserve as interactive modal / overlay | P0 | High: safety UX must remain understandable |
| Edit review / approval dialogs | Review model-proposed edits and approve or deny | Preserve as interactive modal / overlay | P0 | Medium: `.NET` already has edit review overlays, but not full legacy surface |
| Help UI | Rich help / shortcut / command browsing | Preserve as interactive overlay or drawer | P1 | Low |
| Model / effort / settings pickers | Interactive selection of model, effort, config-related toggles | Preserve as interactive drawer for high-value settings; simplify lower-value toggles if needed | P1 | Medium |
| Permissions / hooks / privacy settings screens | Browse or edit policy-related settings | Preserve as interactive UI for core safety settings; defer niche settings if necessary | P1 | Medium | `/permissions` overlay provides view-only access to current mode and tool category counts; hook config views remain deferred |
| Plugin manager / marketplace UI | Discover, install, update, enable, disable plugins | Defer or simplify until plugin distribution model is settled | P1 | High: current `.NET` has local install/enable/disable/status flows, but no marketplace or update UI |
| MCP management UI | Browse configured MCP servers and manage config choices | Preserve as interactive UI for list/inspect first; prompt-based or file-based management is acceptable interim behavior | P1 | Medium |
| Tasks / plan / passes UI | Inspect background work, plan flows, task state | Preserve in TUI drawers and detail views; widen only as orchestration scope firms up | P1 | Medium |
| Skills / memory / agents UI | Browse or manage higher-level workflow helpers | Defer or simplify to plain text until product ownership is clearer | P1 | Medium |
| Doctor / stats / usage / cost / insights UI | Local diagnostics and usage reporting | Simplify to plain text first if necessary | P1 | Low to medium |
| Branch / diff / review workflow UIs | Repository navigation, review, diffing, branch workflow helpers | Simplify to prompt-based or plain-text flow first, then re-evaluate | P1 | Medium |
| Desktop / mobile / Chrome / IDE companion flows | Companion app or browser integrations | Defer unless the product still depends on them | P2 | High: likely broad scope |
| Install / login / logout / onboarding flows | First-run, auth, companion setup, install helpers | Simplify to prompt-based flow if shipped at all | P1 | High: depends on auth and distribution strategy |
| Remote / bridge / SSH / daemon UIs | Advanced remote/distributed operation | Defer | P2 | High: distinct product area |

## E. Config and State Compatibility

This section documents the current compatibility position between the legacy CLI and the .NET CLI. Unlike [ARCHITECTURE.md](./ARCHITECTURE.md), this section is migration-specific.

| Surface | Legacy TypeScript CLI | Current .NET CLI | Compatibility decision / current position |
| --- | --- | --- | --- |
| Global config root | `CLAUDE_CONFIG_DIR` or `~/.claude` | `<LocalApplicationData>/ClawdNet` or fallback `.clawdnet` next to app | Changed; app-data config is authoritative and legacy config roots are not a supported contract |
| User settings | `~/.claude/settings.json` | App-data config is primary; explicit `--settings` is the supported input surface | Not a goal; any current legacy settings helpers are transitional and slated for removal |
| User memory | `~/.claude/CLAUDE.md` | App-data config is primary; no supported legacy memory surface | Not a goal; any current memory compatibility behavior is transitional and slated for removal |
| Project settings | `.claude/settings.json` | App-data config is primary | Not a goal; project `.claude` settings are not part of the supported `.NET` config contract |
| Project-local overrides | `.claude/settings.local.json` | App-data config is primary | Not a goal; project `.claude` overrides are not part of the supported `.NET` config contract |
| MCP config | project `.mcp.json` plus CLI `--mcp-config` | `config/mcp.json` under app data | Changed; project `.mcp.json` and `--mcp-config` are not supported config surfaces |
| LSP config | legacy IDE/LSP settings flow | `config/lsp.json` under app data | Changed; not legacy-compatible |
| Provider config | Anthropic-first, plus provider env toggles | `config/providers.json` and explicit provider catalog | Changed; new abstraction |
| Platform config | Mostly implicit legacy shell/editor behavior | `config/platform.json` | Additive .NET behavior |
| Plugins | `~/.claude/plugins`, marketplace, `--plugin-dir` | `plugins/<plugin-id>/plugin.json` under app data plus local install/enable/disable commands | Changed; local lifecycle exists, but marketplace and `--plugin-dir` parity do not |
| Sessions | JSONL transcripts under `~/.claude/projects/...` | `sessions.json` store; `LegacyTranscriptReader` exists but is unused by current resume flow | Changed; no active legacy resume/import path yet, and this remains a separate migration decision from settings ownership |
| Worker / task state | legacy task-related project files and UI flows | `tasks.json` store | Changed |
| Secrets / auth | env, OAuth, keychain, setup/login flows | provider env vars and config files | Changed; `auth login` and `auth logout` provide env-var guidance, but OAuth and keychain remain intentionally absent |
| Editor launch assumptions | legacy CLI uses shell/editor flows implicitly in several UIs | `.NET` uses configured launcher, `$VISUAL`, `$EDITOR`, `code -g`, then OS fallback | Changed, but currently explicit and additive |

### Migration-Sensitive Assumptions

Agents should treat the following assumptions as unresolved migration work until explicit decisions are made:

- whether legacy JSONL transcripts must be importable or resumable through the live `--resume` flow
- whether auth parity requires OAuth/keychain support, or whether env/config-only auth is acceptable
- whether plugin install/update workflows should remain marketplace-driven or move to a different distribution story

## F. Intentional Deviations

The following behavior differences from the legacy CLI are explicitly accepted:

| Area | Legacy Behavior | .NET Behavior | Rationale |
| --- | --- | --- | --- |
| Configuration and memory | Legacy CLI uses `~/.claude`, project `.claude/settings*.json`, `CLAUDE.md`, `CLAUDE_CONFIG_DIR`, and project `.mcp.json` as active settings and memory surfaces | `ClawdNet` uses only its own app-data config and state layout; legacy settings compatibility is not a goal | Keeping configuration app-owned avoids hidden precedence, cross-project leakage, and partial legacy behavior that is hard to reason about; any remaining legacy loaders are transitional implementation debt and should be removed |
| Auth | OAuth/keychain-based auth with interactive login dialogs, macOS keychain storage, token refresh | Env-var-only auth; `auth login`/`logout` provide setup/teardown guidance | OAuth/keychain primarily serves claude.ai subscriber experience (web billing, feature flags, MCP marketplace) which is out of scope; env-var auth covers all providers (Anthropic, OpenAI, Bedrock, Vertex, Foundry); matches legacy `--bare` mode; simpler and more CI-friendly |

Use this section only after a behavior difference from the legacy CLI is explicitly accepted. Until then, record differences in Section C or Section E as `Changed` and treat them as unresolved parity work.

## G. Verification Checklist

Use this section to verify parity incrementally. Prefer the smallest command set that proves a change.

### G1. Build and Test Baseline

Run after any command-surface or runtime change in `.NET`:

```bash
dotnet build ./ClawdNet.slnx
dotnet test ./ClawdNet.slnx
```

When changing only docs, this is optional.

### G2. Legacy Surface Inspection

Use these to refresh the source-of-truth inventory when legacy behavior is unclear:

```bash
sed -n '1,260p' ./Original/src/entrypoints/cli.tsx
sed -n '900,1100p' ./Original/src/main.tsx
sed -n '3870,4085p' ./Original/src/main.tsx
sed -n '1,260p' ./Original/src/commands.ts
rg -n "type: 'local-jsx'|type: 'local'|type: 'prompt'" ./Original/src/commands
```

If runtime smoke tests against the legacy CLI are needed and the legacy toolchain is available, use them as manual comparison only. Do not assume they are always runnable in CI or on a fresh machine.

### G3. .NET CLI Smoke Checks

Current built-in command surface:

```bash
dotnet run --project ./ClawdNet.App -- --version
dotnet run --project ./ClawdNet.App -- --help
dotnet run --project ./ClawdNet.App -- "hello"
dotnet run --project ./ClawdNet.App -- -p "hello"
dotnet run --project ./ClawdNet.App -- auth status
dotnet run --project ./ClawdNet.App -- provider list
dotnet run --project ./ClawdNet.App -- session list
dotnet run --project ./ClawdNet.App -- session show <session-id>
dotnet run --project ./ClawdNet.App -- session rename <session-id> "Renamed Session"
dotnet run --project ./ClawdNet.App -- session tag <session-id> work
dotnet run --project ./ClawdNet.App -- task list
dotnet run --project ./ClawdNet.App -- plugin list
dotnet run --project ./ClawdNet.App -- plugin status <plugin-name>
dotnet run --project ./ClawdNet.App -- mcp list
dotnet run --project ./ClawdNet.App -- mcp get <server-name>
dotnet run --project ./ClawdNet.App -- lsp list
dotnet run --project ./ClawdNet.App -- doctor
dotnet run --project ./ClawdNet.App -- status
dotnet run --project ./ClawdNet.App -- stats
dotnet run --project ./ClawdNet.App -- usage
dotnet run --project ./ClawdNet.App -- tool echo hello
```

Headless ask flow:

```bash
dotnet run --project ./ClawdNet.App -- ask --json "hello"
dotnet run --project ./ClawdNet.App -- ask --output-format stream-json "hello"
dotnet run --project ./ClawdNet.App -- ask --settings '{"allowedTools":["echo"]}' "hello"
echo '{"type":"user","message":{"role":"user","content":"hello"}}' | dotnet run --project ./ClawdNet.App -- ask --input-format stream-json --output-format stream-json
```

Platform surface:

```bash
dotnet run --project ./ClawdNet.App -- platform open README.md
dotnet run --project ./ClawdNet.App -- platform browse https://example.com
```

### G4. Interactive Manual Checks

Run manually when changing TUI / REPL behavior:

- `dotnet run --project ./ClawdNet.App --` opens the TUI
- `dotnet run --project ./ClawdNet.App -- --session <id>` opens the requested session interactively
- `/help`, `/session`, `/provider`, `/status`, `/context`, `/permissions`, `/config`, `/rename`, `/tag`, `/effort`, `/thinking`, `/tasks`, `/pty`, `/activity`, `/open`, `/browse`, `/clear`, `/bottom`, `/exit` all work in the TUI
- `exit` and `quit` still close the interactive shell
- TUI drawers and overlays are still reachable via `F1` through `F8`, `Tab`, `Shift+Tab`, `PageUp`, `PageDown`, `End`, and `Esc`
- `Ctrl+C` still interrupts PTY first, then active turn, then exits only when idle

### G5. Fixture-Based Compatibility Checks

Use fixtures or temporary directories for migration-sensitive state:

- legacy-style config roots that should be ignored or explicitly unsupported after the refactor:
  - `~/.claude/settings.json`
  - `.claude/settings.json`
  - `.claude/settings.local.json`
  - `.mcp.json`
- `.NET` config roots:
  - `config/providers.json`
  - `config/platform.json`
  - `config/mcp.json`
  - `config/lsp.json`
  - `plugins/<id>/plugin.json`

Check explicitly whether:

- the `.NET` CLI uses only its app-owned config layout and ignores legacy settings surfaces
- providers fall back as expected when `providers.json` is absent
- platform launcher fallbacks behave as expected for configured command, env, and OS fallback
- plugin reload adds and removes plugin commands/tools/hooks as expected

### G6. Output Comparison Guidance

When comparing legacy and `.NET` output:

- normalize session ids, timestamps, task ids, provider defaults, and tool counts
- compare both `stdout` and `stderr`
- compare exit codes explicitly
- compare structured output separately from human-readable output
- for interactive flows, compare reachable states and outcomes rather than literal screen text

### G7. Update Rules For Agents

After a migration change:

- update the relevant row in Section C
- update Section D if an interactive flow decision changed
- update Section E if config/state compatibility changed
- add a note to Section F only if a deviation was explicitly approved
- keep verification notes current with the real command surface
