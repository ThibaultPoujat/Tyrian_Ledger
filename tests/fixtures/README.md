# Test fixtures

Fixtures are deterministic, synthetic, small enough to inspect, and committed
with the test that consumes them. They must never contain real Guild Wars 2
account, character, or credential data.

## Layout

- `gw2/<api-area>/<resource>.json` — successful GW2 response payloads, using
  lower-case endpoint-oriented directory and file names; for example,
  `gw2/commerce/prices.json`.
- `errors/<http-status>.json` — synthetic error scenarios grouped by HTTP
  response status; for example, `errors/429.json`.
- `dashboard/<feature>.json` — deterministic local dashboard response fragments
  used to compare UI-facing scenario output; for example,
  `dashboard/opportunity-detail.json`.

## Content rules

- Use fictional identifiers and sanitized values only.
- Represent currency as integer copper whenever a fixture contains money.
- Keep each fixture focused on one endpoint or error scenario and include only
  fields needed by its consuming tests.
- Add or update the consuming test in the same change as any fixture update.

Integration tests load fixtures from this directory through
`Gw2Tp.Testing.JsonFixtureLoader`; fixture paths must be relative JSON paths and
cannot escape this fixture root.
