# PLAN-16: Root Help, -p/--print, and Root Positional Prompt

## Objective

Add three high-visibility CLI surface improvements to match legacy parity:

1. **`--help` support** at root level and per-command level
2. **Root positional prompt shorthand** (`clawdnet "hello"` behaves like `clawdnet ask "hello"`)
3. **`-p/--print` headless mode** as an alternative to `ask`

## Scope

- `AppHost.RunAsync` entry point logic
- Command dispatcher and handlers
- Built-in command help metadata
- Root argument parsing updates

## Assumptions

- Help output should be plain text, terminal-friendly, similar to Commander-style help.
- `-p/--print` mode is functionally equivalent to `ask` but invoked from root.
- Root positional prompt only applies when args contain a single non-flag token or `-p/--print` plus a prompt.
- These changes preserve the existing TUI default when no args are given.

## Non-Goals

- Full stream-json, JSON schema, or structured stdin (these remain separate P0 items).
- Resume family (`--continue`, `--resume`) — separate milestone.
- System prompt / settings injection (`--settings`, `--system-prompt`) — separate milestone.
- Legacy config compatibility layer — separate milestone.

## Files Changed

- `ClawdNet.Core/Abstractions/ICommandHandler.cs` — added `HelpSummary` and `HelpText` properties
- `ClawdNet.Core/Commands/*.cs` — implemented help metadata on all 10 handlers
- `ClawdNet.Core/Commands/HelpCommandHandler.cs` — new handler for `--help`/`-h`
- `ClawdNet.Core/Services/CommandDispatcher.cs` — updated unknown command message
- `ClawdNet.App/AppHost.cs` — root arg parsing for `--help`, `-p/--print`, positional prompt; help handler wired first
- `ClawdNet.Tests/HelpAndPrintModeTests.cs` — new test file with 17 tests

## What Was Done

### Step 1: Add Help Metadata to Command Handlers
- Added `HelpSummary` (one-liner) and `HelpText` (detailed usage) to `ICommandHandler`.
- Implemented on all 10 existing handlers: ask, provider, platform, task, plugin, session, lsp, mcp, tool, version.

### Step 2: Created HelpCommandHandler
- Handles `--help`/`-h` at root level — lists all commands with summaries.
- Handles `<command> --help` — delegates to the target handler's `HelpText`.
- Wired as the first handler in the dispatch chain so `--help` always wins.

### Step 3: Updated AppHost Root Arg Parsing
- `TryParsePrintMode`: detects `-p`/`--print` and extracts the prompt, routes to ask.
- `TryParseRootPositionalPrompt`: single non-flag arg treated as ask prompt.
- `ExecuteAskAsync`: reuses the existing `AskCommandHandler` with reconstructed args.

### Step 4: Updated CommandDispatcher Error Message
- Changed from listing all commands to: `"Unknown command. Use --help for available commands."`

### Step 5: Tests
- 17 new tests covering help handler, positional prompt parsing, and print mode parsing.
- All 197 tests pass (180 existing + 17 new).

## Validation Results

```
dotnet build ./ClawdNet.slnx  — succeeded (0 errors, 0 warnings)
dotnet test ./ClawdNet.slnx   — 197 passed, 0 failed
```

Smoke checks:
- `clawdnet --help` — shows root help with command list ✓
- `clawdnet ask --help` — shows ask-specific usage ✓
- `clawdnet "hello world"` — routes to ask (API key error expected) ✓
- `clawdnet -p "hello world"` — routes to ask ✓
- `clawdnet --print "hello world"` — routes to ask ✓

## PARITY.md Updates

- Root interactive shell: notes updated to reflect positional prompt support
- Root positional prompt: Changed → Implemented
- `-p/--print` text mode: In Progress → Implemented
- Root help and subcommand help: Not Started → Implemented

## PLAN.md Updates

- Added "Root Help, -p/--print, and Root Positional Prompt" to completed milestones
- Updated Execution Notes with PLAN-16 summary and remaining P0 gaps

## Remaining Follow-ups

- Session resume family (`--continue`, `--resume`, `--from-pr`)
- System prompt / settings injection (`--settings`, `--system-prompt`)
- Stream-json output and structured stdin
- Auth CLI (`auth login/status/logout`)
- Legacy config compatibility layer
