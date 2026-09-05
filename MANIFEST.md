# Repository Documentation Map

## Active entry points

- `README.md` — product overview and source-of-truth read order.
- `CURRENT.md` — current transition state, active milestone, and next ticket.
- `AGENTS.md` — coding-agent execution, boundary, model, review, and reporting rules.
- `docs/specs/project-spec.md` — canonical product specification.
- `docs/specs/trading-rules.md` — canonical financial/recommendation behavior.
- `docs/architecture/architecture.md` — target local-first technical architecture.
- `docs/architecture/data-model.md` — target durable/derived local data model.
- `docs/milestones/INDEX.md` — M0-M11 history plus active M12-M22 roadmap.
- `docs/verification/VERIFY-REGISTER.md` — external-contract uncertainty register.
- `docs/development/validation.md` — exact clean-checkout build, test, retired-runtime audit, and secret-scan procedure.
- `docs/development/local-runtime.md` — concrete development/publish commands and local host security configuration.

## Active ADRs

- `docs/adr/ADR-001-stack.md` — .NET/ASP.NET + React + SQLite stack.
- `docs/adr/ADR-002-local-first.md` — local loopback + SQLite deployment, reaffirmed by ADR-010.
- `docs/adr/ADR-003-no-llm-runtime.md` — no application-runtime LLM.
- `docs/adr/ADR-004-gw2-api-gateway.md` — typed ArenaNet gateway boundary.
- `docs/adr/ADR-005-money-copper.md` — integer-copper money.
- `docs/adr/ADR-006-secrets.md` — OS-backed local secret storage / browser secret boundary.
- `docs/adr/ADR-007-read-only-boundary.md` — no gameplay/TP mutation automation.
- `docs/adr/ADR-010-personal-local-first-pivot.md` — active M12 personal-assistant pivot.

## Superseded/historical architecture

- `docs/adr/ADR-008-static-github-pages-hosting.md` — superseded M10 Pages runtime.
- `docs/adr/ADR-009-external-pages-snapshot-scheduler.md` — superseded M11 scheduler.

TKT-M12-02 removed the active static delivery source, publishable snapshot
contract, and operational guides. M0-M11 milestone/context/ticket files remain
the historical record of that retired runtime; M12-M22 are the active roadmap.
Do not treat historical files as active architecture authority.

## Workflow and review

- `docs/development/validation.md`
- `docs/workflow/ai-development-workflow.md`
- `docs/workflow/codex-collaboration.md`
- `docs/workflow/model-effort-guide.md`
- `docs/workflow/functional-brief-template.md`
- `docs/workflow/delivery-protocol.md`
- `.codex/skills/tyrian-pr-review/SKILL.md`
- `.codex/skills/tyrian-pr-review/references/checklist.md`
- `.github/pull_request_template.md`

## Supporting active references

- `docs/security/security.md`
- `docs/security/security-review.md` — historical security-review evidence; refresh when relevant rather than treating old sign-off as a new audit.
- `docs/testing/testing-strategy.md`
- `docs/ux/ux.md`
- `docs/rate-limiting/rate-limit-policy.md`
- `docs/architecture/gw2-endpoint-matrix.md`
- `docs/specs/verified-external-notes.md`
