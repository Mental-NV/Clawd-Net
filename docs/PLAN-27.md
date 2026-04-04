# PLAN-27: Edit Workflow Parity Decision v1

## Objective

Decide and document whether the current `.NET` patch-first edit review workflow is the accepted migration replacement for the legacy TypeScript editing and review flows, or identify gaps that must be closed.

**Scope:**
1. Compare legacy edit/review expectations with the current patch-batch approval model
2. Identify any critical approval/review UX gaps that block migration acceptance
3. Decide whether the patch-first model is the accepted replacement
4. Document the decision in PARITY.md (verified or intentional deviation)
5. Update ARCHITECTURE.md if the edit workflow decision changes cross-cutting assumptions

**Non-goals (explicitly out of scope):**
- Implementing new edit review UI beyond what is needed for the decision
- Changing the `apply_patch` tool semantics
- Adding per-file approval granularity
- Implementing `/review`, `/init`, `/commit`, or other workflow commands (separate milestone)

## Assumptions

1. The legacy CLI uses Ink-based interactive dialogs for edit review, not a structured patch workflow
2. The `.NET` `apply_patch` tool with batch preview + approve/deny is materially different from the legacy approach
3. This milestone is about the **decision**, not about implementing a completely new review UI
4. `file_write` exists for compatibility but is not the preferred model-facing edit tool
5. The decision should be driven by user outcomes, not screen-for-screen parity

## Files and Subsystems Likely to Change

| File | Change Type | Reason |
|------|------------|--------|
| `docs/PARITY.md` | Update | Move edit workflow row to verified or intentional deviations |
| `docs/ARCHITECTURE.md` | Update | Record edit workflow decision if it affects cross-cutting assumptions |
| `docs/PLAN.md` | Update | Mark milestone complete |
| `docs/PLAN-27.md` | Create | This execution plan |

## Step-by-Step Implementation Plan

### Step 1: Analyze Legacy Edit Workflow Expectations
- Inspect `Original/src/commands.ts` and related command files for edit/review-oriented commands
- Identify the legacy edit/review UX shape (what commands exist, how they work)
- Determine what user outcomes the legacy flows serve

### Step 2: Compare with Current .NET Edit Workflow
- Document the current `apply_patch` + batch approval model
- Document `file_write` as the compatibility path
- Identify gaps: what can the legacy do that `.NET` cannot?
- Identify advantages: what can `.NET` do that legacy cannot?

### Step 3: Make the Parity Decision
- Evaluate whether the current `.NET` edit workflow serves the same user outcomes
- Decide if gaps are blocking or deferrable
- Decide if the patch-first model is the accepted replacement

### Step 4: Document the Decision
- Update `docs/PARITY.md`: move edit workflow row to verified or intentional deviations
- Update `docs/ARCHITECTURE.md`: record the edit workflow decision if it changes cross-cutting assumptions
- Update `docs/PLAN.md`: mark milestone as [v]

## Validation Plan

### Automated
```bash
dotnet build ./ClawdNet.slnx
dotnet test ./ClawdNet.slnx
```

### Manual verification
- Review PARITY.md edit workflow row status
- Review ARCHITECTURE.md edit workflow section
- Verify documentation is internally consistent

### Smoke test (existing edit workflow)
```bash
# Verify apply_patch tool still works
dotnet run --project ClawdNet.App -- ask --permission-mode bypass-permissions "Create a file test.txt with content 'hello'"
```

## Rollback / Risk Notes

- **Risk:** Conflating product improvement with strict parity. **Mitigation:** Focus on user outcomes, not screen-for-screen matching
- **Risk:** Over-expanding scope beyond the decision milestone. **Mitigation:** Keep changes bounded to documentation updates only
- **Risk:** Decision may later prove wrong. **Mitigation:** Document rationale clearly so it can be revisited

## Exit Criteria

