# ADR-002 - Local-First Deployment

## Status

Accepted - reaffirmed and clarified by ADR-010 on 2026-09-04.

## Decision

V1 runs on the user's computer with a loopback ASP.NET Core host and SQLite. No
cloud server is required. The React browser client talks to the local host and
does not access Guild Wars 2 credentials directly.

Supported desktop operating systems may use their native secret-store adapter;
the architecture is not tied to one owner's current machine.

## Consequences

The privacy and security model is local-first; the user owns backup/recovery of
local data. A future multi-user/cloud deployment or non-loopback network
exposure requires a separate architecture/security decision.

M10 temporarily superseded this runtime for a public static Pages experiment.
ADR-010 ends that experiment for the active product direction and restores this
local-first principle.
