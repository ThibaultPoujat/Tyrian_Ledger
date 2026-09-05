# Codex Model and Reasoning Effort Guide

Model names and availability change faster than project architecture. This file
sets a **risk-based selection policy**, not a dependency on one permanent model.
Refresh the named examples when OpenAI's available Codex models materially
change; do not rewrite ticket contracts merely because a new model appears.

## Principles

1. Independent review matters more than using the same strongest model twice.
2. Increase model capability/effort with consequence of error, not ticket size.
3. Tests and evidence do not become optional when a stronger model is used.
4. Use the cheapest configuration that reliably meets the risk class, then
   escalate when evidence justifies it.
5. A fresh reviewer should not inherit the implementation conversation.

## Current reference configurations

As of the M12 pivot planning date (2026-09-04):

- **GPT-5.6 Terra** is the normal cost/capability choice.
- **GPT-5.6 Sol** is the preferred flagship choice for complex/high-stakes work.
- **GPT-6 Astra**, when available in the owner's Codex environment, may replace
  Sol as the strongest review/complex-work option.

High and XHigh are the normal upper efforts. Max is an escalation tool, not a
default.

## Risk classes

### R0 - Mechanical/low-consequence maintenance

Examples: formatting, link repair, straightforward documentation, moving a file
without semantic change. Documentation that establishes architecture, security,
network exposure, financial policy, or another authority boundary is not R0.

- Implementation: Terra Medium.
- Review: Terra Medium/High when a review is still required.

### R1 - Normal product implementation

Examples: ordinary endpoint/UI wiring, non-financial presentation work, additive
refactors with good tests.

- Implementation: Terra High.
- Review: fresh Terra High.

### R2 - Complex cross-layer or stateful work

Examples: non-secret public gateway additions, sync orchestration, background
scheduler, complex order-book integration, backup/restore, and significant
UI/API contracts that do not establish an R3 authority boundary.

- Implementation: Terra High or Sol High.
- Review: fresh Sol High/XHigh according to consequence.

### R3 - Financial/data/security/architecture authority

Examples:

- fee policy/rounding;
- FIFO matching and realized P&L;
- destructive or non-trivial database migrations;
- secret handling, private-data boundaries, Host/origin/CSRF controls, or
  network exposure;
- architecture contracts and ADRs that establish or materially change a
  security, network, persistence, or financial authority boundary;
- historical statistical formulas;
- opportunity scoring;
- bankroll/position sizing;
- recommendation action orchestration;
- personal-performance weighting;
- crafting opportunity-cost economics.

- Implementation: preferably Sol High (Terra High is acceptable when the owner
  wants to conserve quota and tests are strong).
- Review: **fresh flagship XHigh**. Use Sol XHigh by default; Astra XHigh may be
  used when available.
- Consider a second targeted review only when the first review identifies a
  material unresolved issue or the ticket changes an especially sensitive
  invariant.

## Max effort

Use Max only when one of these is true:

- XHigh review leaves a real unresolved correctness ambiguity;
- a subtle bug survives normal implementation + review + targeted tests;
- the task requires unusually difficult cross-domain reasoning;
- the owner explicitly wants the strongest possible audit regardless of cost.

Do not use Max simply because a ticket is financially important; XHigh plus
independent tests/review is normally the better process.

## Ticket annotation

Every active M12+ ticket contains a `Recommended Codex configuration` section
with its risk class and suggested implementation/review choice. Treat that as a
starting point. If model availability differs, preserve the **risk class and
independence requirement**.

When a ticket spans classes, use the highest applicable class. Documentation-
only scope does not lower an architecture/security authority change below R3.