- [x] Legacy edit workflow expectations analyzed
- [x] Current .NET edit workflow compared against legacy
- [x] Parity decision made and documented
- [x] PARITY.md edit workflow row updated (verified or intentional deviation)
- [x] ARCHITECTURE.md updated if cross-cutting assumptions changed
- [x] PLAN.md milestone marked complete
- [x] dotnet build passes
- [x] dotnet test passes
- [x] Changes committed

## Implementation Summary

### What was done:

#### Step 1: Legacy Edit Workflow Analysis
Inspected the legacy TypeScript source for edit/review-oriented commands:

- Legacy has `apply_patch`-style edit flows model-triggered through tool use
- Legacy edit review is Ink-based interactive UI for reviewing model-proposed changes
- Legacy has `/review` as a built-in prompt-style slash command (converts to model-facing prompt flow for code review)
- Legacy has permission-oriented flows that gate write operations with interactive approval dialogs
- Legacy does not have a structured patch-first workflow; edits are more conversational and tool-specific

Key finding: the legacy edit experience is **not** a structured patch workflow. It is permission-gated tool execution with Ink-based approval dialogs. The model proposes changes through tool calls, and the user approves or denies them through interactive modals.

#### Step 2: Comparison with Current .NET Edit Workflow

| Aspect | Legacy TypeScript | Current .NET |
|--------|-------------------|--------------|
| Edit proposal | Model tool calls | Model tool calls (`apply_patch`, `file_write`) |
| Review UX | Ink approval modals | TUI overlay with diff preview |
| Approval granularity | Per-tool-call | Per-tool-call (batch level) |
| Diff preview | Limited/implicit | Explicit unified diff |
| Rollback on failure | Implicit | Explicit rollback on batch failure |
| Patch format | Not structured | Structured edits with hunks |
| `file_write` | Exists | Exists (compatibility, no review) |

Key findings:
1. `.NET` has **stronger** safety guarantees than legacy (explicit diff preview, rollback)
2. `.NET` batch approval is coarser than legacy per-tool approval, but both are per-tool-call
3. `.NET` `file_write` bypasses review (same as legacy - it is a blunt tool)
4. `.NET` lacks `/review` command (separate workflow command, not edit workflow proper)
5. The fundamental user outcome (review and approve/deny model-proposed edits) is served

#### Step 3: Parity Decision

**Decision: The `.NET` patch-first edit workflow is the accepted migration replacement.**

Rationale:
1. The core user outcome is preserved: users can review and approve/deny model-proposed edits
2. `.NET` has **better** safety properties: explicit diff preview, structured patch format, atomic batch application with rollback
3. The batch approval model (approve all or reject all per tool call) is a reasonable design choice, not a parity gap
4. `file_write` exists as a compatibility path for blunt writes, matching the legacy position
5. The missing `/review` command is a workflow command, not part of the edit approval workflow itself (tracked separately in Workflow Command Recovery v1)

Intentional deviations accepted:
- `.NET` uses structured patch format (`apply_patch` with hunks) rather than implicit tool-call-based edits
- `.NET` batch approval is all-or-nothing per tool call rather than potentially finer-grained legacy flows
- `.NET` has explicit diff preview overlay rather than Ink modal dialogs

#### Step 4: Documentation Updates
- Updated PARITY.md: moved "Edit workflow | Reviewable patch flow" row to Verified status with rationale
- Updated PARITY.md: added intentional deviation entry for patch-first vs implicit edit model
- Updated ARCHITECTURE.md: strengthened edit workflow section to document the decision formally
- Updated PLAN.md: marked Edit Workflow Parity Decision v1 as [v]

### What changed:
- `docs/PARITY.md` - edit workflow row moved to Verified, new intentional deviation entry
- `docs/ARCHITECTURE.md` - edit workflow section updated with formal decision
- `docs/PLAN.md` - milestone marked complete

### Remaining follow-ups:
- `/review`, `/init`, `/commit`, `/commit-push-pr`, `/branch`, `/diff` commands remain tracked under Workflow Command Recovery v1
- Per-file approval granularity could be a future enhancement (not a parity blocker)
- `file_write` review flow gap (no preview for blunt writes) could be addressed if needed
