# TKT-M21-S01 - Make CURRENT.md Live State Self-Healing from GitHub

GitHub issue: #129

## Milestone

M21 - Signals and Crafting Intelligence

## Goal

Stop requiring the owner to repair `CURRENT.md` after manual merges. The generated live-state block combines authoritative operational GitHub state with the repository's explicit execution-order and review-gate sources; `CURRENT.md` keeps durable context plus that small generated block.

## Functional outcome

After an implementation PR merges to `develop`, a deterministic workflow can advance the live handoff in `CURRENT.md` without owner editing or AI/model quota. If that workflow is stale or failed, the next coding-agent session detects and repairs the generated block before relying on it.

## Authority split

The generator must not infer every field from issue numbering or from one source:

- **Operational delivery state:** GitHub merged PRs, issue open/closed state, and milestone assignment/title are authoritative.
- **Execution order / next valid ticket:** issue #98 and `docs/milestones/INDEX.md` are authoritative. Numeric issue ordering is not authoritative.
- **Review gates:** `docs/workflow/model-effort-guide.md` is authoritative for the active Sol-gate list and review policy.
- The generated `CURRENT.md` block combines those sources. If a generated value disagrees with its owning authority, repair the generated value; do not silently rewrite the owning source.

## Requirements

- Split `CURRENT.md` into durable narrative and a clearly delimited generated live-state block.
- Add a deterministic GitHub Actions workflow/script that updates only the generated block after merges to `develop`.
- Generated fields include at minimum: latest completed implementation ticket/PR, active milestone, next valid ticket, and currently active explicit Sol gates relevant to handoff.
- Resolve each generated field from the authority split above rather than assuming GitHub issue number order defines roadmap order.
- No LLM/Codex call is used by the workflow.
- Session-start fallback compares the generated block with its owning authoritative sources and repairs stale live state before using it.
- Workflow/script fails safely and cannot rewrite durable narrative or silently mutate roadmap/review-policy sources.

## Acceptance criteria

- [ ] A merged implementation PR can advance the generated live-state block without owner editing.
- [ ] Deterministic tests/fixtures cover normal merge progression and stale-block repair.
- [ ] A fixture proves that lower-numbered open issues cannot override the explicit roadmap order.
- [ ] A fixture proves that the Sol-gate field comes from `docs/workflow/model-effort-guide.md`, not issue risk class or numbering.
- [ ] Durable `CURRENT.md` prose cannot be overwritten by the generated-state writer.
- [ ] `AGENTS.md` and workflow docs explain the operational-state / execution-order / review-gate authority split and fallback behavior.
- [ ] No AI/model quota is consumed by the state update.

## Dependencies

#128.

## Recommended Codex configuration

Risk class: **R2 (workflow/state maintenance)**.

Review path: **NORMAL** — Terra High by default because this touches delivery state/workflows, plus the required independent same-run review/check and CI.

## Validation

- Unit/script tests for generated-block parsing and replacement.
- Fixture test for merged-ticket progression.
- Fixture test for explicit roadmap ordering versus numeric issue ordering.
- Fixture test for model-effort-guide Sol-gate extraction.
- Fixture test proving durable prose is untouched.
- Workflow syntax/contract checks.
- Repository diff check for unintended `CURRENT.md` rewriting.

## Non-goals

- AI-generated handoff text.
- Rewriting historical ticket/PR state.
- Product UI behavior.
