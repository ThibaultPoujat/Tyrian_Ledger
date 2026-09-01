# Market Snapshot Contract v1

`market-snapshot.json` is the versioned public input for the static Tyrian
Ledger browser. It contains a complete, bounded collection of public Guild
Wars 2 market data; it never contains an API key, account data, preferences,
player identity, or generated recommendations.

## Generation

Run the no-key generator with a caller-selected artifact path:

```sh
dotnet run --project src/Gw2Tp.MarketSnapshotGenerator -- --output /path/to/market-snapshot.json
```

The command exits non-zero for invalid arguments, unavailable or incomplete
gateway data, or an unwritable target. It serializes a temporary file in the
target directory and atomically replaces the output only after collection and
serialization succeed.

The typed gateway is the only source of Guild Wars 2 data. Capture policy is
an application-level conservative limit, not a claim about the upstream API:
2 requests per second, at most 2 concurrent requests, and burst budget 20.
The artifact records those values for run evidence.

## JSON shape

All property names are camel case. IDs, listing counts, quantities, and copper
prices are JSON integers within the JavaScript safe-integer range. Browser
code must validate that range before converting values to `BigInt` for
calculation.

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

`contractVersion` must equal `1`. Consumers must reject unsupported contract
versions and compatibility metadata. `generatedAtUtc` is a canonical UTC
ISO-8601 timestamp ending in `Z`. `moneyUnit` is `copper`,
`recommendationPolicyVersion` is `m9-v1`, and `normalStackLimit` is `250`.
The capture-policy values must exactly match the v1 policy above.

Candidates are the M9 finalist set, bounded to 200 entries after aggregate
price screening. They are strictly ascending by `itemId`; each buy and sell
array is ordered by `unitPriceInCopper`, then `quantity`, then
`listingCount`. Every order level value is positive. Empty candidate arrays
are valid when no public item satisfies the finalist screen; partial or
missing finalist data is invalid and is never written.

The browser recalculates recommendations from this input and its local capital
and risk settings. It must not treat the snapshot as a recommendation,
financial guarantee, or instruction to place a trade.
