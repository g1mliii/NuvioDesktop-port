# Performance baseline — 2026-06 (Phase 9)

First captured baseline from the `--perf-probe` harness plus the decisions for the Phase 9 tuning items
(9.8 image sizes/limits, 9.9 mpv cache) and the remaining 9.7 profiling gap. Re-capture per OS from the CI
`perf` job artifacts; the numbers below are a single developer-machine reference, not a cross-OS gate.

## Environment

| Field | Value |
|---|---|
| OS / RID | Windows / win-x64 |
| Cores | 16 |
| GC mode | Workstation |
| Build | Release, `Version=0.8.0` |

## Probe scenarios (developer reference)

| Scenario | elapsedMs | managed delta | peak managed | working set | notes |
|---|---|---|---|---|---|
| cold-start | ~200 | ~0.05 MB | — | ~37 MB | live SQLite service graph built into isolated temp storage |
| catalog-scroll | ~270 | ~3.4 MB | ~30 MB | ~98 MB | 5,000 fixture items fully realized, then released |
| details-loop | ~2 | ~0.02 MB | — | — | 200 build/discard cycles |
| playback-stop | skipped | — | — | — | needs `NUVIO_RUN_MPV_INTEGRATION=1` (+ optional `NUVIO_PERF_MEDIA_URL`) |

**Reading:** cold-start working set (~37 MB) is comfortably under the 200 MB idle target — and this is
pre-window; the window/render stack adds more, measured manually. catalog-scroll peaks at ~30 MB of managed
heap to *fully realize* 5,000 poster cards and returns to baseline after release (the ~3.4 MB residual is
first-touch JIT/parser/intern allocation, not a per-item leak). details-loop retains nothing.

## Deterministic invariants (hard-fail tests, all green)

- **Poster-grid virtualization (9.3):** with 5,000 items the catalog realizes ≤ 250 `PosterCard` containers
  at the top *and* after scrolling to the bottom — it never realizes the full set.
- **Image caches (9.4 / 9.12):** `DecodedImageMemoryCache` holds exactly its 128-item cap under a 1,000-key
  flood and evicts LRU; `DiskImageCache` stays within its byte cap and evicts the oldest entry first.
- **Cancellation storm (9.6 / 9.12):** under 200 rapid-fire searches the newest result wins, no stale result
  overwrites it, the majority are cancelled, and no task faults.
- **Player teardown (9.5 / 9.12):** every engine is disposed across 50 start/stop cycles with < 4 MB managed
  growth; the gated real-mpv loop leaves no orphan process; the gated libmpv loop shows no obvious growth.

## Tuning decisions

### 9.7 — UI-thread stalls during scrolling
**Open.** Current evidence proves virtualization bounds the realized-container count (≤ 250 even at 5,000
items, verified during scroll), so the scroll path does not materialize the full catalog or its bitmaps.
That is necessary but not sufficient evidence for UI-thread/frame-time health. Before 9.7 is marked complete,
capture a real-window Avalonia profiling note with frame timing or UI-thread stall evidence during catalog
scrolling. No rendering default is changed from the VM-layer evidence alone.

### 9.8 — Decoded image sizes and cache limits
**No change — within budget.** Current limits: decoded memory cache 128 items, disk cache 500 MB,
`ImageDecodeSizing` poster clamp [96, 480] px and backdrop clamp [320, 1280] px (× render scaling). Grid
cards lay out small, so decoded posters are typically ~150 px wide (~135 KB each ⇒ ~17 MB for a full 128-item
cache) — well within budget, and the disk cap is enforced by the LRU test.

*Recorded risk / follow-up:* the decoded cache caps by **item count**, not bytes. On a 4K / large-card layout
where poster decode width approaches the 480 px clamp, 128 entries could reach ~177 MB. Mitigated today
because decode width tracks the actual (small) on-screen card size, but a **byte-aware decoded cap** is a
sensible post-MVP hardening item if real 4K telemetry shows pressure. Logged, not changed, per
measure-before-optimizing.

### 9.9 — mpv cache settings
**Defaults retained** (`cache=yes`, `demuxer-max-bytes=150M`, `demuxer-max-back-bytes=50M`, plus
`vo=gpu-next`, `hwdec=auto-safe`, `config=no`) in `ExternalMpvQualityDefaults`; the libmpv engine consumes the
same option set, so external and embedded stay in parity. These match the researched plan defaults and mpv's
streaming recommendations.

The measurement path is now in place: the gated `playback-stop` probe scenario plays through real mpv and
counts buffering events, and with `NUVIO_PERF_MEDIA_URL` set it exercises the demuxer/network cache against a
real stream. On this machine only the generated-WAV (no-network) path was available, which exercises no cache
pressure (0 buffering events) and therefore yields **no evidence to justify moving the demuxer values**.
Changing them blind would risk regressing playback, so values are retained pending real-stream data captured
via the documented probe + report template.
