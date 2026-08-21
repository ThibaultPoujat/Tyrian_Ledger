# TKT-M0-SETUP-01 - Establish VERIFY register and workflow enforcement

## Milestone
M0

## Objective
Establish a project-level VERIFY register and integrate it into the permanent
Qwen ticket-development workflow, so material unresolved assumptions or
external verification questions are not lost inside an individual AI
conversation.

Documentation and workflow maintenance only. No application code, no
architecture changes, no new ADR.

## Dependencies
TKT-M0-01 (source of the imported VERIFY items)

## Changes made
- Created `docs/verification/VERIFY-REGISTER.md`: project-level index with
  stable VERIFY IDs, statuses, owner ticket, dates, and rules. Imported the
  three unresolved VERIFY items from TKT-M0-01 as VERIFY-001, VERIFY-002, and
  VERIFY-003 (all OPEN, none resolved).
- Updated `config/AGENTS.md`: Qwen must review the register before starting
  any ticket; new "Always" rule to maintain the register (add/update items,
  mark RESOLVED only with sufficient ticket evidence).
- Updated `docs/context/permanent-context.md`: new "VERIFY register" section
  establishing the register as the authoritative project-level index and the
  per-ticket obligations.
- Updated `docs/workflow/ai-development-workflow.md`: register added to the
  context hierarchy; ticket loop restructured into Before/During/Before
  completion phases with VERIFY steps; new "VERIFY register requirements"
  section mandated in every generated ticket prompt; new "Verification
  register review" PR checklist; delivery gate requires a current register.
- Created `docs/prompts/TKT-M0-SETUP-01-prompt.md`: first ticket prompt
  generated under the new requirement, including the VERIFY register
  requirements section.
- Registered this ticket in `docs/milestones/INDEX.md` (M0 line) and
  `MANIFEST.md`.

## Specification references
- `docs/workflow/ai-development-workflow.md`
- `docs/context/permanent-context.md`
- `docs/context/qwen-git-context.md`
- `docs/tickets/TKT-M0-01.md` (source of imported VERIFY items)
- `docs/milestones/M0.md` (completion rule already carries VERIFY items
  forward)

## Acceptance criteria
- [x] A project-level register exists at `docs/verification/VERIFY-REGISTER.md` with stable IDs, statuses, owner ticket, and dates.
- [x] The three unresolved TKT-M0-01 VERIFY items are imported as VERIFY-001, VERIFY-002, and VERIFY-003, all OPEN, with no invented or prematurely resolved evidence.
- [x] `config/AGENTS.md` requires Qwen to review the register before starting a ticket and to maintain it during the ticket.
- [x] `permanent-context.md` designates the register as the authoritative project-level index of unresolved verification items.
- [x] The workflow ticket lifecycle includes explicit Before implementation, During implementation, and Before completion VERIFY steps.
- [x] The prompt-generation workflow requires every future ticket prompt to carry the VERIFY register requirements; existing prompts are untouched.
- [x] The PR review workflow includes the five-item verification-register checklist.
- [x] The register/ticket distinction is documented in at least the register, permanent context, and workflow: register is an index, ticket files hold detailed evidence and resolution.
- [x] No application code, architecture, or specification documents were modified; no ADR was created.

## Validation performed
- `git status` reviewed: only intended documentation/workflow files changed.
- Complete diff reviewed for scope, duplication, and Markdown formatting.
- Cross-document consistency checked across `config/AGENTS.md`, `permanent-context.md`, `ai-development-workflow.md`, `qwen-git-context.md`, and the register (same path, same resolution-evidence rule, same index-versus-evidence distinction).
- Confirmed all imported items remain OPEN; no VERIFY item was marked RESOLVED.
- Confirmed no application code, tests, build configuration, or specification documents changed.

## VERIFY implications
- Added: VERIFY-001, VERIFY-002, VERIFY-003 (all OPEN), imported from
  TKT-M0-01 with no status change.
- No existing register items were modified or resolved (no prior register
  existed; numbering starts at VERIFY-001).
- No new unresolved items introduced by this ticket: the change is
  documentation-only.
- Follow-up: the three OPEN items remain carried forward by the M0
  completion rule.

## Non-goals
- No application code or application LLM.
- No changes to project architecture or specifications.
- No ADR for introducing the register.
- No retroactive updates to existing ticket prompts (completed tickets are
  historical records; a reopened ticket must have its prompt updated before
  execution).
- No VERIFY database, API, or scripts.

## Agent prompt
See the dedicated prompt file: `docs/prompts/TKT-M0-SETUP-01-prompt.md`.
