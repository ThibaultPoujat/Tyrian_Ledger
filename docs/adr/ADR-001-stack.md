# ADR-001 - Application Stack

## Status
Accepted

## Decision
Use ASP.NET Core/.NET 10 for the backend, React + TypeScript for the frontend, SQLite for local persistence, xUnit for .NET tests, and Playwright for browser-level tests.

## Rationale
Clear separation of deterministic business logic from browser UI; strong .NET tooling; mature SQLite support; good fit for local desktop use and future hosted deployment.

## Alternative
Blazor Web App remains a valid alternative if keeping one language becomes more important than the current frontend separation.
