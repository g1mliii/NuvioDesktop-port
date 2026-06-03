# Performance report — <change title>

- **Date:** <YYYY-MM-DD>
- **Author:** <name>
- **Trigger:** <measured regression / budget breach / profiling finding that prompted this>
- **Scope:** <files / subsystem touched>

## Environment

| Field | Value |
|---|---|
| OS / RID | <e.g. Windows / win-x64> |
| Machine | <CPU, cores, RAM, disk> |
| GC mode | <Workstation / Server> |
| Build | Release, `Version=<x.y.z>` |
| Probe command | `./scripts/measure-perf.<ps1\|sh>` (+ any `NUVIO_PERF_*` knobs) |

## Measurements

| Scenario / metric | Budget | Before | After | Delta | Source |
|---|---|---|---|---|---|
| cold-start elapsedMs | < 2500 (window-to-interactive, manual) | | | | perf.json |
| cold-start workingSet | < 250 MB | | | | perf.json |
| catalog-scroll peakManaged | stable, no unbounded growth | | | | perf.json |
| catalog-scroll retained delta | ~0 after release | | | | perf.json |
| poster grid realized containers | ≤ 250 at 5000 items | | | | Phase9PerformanceTests |
| decoded image cache count | ≤ 128 | | | | Phase9PerformanceTests |
| disk image cache bytes | ≤ configured cap (500 MB default) | | | | Phase9PerformanceTests |
| playback start/stop heap delta | < 4 MB over 50 cycles | | | | Phase9PerformanceTests |
| mpv buffering events | <context-dependent> | | | | perf.json (playback-stop) |

## Decision

- [ ] Change applied — justified by the measured delta above.
- [ ] No change — already within budget (record the numbers anyway).
- [ ] Deferred — follow-up logged (link):

## Notes / risks
<worst-case scenarios, evidence gaps (e.g. no real-stream data), follow-ups>
