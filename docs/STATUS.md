# Current status

## September 13 release: launcher 1.5.0 / pack 2026.09.13-r9

This release adds independent Full/Lighter selection before installation, quieter
cached mode switches, offline cached metadata, explicit repair and diagnostics,
installation receipts, exact-target recovery and bounded resumable downloads.
Commandodivers help states the Brawny body-type requirement.

The r9 profile feed reuses the existing tested r8 assets in all four combinations
of Clonedivers/Commandodivers and Full/Lighter. Players keeping their selection
need only the launcher update. The format-2 compatibility feed retains pack r8
and advertises app 1.5.0; the new launcher reads the format-3 r9 feed. Both feeds
are published together after hosted assets and the public executable are verified.

The final release inputs are under `dist/release-final/`; its `staged-verified/release.json`
records publication completion and the feed commit. Earlier stages containing
additional RC streaming assets are superseded and must not be published.

## Validation and performance limits

- Native launcher suite passes; profile, diagnostics, release, builder and recovery
  fixtures pass. The new launcher preview was rendered and visually inspected.
- All four final mode/profile sets retain their existing asset names, sizes and
  hashes. The profile migration requires zero new game-asset uploads.
- The user confirmed normal visuals during the local shadows and distant-detail
  geometry trials. Their combat captures do not establish an FPS improvement.
  Original geometry was restored. See [trial results](PERFORMANCE-TRIAL.md).
- Additional RC texture streaming passed byte-for-byte payload and container
  checks, but a live startup/visual check was blocked by a stale GameGuard process.
  Windows elevation consent was canceled; no protection bypass or reboot was used.
- Additional RC streaming, reduced-resolution profiles and mesh experiments remain
  private. None is included in r9. Weak-PC performance remains unproven; this
  launcher release does not claim a gameplay FPS improvement.

## Existing game content

- Modes: **Helldivers**, **Clonedivers**, **Commandodivers**. Commando help starts
  with “Delta Squad is elite.” Scope removal is included in both modded modes.
- The full RC opening picture and dialogue were confirmed in game.
- Voices are selected manually: 1 Sev, 2 Fixer, 3 Scorch, 4 Boss. Armor does not
  automatically route audio. The named voice menu was confirmed visible.
- Original Delta body replacements remain; players unlock their chosen body.
  Four starter B-01 helmets map to the same character order. Use **Brawny** body
  type: the user confirmed this resolved the reported missing Commando bodies.
- Host FOV is 85, independent of pack modes, and was present during the local
  combat captures. Actual camera pullback remains unresolved and paused by choice.
- Four starter helmet appearances, hearing all four character voices from another
  player, and reinforcement/fallback behavior still need multiplayer acceptance.
  See [playtest](PLAYTEST.md). Hash checks do not establish multiplayer behavior.

## Build state and recovery

The complete deployment report includes all 34 RC-only files. Deployment and
variant receipts bind the recipe/report to exact hashes. The verified existing
Lighter source is `dist/pack-optimized-current`. Voice source hashes, dependency
versions and encoding settings are in [build inputs](../build-inputs/rc/README.md).

Use [the publishing workflow](PUBLISHING.md) for future releases. Never overwrite
or delete old content-addressed asset releases: current manifests reuse them.
The deeper [player investigation](PLAYER-UPDATE-INVESTIGATION.md) preserves the
architecture findings and experimental history.

To return to vanilla, close the game and choose **Helldivers**. To undo personal
FOV changes, edit only `vertical_fov` with the game closed; do not restore an entire
old settings file over newer preferences. Prior backups remain under
`dist/release-1.4.1/` and maintenance evidence under `dist/maintenance-review/`.
