# Ralph Loop for Clawd-Net

This folder contains the refactored Ralph Loop implementation using validated JSON-driven orchestration with support for multiple agent providers.

## Overview

The Ralph Loop orchestrates execution of backlog items defined in `docs/backlog.json`. It supports three agent backends: **Qwen**, **Claude Code**, and **Codex**, providing a provider-agnostic execution loop with consistent operator experience.

## Supported Providers

### Qwen
- **CLI**: `qwen`
- **YOLO mode**: `--yolo`
- **Progress**: Rich structured progress via `qwen_pretty_stream.py`
- **Output format**: `stream-json` with partial messages

### Claude Code
- **CLI**: `claude`
- **YOLO mode**: `--dangerously-skip-permissions`
- **Progress**: Best-effort rendering via `simple_stream_renderer.py`
- **Output format**: `stream-json` with partial messages

### Codex
- **CLI**: `codex exec`
- **YOLO mode**: `--dangerously-bypass-approvals-and-sandbox`
- **Progress**: Best-effort line-based rendering via `simple_stream_renderer.py`
- **Output format**: Plain text (no structured events)

## Architecture

### Authoritative State: `docs/backlog.json`

The execution backlog is stored in `docs/backlog.json` with:
- Structured item definitions (id, title, status, priority, order, dependencies, deliverables, exit criteria)
- Explicit state model: `todo`, `in_progress`, `ready_for_validation`, `done`, `blocked`, `deferred`, `cancelled`
- Validation commands that must pass before marking items as `done`
- Dependency graph for execution ordering
- Timestamps for tracking progress

### Schema Validation: `docs/backlog.schema.json`

JSON Schema defining structural constraints:
- Required fields and types
- Status and priority enums
- Kebab-case ID patterns
- Checklist item structure

### Semantic Validation: `backlog_validator.py`

Python script enforcing business rules:
- Unique item IDs and order values
- Valid dependency references
- Acyclic dependency graph
- At most one active item
- Dependencies satisfied before activation
- Done items have all deliverables/criteria complete
- Blocked items include reason
- Valid state transitions

### Orchestration: `ralph_loop.py`

Python-based loop with provider abstraction:
- Deterministic item selection (priority, then order)
- Atomic backlog updates
- Lock-based concurrency control
- Git safety checks
- Validation command execution
- State transitions with timestamps
- Provider-agnostic execution

### Provider Abstraction: `providers.py`

Clean abstraction layer for agent backends:
- `QwenProvider` - Qwen with rich progress
- `ClaudeCodeProvider` - Claude Code with best-effort progress
- `CodexProvider` - Codex with line-based progress
- Automatic provider detection and availability checking

### Progress Rendering

**Rich progress (Qwen only):**
- `qwen_pretty_stream.py` - Full event parsing, tool summarization, colored output
- Logs raw JSONL to `ralph/logs/qwen-stream/`

**Best-effort progress (Claude Code, Codex):**
- `simple_stream_renderer.py` - Handles stream-json events or plain text
- Graceful degradation for providers with less structured output

## Usage

### List Available Providers

```bash
python3 ralph/ralph_loop.py --list-providers
```

### Validate Backlog

```bash
python3 ralph/backlog_validator.py
# or
python3 ralph/ralph_loop.py --validate-only
```

### Show Next Item

```bash
python3 ralph/ralph_loop.py --show-next
```

### Run Loop with Specific Provider

```bash
# Qwen (default)
python3 ralph/ralph_loop.py --provider qwen

# Claude Code
python3 ralph/ralph_loop.py --provider claude

# Codex
python3 ralph/ralph_loop.py --provider codex
```

### Run Loop with YOLO Mode

```bash
# Auto-approve all actions (provider-specific implementation)
python3 ralph/ralph_loop.py --provider qwen --yolo
python3 ralph/ralph_loop.py --provider claude --yolo
python3 ralph/ralph_loop.py --provider codex --yolo
```

### Run Loop (Dry Run)

```bash
python3 ralph/ralph_loop.py --provider claude --dry-run
```

### Run Loop with Auto-Push

```bash
python3 ralph/ralph_loop.py --provider qwen --auto-push
```

## State Model

### Status Values

- `todo` - Not started, ready to be selected
- `in_progress` - Currently being executed
- `ready_for_validation` - Implementation complete, awaiting validation
- `done` - Validation passed, complete
- `blocked` - Cannot proceed (requires `blockedReason`)
- `deferred` - Postponed to future
- `cancelled` - Will not be implemented

### State Transitions

```
todo -> in_progress -> ready_for_validation -> done
  |                                              ^
  v                                              |
blocked ---------------------------------------->
  |
  v
deferred/cancelled
```

## Selection Logic

Items are selected based on:
1. Status is `todo`
2. All dependencies are `done`
3. Lowest priority number (P0 < P1 < P2 < P3)
4. Lowest order value (tiebreaker)

## Validation Rules

### Structural (JSON Schema)
- Required fields present
- Correct types
- Valid enums
- ID format (kebab-case)

### Semantic (Python Validator)
- Unique IDs and orders
- Valid dependency references
- Acyclic dependency graph
- At most one active item
- Dependencies satisfied for active items
- Done items fully complete
- Blocked items have reason
- Valid status values

## Files

- `docs/backlog.json` - Authoritative execution state
- `docs/backlog.schema.json` - JSON Schema for structural validation
- `backlog_validator.py` - Semantic validation script
- `ralph_loop.py` - Main orchestration loop
- `providers.py` - Provider abstraction layer
- `qwen_pretty_stream.py` - Rich progress renderer for Qwen
- `simple_stream_renderer.py` - Best-effort renderer for Claude Code and Codex
- `ralph-loop.sh` - Legacy shell-based loop (deprecated)

## Dependencies

```bash
pip install jsonschema
```

## Lock File

The loop uses `.ralph-loop.lock` in the repo root to prevent concurrent execution. Stale locks (>1 hour) are automatically removed.

## Git Safety

- Requires clean working tree before starting
- Runs `git fetch` before loop
- Optional `--auto-push` after each completed item
- Never runs destructive git operations

## Design Principles

- **Provider-agnostic orchestration**: Backlog logic is separate from provider execution
- **Consistent operator experience**: Same CLI interface across all providers
- **Best-effort parity**: Rich progress where available, graceful degradation otherwise
- **No unnecessary configuration**: Provider differences absorbed internally
- **YOLO as a mode**: Supported uniformly across providers with provider-specific implementations

## Limitations and Best-Effort Differences

### Progress Rendering
- **Qwen**: Full structured progress with tool details, partial messages, colored output
- **Claude Code**: Stream-json events rendered with basic tool/text distinction
- **Codex**: Plain text output, line-based rendering only

### Output Structure
- **Qwen**: Rich event stream with tool_use, tool_result, partial messages
- **Claude Code**: Stream-json events (text, tool_use, tool_result, message lifecycle)
- **Codex**: Plain text only, no structured events

### YOLO Mode Implementation
- **Qwen**: `--yolo` flag
- **Claude Code**: `--dangerously-skip-permissions` flag
- **Codex**: `--dangerously-bypass-approvals-and-sandbox` flag

All providers support YOLO mode conceptually, but the implementation details differ based on each CLI's permission model.

## Future Work

- Add support for `ready_for_validation` status with manual approval
- Implement state transition validation in orchestrator
- Add progress reporting and metrics
- Support for parallel execution of independent items
- Provider-specific configuration options if needed