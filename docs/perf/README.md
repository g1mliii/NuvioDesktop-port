# Performance harness (Phase 9)

Phase 9 is the *measure-before-optimizing* phase. Its gate is **"memory budget is measured, not guessed."**
This directory holds the measurement workflow, the report template, and the captured baseline.

## What measures what

| Concern | Where | Enforced? |
|---|---|---|
| Startup + memory baselines (cold start, catalog scroll, details loop, playback stop) | `--perf-probe` (in-app) via `scripts/measure-perf.{ps1,sh}` | **Reported**, not enforced (see "Report-only gate") |
| Poster-grid virtualization under stress (5,000 items, incl. scroll) | `tests/.../ViewModels/Phase9PerformanceTests.cs` | **Hard-fail** |
| Image-cache caps (decoded LRU item cap, disk LRU byte cap) | `Phase9PerformanceTests.cs` | **Hard-fail** |
| Search cancellation storm (newest-wins, no stale overwrite) | `Phase9PerformanceTests.cs` | **Hard-fail** |
| Player start/stop teardown (fake engine, no leak) | `Phase9PerformanceTests.cs` | **Hard-fail** |
| Player start/stop teardown (real mpv: no orphan process) | `ExternalMpvEngineIntegrationTests` (`NUVIO_RUN_MPV_INTEGRATION=1`) | **Hard-fail when enabled** |
| libmpv create/dispose teardown (no obvious leak) | `LibMpvEngineIntegrationTests` (`NUVIO_RUN_LIBMPV_INTEGRATION=1`) | **Hard-fail when enabled** |

Division of labour: the probe owns *absolute* numbers (timing, managed heap) measured headlessly so the
same binary reports identically on Windows, macOS, and Linux. Deterministic *invariants* (virtualization
caps, cache caps, no-stale, no-leak) live in the xunit suites because that is where assertions belong.

## Running the probe

```powershell
./scripts/measure-perf.ps1   # Windows
```
```bash
./scripts/measure-perf.sh    # macOS/Linux
```

Both build Release and run `Nuvio.Desktop --perf-probe --perf-out artifacts/perf/perf.json`, printing a JSON
report and writing it to `artifacts/perf/` (git-ignored). Scenarios:

- **cold-start** — constructs the live SQLite-backed service graph into isolated temp storage. This is the
  dominant *managed* startup cost paid before the first window paints. (Window paint itself is a manual
  measurement — launch the app and observe; it is not part of the headless probe.)
- **catalog-scroll** — builds a large fixture catalog (`NUVIO_PERF_CATALOG_ITEMS`, default 5000) and fully
  realizes its poster grid, reporting peak managed bytes and retained delta after release.
- **details-loop** — builds/discards media detail state `NUVIO_PERF_DETAILS_LOOPS` times (default 200).
- **playback-stop** — **gated**: only runs with `NUVIO_RUN_MPV_INTEGRATION=1` and mpv installed. Loops
  load/play/stop/dispose; with `NUVIO_PERF_MEDIA_URL` set it uses a real stream so buffering behavior can
  inform mpv cache tuning (9.9). Without the flag it reports `skipped`. Media URLs are never echoed.

Each scenario reports `elapsedMs`, `managedBefore/After/DeltaBytes` (`GC.GetTotalMemory(forceFullCollection)`
around the work), `workingSetBytes`, and scenario notes.

## Report-only gate

Absolute budgets (startup time, idle/working-set memory) are **measured and published as CI artifacts but not
enforced** in Phase 9, because runner hardware varies and baselines need to stabilize across the matrix
first. The CI `perf` job runs the probe on all three OSes and uploads the JSON. What *does* hard-fail is the
deterministic xunit suite above. Promotion of any absolute budget to an enforced gate is a deliberate later
step (tracked in `docs/performance-budget.md`).

The probe process exits non-zero if any scenario reports `status: "error"`; optional scenarios such as
`playback-stop` may report `skipped` without failing the job.

## Writing a before/after report

Copy `perf-report-template.md` whenever an optimization is proposed. Tie every change to a measured
before/after number and a budget. "No change — already within budget" is a valid, recorded outcome.
The current captured numbers, completed tuning decisions, and open profiling gaps live in
`baseline-2026-06.md`.
