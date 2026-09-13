# Current status

Authoritative status after the September 12, 2026 maintenance review. Historical
investigations are in [the session archive](archive/2026-09-12-session/RC-UPGRADE.md).

## Shipped and installed

- Launcher **1.4.1**, pack **2026.09.12-r8**, Steam build **24826606**.
- Modes: **Helldivers**, **Clonedivers**, **Commandodivers**. Help: “Delta Squad is elite.”
- Scope removal is mandatory in both modded modes. Lighter textures remains optional.
- Full RC opening picture/dialogue and the four named voice slots are installed.
- Voices are manually selected: 1 Sev, 2 Fixer, 3 Scorch, 4 Boss. Armor does not route audio.
- Original Delta bodies remain; players unlock their chosen body. The four starter
  B-01 helmets are mapped to the same character order. Use Brawny body type.
- FOV is **85** on the host PC, independent of pack modes. The user confirmed 75
  improved framing; 85 gameplay is unconfirmed. Actual camera pullback remains unresolved
  and is paused by user choice.

The host uses the public manifest, Commandodivers ON and lighter textures OFF.
This maintenance pass did not change the launcher binary, public manifest, game
files, user settings, versions, or remote releases. It requires no player downloads.

## Evidence and remaining checks

| Check | Status |
| --- | --- |
| Launcher native suite | 307 checks passed |
| Original three-mode live round trip | Passed; vanilla parked all mods; both modded sets hash-verified |
| Current r8 file set | All 788 installed files match release names, sizes and hashes |
| Standard manifest rebuild | Reproduces all four mode/texture combinations; zero new upload assets |
| Fresh voice rebuild | Both output hashes match the released patch exactly |
| Full opening picture/dialogue | User confirmed in game |
| Named voice menu slots | User confirmed visible |
| Four starter helmet appearances | Pending gameplay confirmation |
| Four character voices heard by another player | Pending two-player test |
| Reinforcement and mode fallback during gameplay | Pending two-player test |

Run [the existing-release playtest](PLAYTEST.md) for the pending checks. Do not turn
installer/hash verification into a claim of multiplayer validation.

## Build state and recovery

The stale report was repaired to include all **34** RC-only files. Deployment and
variant receipts bind the recipe/report to exact file hashes. The lighter-texture
folder is `dist/pack-optimized-current`; it was reconstructed from verified local
assets by hash. The previous report and texture folder are retained.

Voice mapping, source hashes, dependency versions and encoding settings are preserved
in [build-inputs/rc](../build-inputs/rc/README.md). The portable local input capsule is
`dist/maintenance-review/rc-voice-inputs.zip`, with a checksum receipt alongside it.

For future releases use [the staging/publishing workflow](PUBLISHING.md). The default
action is offline staging; publishing requires an explicit separate action. Current
maintenance evidence lives in `dist/maintenance-review/`.

To return to vanilla, close the game and choose **Helldivers**. To undo the personal
FOV change, change only `vertical_fov` with the game closed; do not restore an entire
old settings file over newer preferences. Prior launcher/config backups remain in
`dist/release-1.4.1/`. Never delete older asset releases: current manifests reuse them.
