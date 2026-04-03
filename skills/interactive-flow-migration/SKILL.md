# interactive-flow-migration

## Purpose

Analyze a legacy Ink-based interactive flow and decide how it should be handled in the .NET terminal stack: preserve, simplify, convert to plain text, or defer.

## When to use

Use this skill when working on:

- legacy `local-jsx` commands
- TUI parity work
- REPL/TUI command migration
- approval, session, task, PTY, help, or settings flows that are UI-driven in the legacy CLI

## Inputs

- the flow or command name
- legacy command/component files under `../../Original/src/commands/**` and related components
- current `.NET` TUI and REPL files:
  - `../../ClawdNet.Terminal/Tui/**`
  - `../../ClawdNet.Terminal/Repl/**`
  - `../../ClawdNet.Terminal/Rendering/**`
- `../../docs/PARITY.md`, especially the interactive-flows section

## Expected outputs

- a recommended handling strategy:
  - preserve as interactive terminal UI
  - simplify to prompt-based flow
  - convert to plain text output
  - defer
- a risk summary
- implementation implications for code, docs, and validation
- updated `../../docs/PARITY.md` if the migration decision changed

## Workflow

1. Identify the legacy flow entrypoint and its command type.
2. Inspect the actual UI behavior:
   - what inputs it accepts
   - what states it presents
   - what side effects it triggers
   - whether it is safety-critical or convenience-only
3. Inspect the current `.NET` equivalent, if any, in the TUI or fallback REPL.
4. Decide the target handling:
   - preserve when the interaction is central, safety-critical, or high-frequency
   - simplify when the user outcome matters more than the exact UI
   - convert to plain text when interactivity adds little value
   - defer when the scope is large and the flow is not migration-critical
5. Record key implications:
   - runtime dependencies
   - TUI drawer/overlay needs
   - slash commands or keybindings
   - tests and manual checks
6. Update `../../docs/PARITY.md` if the flow classification, risk, or status changed.

## Guardrails

- Focus on user outcome, not exact Ink rendering details.
- Do not preserve a complicated UI just because it exists in legacy code.
- Do not simplify safety-critical flows without documenting the tradeoff.
- Do not assume a flow belongs in the TUI if a prompt or plain-text replacement is clearly better.
- Call out ambiguity when the legacy flow depends on hidden feature flags or internal-only behavior.

## Definition of done

- the flow has a clear target handling decision
- risks and implementation implications are documented
- parity documentation reflects the current decision
- the recommendation is actionable for implementation work
