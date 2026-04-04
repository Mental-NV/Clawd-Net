# ClawdNet Architecture

This document captures the architectural decisions, assumptions, defaults, and rollout choices that have shaped `ClawdNet` so far. It is intended to be the working baseline for future implementation work, not just a historical summary.

## Purpose

`ClawdNet` is the .NET replatforming of the Bun-first TypeScript CLI and terminal application now preserved under `Original/`. The project has been built milestone by milestone, with a strong bias toward:

- preserving a usable product at every stage
- reusing one shared runtime across all surfaces
- landing feature slices incrementally instead of attempting one full rewrite
- keeping compatibility paths alive until replacement surfaces are stable

## Current Product Position

`ClawdNet` is no longer a thin CLI prototype. The following core capabilities are now ported:

- session-backed conversations
- buffered and streaming query execution
- a headless `ask` surface
- a fallback REPL
- a full-screen TUI as the default interactive shell
- permission-gated tool execution
- reviewable patch-based edits
- PTY-backed interactive command sessions
- worker tasks and task inspection
- MCP and LSP integration
- plugin commands, hooks, and plugin-defined tools
- multi-provider model support
- lightweight platform launching for editor and browser actions

The remaining gaps are now mostly in deeper workflow parity, orchestration depth, and higher-end product polish.

## Architectural Principles

The system has been built around a few consistent choices:

- The backend runtime is authoritative. UI layers should consume shared runtime behavior rather than reimplementing it.
- Headless CLI, fallback REPL, and full TUI should all ride on the same query, tool, permission, task, and persistence stack.
- New abstractions are added alongside existing ones when necessary; working paths are not replaced abruptly unless the replacement is already stable.
- Interactive features that involve active processes or long-running state are process-local by default unless durable resumption is explicitly designed.
- Console-native ANSI/VT rendering is preferred over introducing a third-party TUI framework.
- Compatibility and regression resistance are more important than perfectly minimal abstractions during the migration period.

## Solution Structure

The solution is intentionally layered:

- `ClawdNet.App`
  - composition root, command routing, interactive launch selection
- `ClawdNet.Core`
  - domain models, abstractions, command handlers, query orchestration, permissions, tool contracts
- `ClawdNet.Runtime`
  - concrete implementations for providers, stores, PTY, plugins, tools, platform launchers, MCP, LSP
- `ClawdNet.Terminal`
  - fallback REPL, full TUI host, terminal models, renderers, input handling
- `ClawdNet.Tests`
  - unit, integration, and regression coverage across the shared runtime and interactive surfaces

This layering is deliberate. Domain and orchestration behavior are meant to remain reusable even if terminal surfaces continue to evolve.

## Runtime Model

### Conversations and Sessions

Conversation sessions are the unit of continuity across headless and interactive use.

- Sessions are persisted as JSON-backed records.
- Sessions hold the active provider and model for future turns.
- Existing sessions missing newer fields are normalized on load rather than migrated destructively.
- Current normalization default for legacy sessions without provider metadata is `anthropic`.

### Query Execution

The query engine is now provider-agnostic and drives both buffered and streaming flows.

- Interactive mode uses real streaming rather than simulated incremental output.
- Buffered `ask` remains the default headless CLI behavior.
- Partial streamed assistant text is treated as UI-only until it reaches a safe commit point.
- Persisted state is only updated at safe boundaries such as committed assistant messages and committed tool results.
- The same query engine is used by normal sessions and worker sessions.

### Provider Resolution

Provider selection is explicit and first-class.

- Provider is session-scoped and task-scoped.
- Per-turn overrides may update an existing session's provider and model for future turns.
- Provider choice is never inferred from a model name.
- The shared model contracts remain `ModelRequest`, `ModelResponse`, and `ModelStreamEvent`.
- All built-in provider adapters normalize into the same shared model layer.

Current provider defaults:

