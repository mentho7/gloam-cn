# Architecture maintenance — 2026-10-31

Baseline: `2706334`; documentation snapshot: `1727151`; branch: `codex/architecture-maintenance`.

## Changes and boundaries

- Report uncertainty now lives in `Gloam.Core/Calibration/ReportUncertaintyCalculator.cs`. The window supplies measurement and instrument context; persisted pre-compensation drift remains authoritative. Probe ownership, cancellation and session coordination retain their existing owners. This is a bounded extraction, not a wholesale rewrite of the calibration windows.
- `GammaApplyPolicy` resolves scalar request policy without reading settings or writing hardware. Game settings are cloned at the request boundary. Normal application still uses the existing coalescer, bypass rechecks and ramp guard.
- `HdrPatchRenderer` remains in the calibration UI feature after dependency review: it owns the native presentation window and consumes `Gloam.Localization.Wpf.Loc`. Moving it directly to Core would introduce the wrong dependency. The project structure guide now records this reason.
- Location detection uses `https://ipapi.co/json/`, according to the [provider API documentation](https://ipapi.co/api/). JSON numbers are validated as a complete pair of finite, in-range coordinates. Responses are bounded, redirects disabled and timeout retained at five seconds. Provider errors leave existing location unchanged; raw responses are not logged. IP-derived location remains approximate; manual coordinates remain available.
- The structure guide lists seven source projects and the test project. The historical i18n task list carries a snapshot warning rather than rewriting historical milestones.

## Request policy precedence

1. An explicit manual-calibration or temperature preview suppresses the game profile.
2. The temperature override, otherwise the scheduled temperature, supplies the baseline.
3. App exclusion sets that baseline to daylight.
4. An active game may apply its locked temperature, ForceDaylight or NightOps policy and gamma override. This preserves the existing ordering: a game policy can supersede the exclusion baseline.
5. The global manual-disable hotkey finally forces daylight.

An explicit temperature preview retains the existing active-night flag even at daylight. This detail is preserved to avoid changing preview composition semantics. Night settings composition, dose limits and hardware application remain in `GammaApplyService`; this extraction is deliberately limited to priority resolution.

## Validation scope

The pre-change suite passed 1,507 tests. Added tests exercise conflicting game/exclusion/manual policies, preview precedence, location response validation and retention of persisted drift after measurement normalization. No real display calibration, geolocation request or renderer hardware test was performed. Unit tests use synthetic provider responses and do not send the machine's IP to the location provider.

Validation result: full suite passed 1,521 tests (zero failures/skips). After the final snapshot-cloning and report exception-boundary adjustments, strict Release build passed with zero warnings/errors and all 14 new targeted tests passed. Hardware behavior remains unmeasured.
