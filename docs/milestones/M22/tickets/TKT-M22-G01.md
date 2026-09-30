# TKT-M22-G01 — Corrective Delivery Integration Gate

GitHub issue: #150

Ticket type: checkpoint

Milestone: M22. Type: tracking/checkpoint, **NOT an executable coding ticket**.
Risk: R1 for checkpoint records only; each implementation child has its own risk.

## Purpose

Prevent a completed first resource fix from advancing the queue directly to
release hardening. Keep this gate open until P01–P06 exit evidence in
[the approved delivery plan](../../approved-delivery-plan.md) exists and the
owner accepts progression. #149 alone does not satisfy it.

## Checkpoint procedure

At each batch exit, Astra reviews the completed batch evidence and owner product
feedback, records remaining exits, then prepares the next 3–5 bounded Ready
contracts. Insert their exact order before this gate in BOTH #98 and INDEX.
Assign model/review requirements explicitly. Future units are mapped in
[remaining-delivery-map.md](../../remaining-delivery-map.md).

Within a prepared batch, each owner merge allows the next Goal to verify its
entry conditions and implement that contract without another planning session.
Current batch: [B1](../batch-01.md), #158 → #159 → #160 → #161 after preparation
#157. Stop coding here after #161. Never implement the entire tracking issue,
auto-merge, skip an unmet prerequisite or silently relax a review gate.

Required exit evidence: repeatable resource/lifecycle slice; complete supported
account evidence and equipment protections; endpoint-aware bounded caching and
responsive local commands; comparable session-aware plans for both objectives;
six-screen visual/interaction acceptance and owner usability checkpoint; Windows
tray/notification/install lifecycle. P01/P02 close their integration gate together.

Only then may #96/#97 proceed. External-history research #139 is optional and
cannot block resource correctness. Record closure evidence and owner acceptance;
never close this issue solely because a planning document has been written.
