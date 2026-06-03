# Performance Budget

Phase 9 turns these from *targets* into a *measured* budget. Numbers are captured by the `--perf-probe`
harness and the hard-failing Phase 9 regression tests — see `docs/perf/README.md` for how to run them and
`docs/perf/baseline-2026-06.md` for the current captured values.

| Area | Budget | How it is measured | Enforced? |
|---|---|---|---|
| Idle memory after cold start | under 200 MB target, investigate above 250 MB | `--perf-probe` cold-start `workingSet` + manual window-up reading | Reported |
| Startup to interactive | under 2.5 s on a normal SSD desktop | `--perf-probe` cold-start `elapsedMs` (service graph) + manual window paint | Reported |
| Catalog browsing memory | stable after scrolling; no unbounded poster bitmap growth | `--perf-probe` catalog-scroll peak/retained; `Phase9PerformanceTests` virtualization cap | **Enforced** (virtualization cap) |
| Poster grid | virtualized items only — ≤ 250 realized containers at 5,000 items, incl. scroll | `Phase9PerformanceTests.CatalogGrid_With5000Items_StaysVirtualizedDuringScroll` | **Enforced** |
| Disk image cache | default 500 MB or less; LRU byte-cap eviction | `Phase9PerformanceTests` disk-cache pressure | **Enforced** |
| Decoded image memory cache | default 128 decoded images; LRU item-cap eviction | `Phase9PerformanceTests` decoded-cache flood | **Enforced** |
| Single image download | default 20 MB or less | `DiskImageCacheOptions.MaxImageBytes` | **Enforced** |
| Metadata cache | default 30 minute TTL | `SqliteMetadataCache` | **Enforced** |
| Search cancellation | newest-wins under a storm; no stale overwrite; stale work cancelled | `Phase9PerformanceTests` cancellation storm | **Enforced** |
| Player start/stop | every engine disposed; no orphan mpv process; no obvious leak | `Phase9PerformanceTests` (fake) + gated mpv/libmpv loops | **Enforced** |
| Playback | mpv handles decode; overlays avoid redraw loops; cache=yes, demuxer-max-bytes=150M | `--perf-probe` gated playback-stop (buffering events) | Reported |

## Report-only gate (Phase 9)

Absolute time/memory budgets are **measured and published as CI artifacts but not auto-failed** yet: runner
hardware varies and baselines must stabilize across the Windows/macOS/Linux matrix before a number becomes a
hard gate. The deterministic invariants in the table above (virtualization, cache caps, cancellation,
teardown) **do** hard-fail in the normal test run. The CI `perf` job runs `--perf-probe` on all three OSes
and uploads `artifacts/perf/perf.json`.

## Release gate

Before tagging a release candidate (Phase 10):

1. Run `./scripts/measure-perf.{ps1,sh}` on each OS (or pull the CI `perf` artifacts) and confirm cold-start
   working set and catalog-scroll memory are within budget; investigate anything over the thresholds.
2. Confirm the Phase 9 regression tests are green on all three OSes (they run in the normal `dotnet test`).
3. Attach the Phase 9.7 real-window Avalonia UI-thread/frame-time profiling note before marking Phase 9
   complete; virtualization bounds alone are not enough for the scrolling-stall gate.
4. Attach a filled `docs/perf/perf-report-template.md` for any optimization made since the last baseline, and
   refresh `docs/perf/baseline-<period>.md`.
5. Any change to image sizes/cache limits (9.8) or mpv cache settings (9.9) must cite a before/after number.

## Working rules

Measure before optimizing. UI work that touches lists, image loading, caching, or playback must include a
memory/performance regression check. Clear-cache must trim metadata rows, disk images, and decoded image
memory without touching settings, addons, or watch progress. Image cache tests must cover LRU eviction, and
decoded image tests must cover the configured item cap.
