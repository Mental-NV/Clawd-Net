# dotnet-cli-validation

## Purpose

Run the required validation flow for the .NET CLI, classify failures clearly, and report only factual results.

## When to use

Use this skill after changing:

- `.cs` code
- command handlers
- runtime services
- TUI / REPL behavior
- provider, plugin, MCP, LSP, PTY, task, or tool infrastructure

Use a reduced version for docs-only changes.

## Inputs

- the changed files
- the expected impact area
- the solution path: `../../ClawdNet.slnx`
- any command-specific smoke tests relevant to the change

## Expected outputs

- ordered validation commands
- pass/fail status for each command
- a clear blocker summary if something fails
- no claim of success when validation was skipped or blocked

## Workflow

1. Decide whether the change is:
   - docs-only
   - code change without command-surface impact
   - command/runtime change
   - interactive-flow change
2. Run validations sequentially, never in parallel:
   - `dotnet build ./ClawdNet.slnx`
   - `dotnet test ./ClawdNet.slnx`
3. If command surface or runtime behavior changed, run relevant smoke tests, for example:
   - `dotnet run --project ./ClawdNet.App -- --version`
   - `dotnet run --project ./ClawdNet.App -- provider list`
   - `dotnet run --project ./ClawdNet.App -- ask --json "hello"`
   - other command-specific checks from `../../README.md` or `../../docs/PARITY.md`
4. If an interactive flow changed, add the required manual checks instead of pretending they were covered automatically.
5. Classify any failure clearly:
   - build failure
   - test failure
   - runtime failure
   - configuration failure
   - validation blocked / not run
6. Report results exactly as they happened.

## Guardrails

- Never run build and test in parallel.
- Treat failing validation commands as blockers.
- Do not say "done" if required validation did not run.
- For docs-only work, say explicitly that build/test were not run.
- Update docs only with factual validation outcomes, not expected ones.

## Definition of done

- required validation commands completed in order
- failures, if any, are reported clearly and not hidden
- successful completion is claimed only when the required validations passed
- manual checks are identified when automation is insufficient
