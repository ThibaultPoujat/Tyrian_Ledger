# Market Snapshot Contract v1 - Historical M10-M11 Contract

## Status

**Superseded for the active product runtime by ADR-010.** This contract remains
historical/transition evidence until TKT-M12-02 retires the static Pages path.
Do not design new personal-assistant features around `market-snapshot.json`.
Reusable market-collection/order-book concepts may be adapted into the local
ASP.NET/SQLite architecture.

## Historical contract

`market-snapshot.json` was the versioned public input for the static Tyrian
Ledger browser. It contained a complete, bounded collection of public Guild
Wars 2 market data; it never contained an API key, account data, preferences,
player identity, or generated recommendations.

### Generation

The historical no-key generator accepted a caller-selected artifact path:

```sh
dotnet run --project src/Gw2Tp.MarketSnapshotGenerator -- --output /path/to/market-snapshot.json
```

It exited non-zero for invalid arguments, unavailable/incomplete gateway data,
or an unwritable target, serialized a temporary file, and atomically replaced
the output only after successful collection/serialization.

The typed gateway was the only source of Guild Wars 2 data. The recorded M10
capture policy was an application conservative limit rather than an upstream API
claim: 2 requests/second, at most 2 concurrent, burst budget 20.

### JSON shape

All property names were camel case. IDs, listing counts, quantities, and copper
prices were JSON integers within JavaScript safe-integer range because the M10
browser validated them before converting to `BigInt`.

```json
{
  "contractVersion": 1,
  "generatedAtUtc": "2026-09-01T12:00:00.0000000Z",
  "compatibility": {
    "moneyUnit": "copper",
    "recommendationPolicyVersion": "m9-v1",
    "normalStackLimit": 250
  },
  "capturePolicy": {
    "requestsPerSecond": 2,
    "maxConcurrentRequests": 2,
    "burstBudget": 20
  },
  "candidates": [
    {
      "itemId": 900001,
      "itemName": "Synthetic Example Item",
      "buys": [
        { "listingCount": 3, "quantity": 10, "unitPriceInCopper": 1200 }
      ],
      "sells": [
        { "listingCount": 4, "quantity": 12, "unitPriceInCopper": 1500 }
      ]
    }
  ]
}
```

`contractVersion` equaled `1`; `moneyUnit` was `copper`, the recommendation
policy was `m9-v1`, and candidates were bounded M9 finalists. Partial/missing
finalist data was invalid and never written.

The M10 browser recalculated recommendations from this artifact plus local
capital/risk settings. That browser-side authoritative calculation path is
specifically scheduled for retirement in TKT-M12-02; future React code consumes
backend-authoritative structured calculations instead.