- Built-in provider scope is `Anthropic + OpenAI + AWS Bedrock + Google Vertex AI + Azure Foundry`.
- If `providers.json` is missing, built-in in-memory provider definitions are seeded.
- Legacy sessions and tasks without provider data normalize to `anthropic`.
- Anthropic keeps its current environment-variable-friendly setup.
- OpenAI support is built in, but its default model should come from configuration or explicit user choice rather than a hardcoded runtime default.
- AWS Bedrock supports standard AWS credentials (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN`), bearer token auth (`AWS_BEARER_TOKEN_BEDROCK`), and skip-auth mode (`CLAUDE_CODE_SKIP_BEDROCK_AUTH=1`). Region defaults to `us-east-1` via `AWS_REGION`/`AWS_DEFAULT_REGION`. Custom endpoints are supported via `ANTHROPIC_BEDROCK_BASE_URL`. Bedrock uses the Converse API with AWS SigV4 signing and supports ARN-format model IDs and cross-region inference profiles.
- Google Vertex AI supports GCP service account key authentication via `GOOGLE_APPLICATION_CREDENTIALS`, project ID via `ANTHROPIC_VERTEX_PROJECT_ID`/`GOOGLE_CLOUD_PROJECT`/`GCLOUD_PROJECT`, and skip-auth mode (`CLAUDE_CODE_SKIP_VERTEX_AUTH=1`). Region defaults to `us-east5` via `CLOUD_ML_REGION`, with per-model overrides via `VERTEX_REGION_CLAUDE_*` env vars. Model IDs use the `model-name@YYYYMMDD` format with automatic resolution from short names. Vertex AI uses the `rawPredict` and `streamGenerateContent` endpoints with Anthropic-compatible message format.
- Azure Foundry supports API key auth via `ANTHROPIC_FOUNDRY_API_KEY` and skip-auth mode (`CLAUDE_CODE_SKIP_FOUNDRY_AUTH=1`). Endpoint is constructed from `ANTHROPIC_FOUNDRY_RESOURCE` (format: `https://{resource}.services.ai.azure.com/anthropic`) or overridden via `ANTHROPIC_FOUNDRY_BASE_URL`. Model names are simple deployment identifiers. Foundry uses the same Anthropic messages API format.
- Auth is currently environment-variable-based across providers; legacy OAuth/keychain flows are not implemented.

## Persistence Model

Persistence is intentionally conservative.

- Sessions and tasks are persisted.
- PTY transcripts are now persisted as bounded JSONL transcripts under `<AppData>/ClawdNet/pty-transcripts/<session-id>.jsonl`.
- PTY transcripts store recent output chunks with sequence numbering for ordered replay.
- PTY transcript storage is bounded (default 1000 chunks per session) to avoid unbounded disk growth.
- Active execution state for PTY and running tasks is not durably resumed after restart.
- Persisted task records keep enough summary and inspection metadata to make `task show` and the TUI useful without loading entire worker transcripts.
- PTY output is bounded and clipped for in-memory display, but full transcript history is available from disk.
- Worker transcript persistence is stored as bounded preview and tail data, not as a second full transcript copy.

This reflects an explicit decision: persist durable metadata and safe summaries first, not arbitrary live-process state. PTY transcript persistence provides replay capability within and across app sessions.

## Tool and Permission Model

The permission system remains central across the entire product.

- Built-in tools, plugin tools, PTY actions, task actions, and model-triggered platform actions all flow through the same permission concepts.
- Read-only tools are allowed by default.
- Write and execute tools are permission-gated unless the current permission mode bypasses them.
- `accept-edits` auto-allows reviewable edit application, but does not auto-allow PTY or task execute actions.
- Plugin-defined tool categories map onto the same `readOnly`, `write`, and `execute` semantics as built-in tools.
- If a plugin tool omits a valid category, it is treated conservatively as execute-class.
- Model-triggered `open_path` and `open_url` are treated as execute-class tools.
- User-triggered CLI and slash-command platform actions execute directly without approval prompts.

This was a deliberate unification decision. New capabilities should plug into existing permission semantics instead of growing side-channel approval logic.

## Edit Workflow

Structured patch review is the preferred editing path.

- `apply_patch` is the preferred model-facing code-edit tool.
- `file_write` still exists for compatibility.
- Review happens as a batch per turn rather than per hunk.
- Diff preview is read-only.
- Approval is coarse: approve the whole batch or reject it.
- If any patch in the batch is invalid, the full batch is rejected to avoid partial surprise application.

This keeps the edit experience safe and understandable without implementing a full patch staging UI yet.

## PTY Architecture

PTY support exists as a distinct long-lived execution surface.

- PTY is not a replacement for the existing one-shot `shell` path.
- PTY sessions use a true pseudo-terminal device via Porta.Pty (`Porta.Pty` NuGet package).
- Porta.Pty provides cross-platform PTY support: forkpty/openpty on Linux/macOS, ConPTY on Windows.
- The `TruePtySession` implementation runs commands directly in the PTY (not wrapped in a shell).
- If true PTY allocation fails, the system falls back to the pipe-based `SystemPtySession`.
- PTY sessions are process-local and conservative.
- PTY startup remains validated and permission-gated.
- PTY output is bounded and clipped for in-memory display (4096 chars).
- PTY transcripts are persisted to disk as JSONL files with bounded storage (default 1000 chunks per session).
- PTY transcript replay is available via `GetTranscriptAsync` for ordered output retrieval.
- Multi-session PTY management is supported.
- One PTY session is always the current focused session for writes and live display.
- Full PTY replay persistence and restart-time resume are intentionally not implemented.
- PTY sessions support optional timeouts: a background monitor terminates the process on expiry.
- PTY sessions track duration (computed from start/completion timestamps) and output line count.
- PTY sessions can be marked as background (model-initiated vs user-initiated).
- The TUI PTY drawer displays duration, line count, background status, and timeout warnings.

Current interrupt priority is an explicit product default:

1. interrupt the current running PTY session
2. interrupt the active model turn
3. exit the app only when neither of the above applies

## Task Architecture

Worker tasks are first-class records backed by their own worker sessions.

- Parent and worker conversations are separate sessions linked by task metadata.
- Direct parent-child task relationships are supported for bounded delegated work.
- Worker tasks run through the same query engine, tool stack, permissions, and persistence behavior as normal turns.
- Tasks are persisted; active execution is process-local.
- Worker tasks inherit the parent session's provider and model unless they are explicitly overridden.
- Read-only worker inspection is supported.
- Running or pending tasks discovered on startup are normalized to interrupted instead of being resumed automatically.
- Parent tasks can supervise direct child tasks and wait for child completion before finalizing.

Current task scope is intentionally bounded:

- no task graph engine
- no arbitrary worker-to-worker recursion
- delegated child-task spawning is currently bounded to one additional level
- no durable live-task resumption after restart
- no interactive attach into worker sessions

This was a conscious decision to build safe inspection and control before deeper autonomy.

## Plugin Architecture

The plugin platform is local, manifest-driven, and subprocess-based.

- Plugins are discovered from the local filesystem.
- Plugins can currently contribute MCP servers, LSP servers, commands, hooks, and tools.
- Plugin execution is subprocess-only.
- No in-process plugin loading is supported.
- Invalid plugin extensions invalidate only that extension entry, not the whole plugin.
- Built-in command and tool names are reserved and cannot be overridden by plugins.
- Plugin reload must add, update, and remove plugin contributions cleanly without requiring app restart.

Hook behavior is intentionally conservative:

- plugin hooks are observational or augmenting by default
- hook failures should be surfaced but remain non-fatal unless explicitly configured otherwise

Plugin-defined tools were designed to participate in the same tool registry, permission model, transcript semantics, and UI activity surfaces as built-in tools.

## MCP and LSP Integration

MCP and LSP are integrated as runtime subsystems rather than special-case UI features.

- They are configured from local config files and plugin manifests.
- They surface model-visible tools through the shared tool pipeline.
- Their runtime activity is visible through transcript and terminal activity surfaces rather than dedicated management dashboards.
- Dedicated MCP and LSP commands remain available for non-interactive inspection and diagnostics.

The architecture intentionally keeps these integrations inside the common runtime and tool model instead of creating a second subsystem interface.

## Interactive Surfaces

### Headless CLI

The headless CLI remains a first-class surface.

- `ask` is buffered by default.
- Root positional prompts and `-p/--print` route into the same headless query path.
- `ask` supports `text`, `json`, and `stream-json` output modes, plus `stream-json` stdin.
- Non-interactive commands are expected to remain behaviorally stable across milestones.
- New capabilities should be added through the shared runtime first, then exposed to the CLI.

### Fallback REPL

The fallback REPL still exists for compatibility and stabilization.

- It remains useful during TUI rollout and regression recovery.
- It is retained behind an internal compatibility path rather than being the primary no-arg interactive shell.
- It should stay compatible with shared runtime features such as providers, PTY, tasks, permissions, and platform actions.

### Full TUI

The full-screen TUI is now the default interactive entry point.

- Running `clawdnet` with no subcommand launches the TUI by default.
- The TUI is conversation-first rather than dashboard-first.
- Drawers are used for browsing and detail views.
- Overlays are used for interruptive flows such as approvals, edit review, help, and blocking errors.
- Slash commands remain available, but are secondary to the key-driven TUI.
- Rendering remains console-native and uses deterministic full-frame redraw rather than a more complex diff renderer.

This reflects a deliberate product decision: the TUI is the primary surface, but it should be layered on the shared runtime rather than becoming a separate application architecture.

## Platform Integration

Platform integration is intentionally narrow in v1.

Current scope:

- open file or path in an editor
- open URL in a browser

Out-of-scope for the current phase:

- voice and audio workflows
- deep IDE protocol integration
- native host daemons
- broader OS automation surfaces

Current launcher defaults are explicit:

- editor launch preference order is configured command, then `$VISUAL` or `$EDITOR`, then `code -g` when available, then OS open fallback
- browser launch preference order is configured command, then OS default opener

## Configuration Strategy

The project favors file-based local configuration over heavier runtime setup systems.

Current config categories include:

- providers
- platform launch preferences
- MCP servers
- LSP servers
- plugins

When configuration files are absent, the project prefers sane defaults or in-memory built-ins rather than forcing an initial setup ceremony.

### Legacy Config Compatibility

The codebase currently contains staged compatibility helpers for the legacy TypeScript CLI configuration layout, but they are not yet wired into the active query, TUI, or session-resume path.

What exists today:

- `LegacyConfigPaths` mirrors `CLAUDE_CONFIG_DIR` and the legacy `~/.claude` path layout
- `LegacySettingsLoader` can parse and merge `settings.json` and `settings.local.json`
- `MemoryFileLoader` can read `CLAUDE.md` and `rules/*.md`
- `ProjectMcpConfigLoader` can parse project `.mcp.json` files
- `LegacyTranscriptReader` can parse legacy JSONL transcript files

Current limitation:

- active runtime behavior still uses `config/providers.json`, `config/platform.json`, `config/mcp.json`, `config/lsp.json`, `sessions.json`, and `tasks.json` under the `.NET` app-data root
- `ask --settings` is parsed but not applied to the current query path
- `ask --add-dir` is parsed but not currently connected to settings, memory, or MCP loading
- legacy JSONL transcripts are not yet part of the live `--resume` or `--continue` flow

This is an explicit migration gap, not an accepted compatibility result.

## Rollout and Compatibility Decisions

Several rollout choices have been consistent across milestones:

- new features should land in the shared runtime before getting specialized UI behavior
- compatibility paths are retained during transitions when they materially reduce migration risk
- headless and non-interactive flows should not regress to support richer interactive features
- new metadata fields should be normalized on load rather than requiring destructive upgrades
- partial or live-only UI state should not be persisted unless there is a clear durable contract for it

These choices explain many current implementation details and should continue to guide future migration work.

## Testing and Verification Defaults

The project has followed a consistent testing posture:

- each milestone should include focused unit coverage
- new behavior should include integration or regression coverage where it crosses subsystem boundaries
- existing tests should continue to pass unless behavior intentionally changes
- runtime behavior should be verified first, then exercised through REPL or TUI surfaces where relevant
- local validation uses `dotnet build` and `dotnet test` as the standard baseline

We have intentionally accepted fake-process, handler-level, and parser-level tests for some provider and platform paths where live external service validation would add cost without changing the architectural decision.

## Explicit Defaults and Assumptions

These defaults are now part of the working project baseline:

- default legacy provider normalization is `anthropic`
- provider choice is explicit, not model-name inferred
- built-in providers are Anthropic, OpenAI, AWS Bedrock, Google Vertex AI, and Azure Foundry
- auth is currently environment-variable-based rather than OAuth/keychain-based
- Bedrock uses AWS SigV4 signing, bearer token auth, or skip-auth mode
- Bedrock region defaults to `us-east-1` via `AWS_REGION`/`AWS_DEFAULT_REGION`
- Bedrock supports ARN-format model IDs and cross-region inference profiles
- Vertex AI uses GCP service account key auth or skip-auth mode
- Vertex AI region defaults to `us-east5` via `CLOUD_ML_REGION`
- Vertex AI model IDs use `model-name@YYYYMMDD` format with automatic short-name resolution
- Foundry uses API key auth or skip-auth mode
- Foundry endpoint is derived from resource name or custom base URL
- headless `ask` stays buffered by default
- interactive mode uses real streaming
- patch-based edit review is the preferred model editing path
- edit approval is batch-based and coarse
- PTY remains bounded, clipped, and process-local
- PTY sessions use true pseudo-terminal devices via Porta.Pty (with pipe-based fallback)
- Porta.Pty provides cross-platform PTY: Linux/macOS (forkpty), Windows (ConPTY)
- PTY transcripts are persisted to disk with bounded storage (1000 chunks/session)
- PTY transcript replay is available for ordered output retrieval
- PTY sessions support optional timeouts with automatic process termination
- PTY sessions track duration and output line count
- PTY sessions can be marked as background (model-initiated) vs user-initiated
- TUI PTY drawer shows duration, line count, background status, and timeout warnings
- tasks persist metadata but do not durably resume active execution
- worker inspection is read-only
- plugin execution remains subprocess-based
- the TUI is the default no-arg interactive shell
- slash commands remain available but secondary in the TUI
- platform integration is limited to opening files and URLs for now

## Deferred or Intentionally Missing Work

The following areas have been intentionally deferred:

- exact screen-for-screen TypeScript parity
- deeper autonomous orchestration and task graphs
- durable resume for running tasks
- PTY overlay/full-screen terminal mode — implemented via PtyFullScreen overlay
- PTY output pagination/scrolling in TUI
- graceful interrupt signaling (SIGINT vs SIGTERM)
- plugin-defined agents and broader marketplace or install flows
- deep editor or IDE integrations beyond open-file and open-URL
- voice and audio features
- mouse support, fuzzy search, and heavyweight dashboard-style pane systems

These are not omissions by accident. They are deferred scope choices made to keep the migration coherent and shippable.

## How To Use This Document

When making future architectural changes:

- treat this document as the current baseline rather than an immutable contract
- prefer updating this file when a milestone changes a cross-cutting assumption
- record both the new capability and the default or constraint that came with it
- preserve the distinction between what is implemented now and what is intentionally deferred

That discipline is important because `ClawdNet` has reached the point where most future work will be about refining system boundaries and product defaults, not just adding missing classes.
