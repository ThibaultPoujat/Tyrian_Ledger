# UI/UX Specification

## Visual direction

Modern analytical dashboard with restrained Guild Wars 2-inspired atmosphere without copying ArenaNet assets.

Use:

- dark/charcoal base;
- subtle metallic/parchment surfaces;
- restrained cyan/teal accents;
- clear gold/copper emphasis only for economic values;
- high information density with generous grouping;
- simple icons and CSS shapes instead of copied game assets.

## Main screens

### Dashboard

- opportunity ranking;
- user/session constraints;
- data freshness;
- last refresh;
- warning banner when data is stale/incomplete.

### Opportunity detail

Show:

- exact scenario;
- acquisition assumption;
- exit assumption;
- fees;
- modeled profit;
- capital required;
- ROI;
- order-book impact;
- liquidity proxy;
- risk/confidence factors;
- freshness.

### Crafting detail

Show the path as a tree or expandable graph:

output -> recipe -> intermediate ingredients -> source cost.

Highlight where owned materials are used and show their opportunity cost.

### Account/settings

- API key connection state;
- permission status;
- data refresh controls;
- stored profile settings;
- clear local account data.

## Interaction rules

- Do not hide critical assumptions behind tooltips only.
- Use explicit labels: `Modeled profit`, not `Profit`.
- Use `Data age` everywhere live market data appears.
- Never show a green “guaranteed” state.
- Use filter controls that preserve current selections when possible.
- Support keyboard navigation.

## Desktop-first

Mobile support is deferred. The desktop layout should be responsive enough to avoid unusable overflow at narrower desktop window widths.
