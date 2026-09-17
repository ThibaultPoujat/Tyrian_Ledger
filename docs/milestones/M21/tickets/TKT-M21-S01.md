# TKT-M21-S01 - Make CURRENT.md Live State Self-Healing from GitHub

GitHub issue: #129

## Milestone

M21 - Signals and Crafting Intelligence

## Goal

Stop requiring the owner to repair `CURRENT.md` after manual merges. GitHub issue/PR/milestone state is authoritative for live delivery state; `CURRENT.md` keeps durable context plus a small generated live-state block.

## Functional outcome

After an implementation PR merges to `develop`, a deterministic workflow can advance the live handoff in `CURRENT.md` without owner editing or AI/model quota. If that workflow is stale or failed, the next coding-agent session detects and repairs the generated block before relying on it.

## Requirements

- Split `CURRENT.md` into durable narrative and a clearly delimited generated live-state block.
- Add a deterministic GitHub Actions workflow/script that updates only the generated block after merges to `develop`.
- Generated fields include at minimum: latest completed implementation ticket/PR, active milestone, next valid ticket, and currently active explicit Sol gates relevant to handoff.
- No LLM/Codex call is used by the workflow.
- Session-start fallback compares GitHub live state with the generated block and repairs stale live state before using it.
- GitHub live state wins when it disagrees with the generated block.
- Workflow/script fails safely and cannot rewrite durable narrative.

## Acceptance criteria

- [ ] A merged implementation PR can advance the generated live-state block without owner editing.
- [ ] Deterministic tests/fixtures cover normal merge progression and stale-block repair.
- [ ] Durable `CURRENT.md` prose cannot be overwritten by the generated-state writer.
- [ ] `AGENTS.md` and workflow docs explain GitHub-authoritative fallback behavior.
- [ ] No AI/model quota is consumed by the state update.

## Dependencies

#128.

## Recommended Codex configuration

Risk class: **R2 (workflow/state maintenance)**.

Review path: **NORMAL** — Terra High by default because this touches delivery state/workflows, plus the required independent same-run review/check and CI.

## Validation

- Unit/script tests for generated-block parsing and replacement.
- Fixture test for merged-ticket progression.
- Fixture test proving durable prose is untouched.
- Workflow syntax/contract checks.
- Repository diff check for unintended `CURRENT.md` rewriting.

## Non-goals

- AI-generated handoff text.
- Rewriting historical ticket/PR state.
- Product UI behavior.
