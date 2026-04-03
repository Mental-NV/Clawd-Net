# Autonomous Mission Prompt

Use this prompt to drive long-running autonomous execution for `ClawdNet`.

```text
You are working on ClawdNet, a .NET 10 replatforming of the legacy TypeScript + React + Ink CLI preserved under Original/. The goal is to port the important legacy tool surface to .NET 10 with functional parity unless an intentional deviation is explicitly documented.

This is long-running autonomous migration work. Operate milestone by milestone, not feature-by-feature across the whole repo at once.
Continue executing milestone slices until every roadmap item in `docs/PLAN.md` is finished, unless the user explicitly redirects, pauses, or stops the work.

Before coding, read the authoritative documents and treat them as the operating contract:
- AGENTS.md
- docs/ARCHITECTURE.md
- docs/PLAN.md
- docs/PARITY.md

Execution loop:
1. Open docs/PLAN.md and select exactly one roadmap milestone, starting with the highest-priority unfinished item unless the user explicitly redirects you.
2. Create a milestone execution file under docs/PLAN-XX.md using the next available zero-padded number. Treat it as the working execution plan for that milestone.
3. In docs/PLAN-XX.md, write a concise executable plan:
   - objective and scope
   - assumptions and non-goals
   - files and subsystems likely to change
   - step-by-step implementation plan
   - validation plan
   - rollback or risk notes
4. Keep docs/PLAN-XX.md current as you learn. If scope, risks, or sequencing changes, update the plan instead of letting it drift.
5. Implement the milestone in small, reversible steps. Preserve existing behavior unless a deliberate deviation is recorded in docs/PARITY.md.
6. Verify sequentially, not in parallel:
   - dotnet build ./ClawdNet.slnx
   - dotnet test ./ClawdNet.slnx
   - targeted smoke tests for the changed command surface or interactive flow
7. If validation fails, fix the issues and re-run validation. Do not claim completion while any required validation is failing, skipped, or blocked.
8. Update the authoritative documents to match the new implementation state:
   - docs/PARITY.md for parity status, compatibility, and intentional deviations
   - docs/ARCHITECTURE.md for stable decisions, defaults, or assumptions
   - docs/PLAN.md for milestone status and execution order
   - README.md for supported behavior or config changes
   - AGENTS.md only if durable repo-wide operating rules changed
9. After each completed execution slice, commit the validated slice changes on the current branch with a focused commit message.
10. After the milestone is fully implemented, validated, documented, and committed, move to the next roadmap milestone in docs/PLAN.md and repeat until the roadmap is complete.

Use the repo-local skills when relevant:
- skills/cli-parity-audit/SKILL.md
- skills/migration-doc-sync/SKILL.md
- skills/dotnet-cli-validation/SKILL.md
- skills/interactive-flow-migration/SKILL.md

Working rules:
- Prefer simple, readable code and avoid unnecessary abstractions.
- Keep changes bounded to the active milestone.
- Do not speculate beyond the current milestone unless required for a safe design.
- Do not duplicate architecture content into PLAN documents.
- Do not update parity or milestone status from memory; verify from code and validation results.
- Do not leave a completed execution slice uncommitted.
- If blocked by a real ambiguity or external dependency, document the blocker in docs/PLAN-XX.md and stop rather than guessing.

Definition of done for each milestone:
- implementation is complete for the planned scope
- required validation passed
- docs are consistent with the new state
- docs/PLAN.md reflects the updated roadmap status
- docs/PLAN-XX.md records what was done, what changed, validation results, and any remaining follow-ups
- the slice is committed on the current branch
```
