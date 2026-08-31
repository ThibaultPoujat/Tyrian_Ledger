# Historical External Verification Notes

> These notes document historical external verification. The retired
> Qwen/MTPLX material and the dated M2 public probe below are records, not
> active Codex instructions.

Verification date: 2026-08-19.

## MTPLX

- MTPLX is Apple Silicon/macOS focused and currently documents a local OpenAI-compatible server on `127.0.0.1:8000`.
- Current MTPLX documentation says it runs MLX-native models and treats GGUF as the llama.cpp format; raw GGUF repositories are not assumed to load directly.
- Current MTPLX documentation lists Qwen 3.8 27B compatible catalog artifacts and memory guidance. Exact local model choice must be tested on the user's Mac.
- Source: https://github.com/youssofal/MTPLX
- Source: https://github.com/youssofal/MTPLX/blob/main/CHANGELOG.md

## Qwen3.8-27B GGUF

- Unsloth's current model page documents Qwen3.8-27B GGUF variants and quantization sizes.
- It is a GGUF distribution, which is separate from MTPLX's native MLX runtime path.
- Source: https://huggingface.co/unsloth/Qwen3.8-27B-GGUF

## Visual Studio on Mac

- Visual Studio for Mac was retired on 2024-08-31. Microsoft recommends Visual Studio Code on Mac.
- Source: https://learn.microsoft.com/en-us/lifecycle/announcements/visual-studio-mac-end-of-servicing
- Source: https://code.visualstudio.com/docs/setup/mac

## GW2 API

The current project uses these references as the starting point for verification: 
- https://wiki.guildwars2.com/wiki/API:2/commerce/prices
- https://wiki.guildwars2.com/wiki/API:2/commerce/listings
- https://wiki.guildwars2.com/wiki/API:2/tokeninfo
- https://wiki.guildwars2.com/wiki/API:API_key
- https://wiki.guildwars2.com/wiki/API:Best_practices
- https://wiki.guildwars2.com/wiki/API:Terms_of_Use

The exact API contract must always be rechecked before release.

## M2 public API probe

On 2026-08-26, TKT-M2-01 performed the owner-approved, read-only, no-key
verification check. It made exactly two requests and retained only metadata
and response shape:

- `GET https://api.guildwars2.com/v2.json?v=latest` returned HTTP 200 with
  `langs`, `routes`, and `schema_versions`; the newest listed schema version
  was `2025-08-29T01:00:00.000Z`.
- `GET https://api.guildwars2.com/v2/commerce/prices?v=latest` returned HTTP
  200 with an array of 27,987 numeric item IDs and
  `X-Rate-Limit-Limit: 600`. It did not fetch detailed records without an
  explicit `ids` parameter.

This confirms the global schema value used by the public prices/listings
client. It does not settle batch limits, 206 behavior, rate-limit scope,
burst/refill values, 429 headers, or a sustainable rate; those remain in the
VERIFY register.

## M9 public whole-market contract verification

On 2026-08-31, TKT-M9-02 performed keyless, read-only public contract checks;
no API response body was retained:

- `GET https://api.guildwars2.com/v2.json?v=latest` returned HTTP 200. Its
  public `routes` include active, unauthenticated `/v2/commerce/prices`,
  `/v2/commerce/listings`, and `/v2/items`; its newest `schema_versions` entry
  remains `2025-08-29T01:00:00.000Z`.
- `GET https://api.guildwars2.com/v2/items?ids=19684,19709&v=latest` returned
  HTTP 200 with `X-Result-Total`, `X-Result-Count`, and
  `X-Rate-Limit-Limit: 600`. The item records exposed `id` and `name`, but no
  per-item `max_stack` field.
- The Guild Wars 2 Wiki’s public endpoint pages document that the
  `/v2/commerce/prices` root returns item IDs, while `ids` requests for prices
  and listings return response-object arrays. `ids` batching and HTTP 206 are
  handled conservatively under VERIFY-004 rather than treated as settled.

M9 pins `2025-08-29T01:00:00.000Z` for public prices, listings, and items. Its
normal stack cap of 250 is owner-selected product policy, not external API
metadata.
