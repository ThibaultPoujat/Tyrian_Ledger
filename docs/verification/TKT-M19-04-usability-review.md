# TKT-M19-04 Timed Primary-Workflow Review

Review date: 2026-09-09

Evidence source: deterministic mocked Playwright recommendation response
External requests: none; no ArenaNet key or request was used

## Scenario and result

The review starts when the local page is opened at desktop width. The first
card is an attention-ranked current buy order named `Overpriced bid`. Within
the two-minute limit, the reviewer can identify all decision-critical fields
without opening the supporting evidence:

| Required fact | Visible value |
|---|---|
| Top action | `CANCEL BID` |
| Quantity | 3 |
| Current price | 0g 1s 25c |
| Maximum bid | 0g 1s 10c |
| Confidence | Strong |
| Binding constraint | Item Exposure |
| Primary reason | The current bid is above the maximum allowed bid. |

The same journey uses the keyboard to expand depth, retained-history, score,
constraint, and complete-reason evidence, then reveals the sixth new
opportunity with the explicit show-more control. It verifies that only five new
opportunities are initially visible while the current attention action remains
first. The journey also runs the WCAG 2.2 AA axe rules and a 375-pixel overflow
check.

## Automated proof

`tests/Gw2Tp.Web.E2E/tests/transition-shell.spec.ts` records the start time and
asserts completion in less than 120 seconds. The suite runs this mocked path in
Chromium, Firefox, and WebKit, asserts exactly one local recommendation GET,
and rejects external or `api.guildwars2.com` requests.
