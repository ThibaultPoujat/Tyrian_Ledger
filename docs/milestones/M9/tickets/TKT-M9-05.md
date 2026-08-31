# TKT-M9-05 - Beginner Recommendations and Settings UX

## Goal

Deliver the accessible beginner user experience for configuration, scanning,
grouped recommendations, and manual Guild Wars 2 Trading Post instructions.

## Dependencies

- TKT-M9-04.

## References

- [M9 milestone plan](../../M9.md)
- [M9 milestone context](../../../context/milestone-context-M9.md)
- [VERIFY register](../../../verification/VERIFY-REGISTER.md)
- Existing browser accessibility, design-system, API-client, and Playwright
  test conventions.

## Acceptance criteria

- Navigation contains only Recommendations and Settings. Retired historical,
  account, crafting, and personal-history pages are absent.
- First visit presents brief guided setup in beginner language. It explains
  capital, risk, and the fact that every Trading Post action remains manual.
- Settings accepts initial capital in gold, silver, and copper and one of the
  three risk profiles. Validation prevents invalid, negative, or overflow
  values and clearly explains each profile's maximum spend and thresholds.
- Recommendations has one player-triggered Scan the market action and clear
  idle, running, progress, cancellation, rate-limit, failure, empty, and
  complete states. Failed/incomplete scans show no results and offer retry.
- Complete results show at most five cards, grouped as Can act now and Place
  an order and wait. The interface makes no guarantee about fill time, sale,
  or profit.
- Each card shows item name, quantity, buy and sale prices, total spend,
  estimated fee breakdown, modeled profit, modeled ROI, scan time, route
  explanation, and numbered manual in-game steps. There is no copy button or
  execution automation.
- The UI is keyboard-operable, has visible focus, semantic labels, accessible
  errors/status announcements, sufficient contrast, responsive layout, and
  understandable empty/error content.
- Browser state stores only settings needed for the experience and does not
  store account credentials, personal history, market history, or
  recommendations as historical records.

## Required tests

- Component/integration tests for setup, capital parsing, profile selection,
  validation, scan states, grouped result rendering, fee/profit explanation,
  and stale-result removal after failure/cancellation.
- Accessibility tests for keyboard flow, focus, labels, status/error
  announcements, and contrast-sensitive components.
- Playwright end-to-end coverage from first visit through Settings, successful
  scan, card instructions, cancellation, rate-limit failure, and retry.
- Regression tests confirming retired navigation/routes are absent.

## Non-goals

- Altering gateway contracts, scan lifecycle, fee policy, quantity logic, or
  recommendation ranking from earlier M9 tickets.
- Account linking, order placement, notification, portfolio, historical, or
  post-trade features.
