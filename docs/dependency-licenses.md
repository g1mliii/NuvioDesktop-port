# Dependency License Inventory

This file is a release-blocking placeholder. Before public binary distribution, list every runtime dependency, license, source URL, and whether it is bundled.

| Dependency | Purpose | License | Bundled | Notes |
|---|---|---|---|---|
| Avalonia | desktop UI framework | TBD | yes | generated template dependency |
| CommunityToolkit.Mvvm | MVVM helpers | TBD | yes | generated template dependency |
| mpv/libmpv | playback backend | TBD | no for Phase 2 | External `mpv` is discovered and launched directly; bundling requires a later provenance and license pass. |
| MPV Manager | optional setup helper | MIT per upstream site | no | Documented only; not launched, bundled, or trusted as a playback executable. |
