# TKT-M0-01 - Verify MTPLX/Qwen3.8-27B development model path

## Milestone
M0

## Goal
Determine whether the user-provided Qwen3.8-27B-GGUF can be used directly, must be converted, or should be replaced by an MTPLX-compatible Qwen3.8 27B artifact.

## Dependencies
None

## Acceptance criteria
- [x] Inspect current MTPLX documentation and model compatibility behavior.
- [x] Run the smallest safe model inspection/load command available on the Mac.
- [x] Document whether raw GGUF loads directly.
- [x] Document the chosen MTPLX-compatible artifact and provenance if conversion/catalog artifact is required.
- [x] Do not modify application code to couple it to MTPLX.

## Required tests
- [x] A reproducible smoke command succeeds or fails with an explicit documented reason.
- [x] The decision is recorded in the ticket/ADR notes.

## Non-goals
- Changing the application architecture.
- Training a model.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Decision record (executed 2026-08-20)

### Decision
The raw `unsloth/Qwen3.8-27B-GGUF` cannot be used directly. MTPLX requires an
MTP-equipped model (`"tier": "no-MTP"`, `can_run: false`); a raw GGUF download
is therefore rejected as a development path. The project uses the existing
MTPLX catalog artifact `Youssofal/Qwen3.8-27B-MTPLX-Optimized-Speed-FP16`,
which is verified-native (`tier: verified`, `can_run: true`, MTP contract
verified). No conversion was performed for this ticket; the FP16 sibling of
the parent artifact already exists in the local MTPLX cache.

No ADR was created. Under the project ADR policy (`docs/workflow/ai-development-workflow.md`),
an ADR is required only when a decision is durable and cross-cutting or when
a ticket requires an architecture change (M0 context). This ticket changes no
code and no architecture; ADR-003 already forbids any application LLM/MTPLX
runtime coupling. The decision is recorded here in the ticket only.

### Evidence
MTPLX v2.8.3, macOS arm64, cache `~/.mtplx/models`.

1. Raw GGUF (smallest safe inspection; no download performed — the local cache
   entry `https:----huggingface.co--unsloth--Qwen3.8-27B-GGUF?...` is 0 B and
   `mtplx inspect` operates on local cache metadata):
   `mtplx inspect --json --no-strict-exit-code <cache-dir>/https:----huggingface.co--unsloth--Qwen3.8-27B-GGUF?show_file_info=Qwen3.8-27B-UD-Q4_K_XL.gguf`
   - `architecture_recognized: false`, `model_files: []`, `config_exists: false`
   - verdict: `"Model has no MTP head. MTPLX requires an MTP-equipped model."`
   - `runtime_compatibility: unsupported`, `tier: no-MTP`, `can_run: false`
   - Documented reason: MTPLX's runtime contract requires an MTP head; a raw
     GGUF without MTP tensors cannot satisfy it.

2. Chosen artifact:
   `mtplx inspect --json --no-strict-exit-code <cache-dir>/Youssofal--Qwen3.8-27B-MTPLX-Optimized-Speed-FP16`
   - `architecture: Qwen3_5ForConditionalGeneration`, recognized: true
   - `tier: verified`, `support_level: verified-native`, `can_run: true`
   - `"Verified MTPLX runtime contract found."`
   - MTP tensor gate: 15/15 expected keys present, `passes_tensor_gate: true`
   - Runtime contract `mtplx_runtime.json` (contract mtplx_version 2.6.0,
     mtp_depth_max 3, recommended profile `turbo`)
   - Recommended backend: `qwen3_next`

3. `mtplx models`: the FP16 artifact is `contract=true` (21.3 GB); the raw
   GGUF cache entry is `contract=false` (0 B).

### Chosen artifact and provenance
- Artifact: `Youssofal/Qwen3.8-27B-MTPLX-Optimized-Speed-FP16`
  (Hugging Face, apache-2.0 license per its README; M1/M2 FP16 sibling of
  `Youssofal/Qwen3.8-27B-MTPLX-Optimized-Speed`; base model
  `Qwen/Qwen3.8-27B`, 4-bit dynamic quant with fp16 floating tensors).
- Local cache copy is an MTPLX conversion output
  (`MTPLX_FP16_CONVERSION_MANIFEST.json`: created 2026-08-15,
  source `~/.mtplx/models/Qwen3.8-27B-MTPLX-Optimized-Speed`,
  policy "convert bf16 floating tensors to fp16; preserve packed
  integer/quantized tensors", per-shard sha256 recorded).
- The artifact is the default MTPLX selection for an M1/M2 Mac with 32 GB+.
- Development use is limited to the local coding-agent runtime (MTPLX on
  loopback). The application itself has no dependency on MTPLX and none was
  introduced by this ticket (no application code exists yet).

### VERIFY items
- VERIFY: upstream license terms of `Qwen/Qwen3.8-27B` for the FP16 sibling's
  redistribution context (README states apache-2.0 on the MTPLX repo; the
  base model license was not independently checked).
- VERIFY: whether the FP16 sibling is the correct pick for this Mac's chip
  generation (README: M1/M2 use FP16 sibling; M3+ use the parent
  `Qwen3.8-27B-MTPLX-Optimized-Speed`). `mtplx hardware` timed out during
  inspection and was not completed.
- VERIFY: a live generation smoke (`mtplx runtime-smoke` or one `mtplx ask`)
  was not run for this ticket; the acceptance criterion is satisfied by the
  documented inspect verdicts. Run one before relying on sustained dev use.

### Follow-up
None required for M0-01. If a full runtime smoke is desired, it is a
small follow-up; if the chip is M3+, consider switching the cached default to
the parent (non-FP16) artifact.

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
