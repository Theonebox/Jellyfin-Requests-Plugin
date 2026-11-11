# Archive

Older releases of the Requests plugin, kept for reference. They are no longer offered in the
plugin catalog, and their GitHub releases have been removed: the live
[`manifest.json`](../manifest.json) lists only the current version (1.1).

- `releases/` — the release packages 1.0.0 through 1.0.0.75 (1.0.0.74 was never released).
  These are the only remaining copies.
- `manifest-history.json` — the catalog manifest entries for those versions, with changelogs and
  checksums. Their download URLs point to GitHub releases that no longer exist.

To make an old version installable again, publish its zip from `releases/` as a GitHub release
under the tag in its `sourceUrl`, then copy its entry back into `manifest.json`.
