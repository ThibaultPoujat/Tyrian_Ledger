# TKT-M9-07 - Preserve current scan across navigation

## Goal

Keep the current player-requested M9 scan and its completed recommendations
available while the player navigates between Recommendations and Settings in
one browser tab, without adding durable result retention.

## Dependencies

- TKT-M9-06.

## References

- [M9 milestone plan](../../M9.md)
- [M9 milestone context](../../../context/milestone-context-M9.md)
- [VERIFY register](../../../verification/VERIFY-REGISTER.md)
- Existing player scan lifecycle and beginner browser experience.

## Acceptance criteria

- Current scan state remains in browser memory across Recommendations/Settings
  navigation and Back/Forward, but starts idle after a reload or tab close.
- A player-started running scan continues its existing one-second polling
  across navigation and stops only at a terminal state or app cleanup. No
  page load or navigation starts a scan.
- A changed capital or risk profile prompts before a completed result is
  cleared. Cancelling the prompt retains the current settings and result;
  confirming saves the new settings and clears the scan session.
- A changed setting during a starting or running scan safely cancels that scan
  before saving. An unconfirmed cancellation keeps the current settings.
- The confirmation dialog is keyboard accessible, announces its purpose, and
  returns focus to Save settings when dismissed.
- Only the existing versioned capital/risk entry uses browser storage. Scan
  results, progress, terminal states, and dialog state are transient.

## Required tests

- Unit coverage for navigation persistence, polling while Settings is open,
  save/change confirmation, active-scan cancellation, and stale-response
  safety.
- Browser coverage for result persistence, confirmation keyboard behavior,
  active-scan cancellation, and WCAG 2.2 AA automated checks using intercepted
  scan responses only.

## Non-goals

- New scan endpoints, backend lifecycle behavior, caching, browser result
  persistence, history, market-policy changes, or background refresh.
