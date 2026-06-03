# Phase 1 Behavior Mapping

This document records how `NuvioMobile` behavior was mapped into the portable
`Nuvio.Core` domain during Phase 1. The mobile app is the behavioral source of truth;
the desktop port re-expresses that behavior in C# with tests rather than sharing code.
Upstream checkouts live in the git-ignored `upstream/` folders and are never committed.

## Scope

Phase 1 ports portable *logic* only — models, validation, parsing, and redaction. No
Avalonia UI, no mpv runtime, no live addon fetching, and no platform-native loading are
introduced in this phase (those arrive in later phases).

## Mapping

| Mobile concept | Desktop home (`Nuvio.Core`) | Notes |
|---|---|---|
| Addon manifest + install/validation | `Addons/` models, `AddonService` | Valid/invalid manifest cases covered by fixtures. |
| Catalog rows / sections | `Services/CatalogService`, catalog models | Paging/virtualization is a UI concern added later; Core stays source-shaped. |
| Stream source resolution | `Services/StreamResolver`, stream models | Maps addon stream payloads into normalized `Stream` records. |
| Subtitle tracks | `Services/SubtitleService` | Subtitle mapping mirrors mobile track selection semantics. |
| Metadata / details | `Services/MetadataService`, `Metadata/` (incl. `TmdbClient`) | Metadata is cache data; parsing is deterministic and fixture-tested. |
| Watch progress | `Progress/` models | Sub-1-second positions ignored; percent/duration normalized; retained until cleared. |
| Logging of network/playback | `Security/` redaction helpers, `Net/` | Stream URLs, query strings, tokens, and sensitive headers redacted by default. |

## Redaction parity

Log redaction is a Phase 1 deliverable because it is portable and safety-critical:
stream URLs, query strings, tokens, cookies, and authorization headers are redacted,
while non-sensitive playback headers are preserved for later `IPlayerEngine` handoff.

## Verification

- Core fixture tests cover valid/invalid addon manifests, stream source mapping,
  subtitle mapping, metadata/catalog parsing, redaction, and watch-progress normalization.
- Local gate: `./scripts/verify-all.ps1` (Windows) / `./scripts/verify-all.sh` (Unix);
  CI runs the same on all three OSes.
