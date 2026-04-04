# Ralph Loop for Clawd-Net

This folder contains the refactored Ralph Loop implementation using validated JSON-driven orchestration.

## Overview

The Ralph Loop orchestrates execution of backlog items defined in `docs/backlog.json`. It replaces the legacy Markdown-driven approach with a robust JSON-based model featuring structural and semantic validation.

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

Python-based loop replacing `ralph-loop.sh`:
- Deterministic item selection (priority, then order)
- Atomic backlog updates
- Lock-based concurrency control
- Git safety checks
- Validation command execution
- State transitions with timestamps

## Usage

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

### Run Loop (Dry Run)

```bash
python3 ralph/ralph_loop.py --dry-run
```

### Run Loop (Live)

```bash
python3 ralph/ralph_loop.py
```

### Run Loop with Auto-Push

```bash
python3 ralph/ralph_loop.py --auto-push
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
- `qwen_pretty_stream.py` - Stream-json renderer (unchanged)
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

## Future Work

- Integrate with actual clawdnet invocation (currently placeholder)
- Add support for `ready_for_validation` status with manual approval
- Implement state transition validation in orchestrator
- Add progress reporting and metrics
- Support for parallel execution of independent items