# Four-stage upgrade validation

Stages are stacked branches: each starts at the preceding validated commit. Tests are automated; no colorimeter or real HDR calibration is claimed.

## 1 — Effective output and monitor cards

Branch: `codex/01-effective-display-state`.

Added request/receipt state with per-device revision protection, explicit hardware API outcomes and localized policy-source text. Saved values remain separately labeled. Fixed card clipping found by rendered-window inspection. The background polling regression now uses an injected test interval while production stays at two seconds.

Gate: strict Release build (0 warnings/errors), isolated launch smoke, full suite 1,523 passed, 0 skipped/failed, including opt-in English/Chinese/pseudo-language rendered windows. Chinese card inspected after the clipping fix. Artifacts: `artifacts/stage1-ui`, `artifacts/smoke-stage1`.
