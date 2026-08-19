# ADR-004 - Single GW2 API Gateway

## Status
Accepted

## Decision
All Guild Wars 2 API requests go through one typed gateway abstraction with caching, batching, deduplication, scheduling, retries, and response validation.

## Rationale
Prevents request multiplication, centralizes rate-limit policy, and isolates external schema changes.
