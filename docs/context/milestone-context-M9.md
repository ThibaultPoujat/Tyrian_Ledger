# Milestone Context - M9 Beginner Fast-Flip MVP

Read this file with [M9](../milestones/M9.md), the assigned M9 ticket, the
permanent context, and the VERIFY register. Do not load unrelated milestone
plans unless the ticket needs an explicit compatibility detail.

## User and outcome

The intended player is new to Guild Wars 2 Trading Post trading. They know
their available capital and comfort with risk, but do not know items, flips,
liquidity, or what moves quickly. The outcome is a short, manual in-game action
plan, not education, investment management, or trade automation.

The primary flow is Settings -> player-triggered Scan -> up to five ranked
Recommendations -> player manually creates the in-game orders.

## M9 decisions

- Support only fast flips. Target timing is guidance, never a fill or profit
  guarantee.
- Use three risk profiles: Cautious, Balanced, Adventurous.
- Apply profile caps of 10%, 25%, and 50% of capital; minimum modeled ROI of
  5%, 8%, and 12%; and modeled profit floors of 10 silver, 25 silver, and
  50 silver respectively.
- Calculate in integer copper with built-in 5% listing and 10% exchange fees.
  Fee rounding/minimum behavior remains VERIFY-013 until confirmed.
- Discover candidates across the public market with current data only. Do not
  use watchlists, automated refreshes, historical snapshots, or personal data.
- Fetch cheap whole-market data first, then bounded detailed listings and item
  metadata for finalists through the typed gateway.
- Publish no partial result set. Cancellation, rate limiting, and failures
  leave no recommendations and offer retry.
- Show two groups: Can act now and Place an order and wait. Both prescribe
  buying one copper above the current best buyer and selling one copper below
  the current cheapest seller.
- Quantity is the largest whole quantity within the selected risk cap and one
  normal item stack.
- The user chooses one independent recommendation at a time; no portfolio or
  post-trade tracking is provided.
- Navigation is Recommendations and Settings. First visit uses guided,
  plain-language setup. Cards provide numbered manual steps but no copy button.
- A normal item stack is a fixed M9 product cap of 250. The public
  `/v2/items` response supplies item identity and display name but not a
  per-item stack-limit field; this cap is not represented as ArenaNet data.

## Architectural invariants

- Remain local and read-only. Never automate Guild Wars 2 or Trading Post
  actions and never introduce credentials.
- All Guild Wars 2 access uses typed gateway clients; feature code never makes
  ArenaNet URLs.
- External DTOs and domain types stay separate.
- Financial logic is deterministic and uses integer copper only.
- M9 retains no historical market data, recommendations, partial scans, account
  data, crafting data, or personal trade history.
- Verify external API schemas, batching, and fee rounding before relying on
  them. Record unresolved facts in the VERIFY register.

## Ticket handoff

Work tickets in numerical order after their predecessor is merged. Each ticket
defines its acceptance criteria, required tests, non-goals, and references.
Do not implement a later ticket's UI, scan orchestration, or recommendations
while completing an earlier foundation ticket.
