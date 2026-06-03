# Source Availability (GPL Written Offer)

Nuvio Desktop is distributed under the **GNU General Public License, version 3 or later
(GPL-3.0-or-later)**. The GPL requires that the complete corresponding source code be
available to anyone who receives a binary. This document is the project's written offer
and explains the two ways the source is provided.

## 1. Complete corresponding source (preferred)

The complete source for every released binary is the tagged commit it was built from:

- **Repository:** <https://github.com/g1mliii/nuviodesktop-port>
- **Release tag:** `v<VERSION>` (for example, `v0.8.0`) — the tag matches the version
  reported by the app's `--self-check` output and embedded in the artifact file name.

Each binary release also ships a reproducible source tarball produced from that exact
tag by `scripts/make-source-bundle.{sh,ps1}`:

```
nuvio-desktop-src-<VERSION>.tar.gz
```

The tarball is generated with `git archive` and therefore honors `.gitignore`; the
git-ignored `upstream/` reference checkouts are **not** included (they are third-party
code with their own distribution terms and are not part of this project's source).

## 2. Written offer

If you received a Nuvio Desktop binary and cannot obtain the corresponding source from
the repository or release assets above, you may request it. For any binary we
distribute, we will provide the complete corresponding source for that version.

- **Request contact:** info@anchored.site
- **Subject:** `Nuvio Desktop source request`
- Include the **version** (from `--self-check` or the artifact file name) and your
  preferred delivery method (link or archive).

This offer is valid for as long as we distribute the corresponding binaries, and in any
case for at least three years from the date of distribution, consistent with the GPL.

## Third-party components

Bundled third-party components (Avalonia, CommunityToolkit.Mvvm, the .NET runtime, etc.)
are inventoried in [`dependency-licenses.md`](dependency-licenses.md). mpv/libmpv is
**not** bundled — see [`native-dependency-provenance.md`](native-dependency-provenance.md).
Each third-party component remains under its own license; this offer covers the Nuvio
Desktop source.
