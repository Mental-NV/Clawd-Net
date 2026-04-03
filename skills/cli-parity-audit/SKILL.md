# cli-parity-audit

## Purpose

Inspect the legacy TypeScript CLI and the current .NET CLI, then update parity tracking in `../../docs/PARITY.md` with verified facts.

## When to use

Use this skill when:

- a command, flag, alias, output format, or interactive flow is being migrated
- `../../docs/PARITY.md` needs to be created or updated
- the repo needs an accurate inventory of the legacy CLI surface
- a change may affect config, state, compatibility, or exit-code parity

## Inputs

- the command, feature, or flow being audited
- relevant legacy files under `../../Original/src/`
- relevant .NET files under this workspace
- current `../../docs/PARITY.md`

## Expected outputs

- updated parity rows in `../../docs/PARITY.md`
- corrected inventory details for the audited area
- explicit parity status, notes, and verification method
- a short list of unresolved ambiguities if the source is unclear

## Workflow

1. Read `../../docs/PARITY.md`, `../../docs/PLAN.md`, and `../../docs/ARCHITECTURE.md` first.
2. Identify the legacy source of truth for the area:
   - `../../Original/src/entrypoints/cli.tsx`
   - `../../Original/src/main.tsx`
   - `../../Original/src/commands.ts`
   - `../../Original/src/commands/**`
   - `../../Original/src/utils/envUtils.ts`
   - `../../Original/src/utils/sessionStorage.ts`
3. Inspect the current .NET implementation for the same area:
   - `../../ClawdNet.App/AppHost.cs`
   - `../../ClawdNet.Core/Commands/**`
   - `../../ClawdNet.Core/Services/CommandDispatcher.cs`
   - `../../ClawdNet.Terminal/**`
   - `../../ClawdNet.Runtime/**`
4. Compare:
   - command names and aliases
   - flags and positional arguments
   - interactive entry paths
   - config and state layout
   - output formats
   - exit/error behavior
5. Update `../../docs/PARITY.md` with facts from code, not assumptions.
6. Set parity status conservatively:
   - `Implemented` only when behavior exists
   - `Verified` only when tests or smoke checks have actually been run
   - `Changed` when behavior differs materially
7. Add or refine verification guidance for the audited area.

## Guardrails

- Do not infer legacy behavior from old docs if the code says something else.
- Do not mark parity as complete because a feature is "close enough."
- Do not collapse different behaviors into one parity row when the risk is different.
- Do not put approved deviations in `Intentional Deviations` unless they were explicitly accepted.
- Treat config/state compatibility as part of parity, not as a separate concern.

## Definition of done

- the audited area in `../../docs/PARITY.md` matches the current codebase
- statuses and notes are evidence-based
- verification steps are specific enough for another agent to run
- any unresolved ambiguity is called out explicitly
