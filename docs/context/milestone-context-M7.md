# Context - M7

## Milestone
M7 - Historical market data and investment research

## Goal
Collect and use local historical market data where justified, and provide comprehensible long-term investment analysis with explicit uncertainty.

## Agent context
Load `docs/context/permanent-context.md`, the VERIFY register, one M7 ticket, and its matching M7 prompt. Read history/data-retention and investment-analysis guidance only when required.

## Session rule
Separate ingestion/storage, analytical logic, UI, and review into fresh sessions when practical. Keep active context near 16K and use Git state as the hand-off.

## Rules
Do not claim historical accuracy that the collected data does not support. Record data gaps, retention assumptions, liquidity limitations, and uncertainty explicitly.
