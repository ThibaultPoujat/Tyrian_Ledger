# Historical External Verification Notes

> These notes document the retired Qwen/MTPLX discovery path. They are kept
> for project history and must not be used as active Codex instructions.

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
