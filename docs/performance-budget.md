# Performance Budget

| Area | Target |
|---|---|
| Idle memory after cold start | under 200 MB target, investigate above 250 MB |
| Startup to interactive | under 2.5 seconds on a normal SSD desktop |
| Catalog browsing memory | stable after scrolling |
| Poster grid | virtualized items only |
| Disk image cache | default 500 MB or less |
| Single image download | default 20 MB or less |
| Decoded image memory cache | default 128 decoded images |
| Metadata cache | default 30 minute TTL |
| Playback | mpv handles decode; overlays avoid redraw loops |

Measure before optimizing. UI work that touches lists, image loading, caching, or playback must include a memory/performance regression check.

Clear-cache must trim metadata rows, disk images, and decoded image memory without touching settings, addons, or watch progress. Image cache tests must cover LRU eviction, and decoded image tests must cover the configured item cap.
