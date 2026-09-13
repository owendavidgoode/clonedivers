# Current status

Authoritative status after the September 12, 2026 maintenance review. Historical
investigations are in [the session archive](archive/2026-09-12-session/RC-UPGRADE.md).

## September 13: 1.5.0 candidate implemented, not published

The combined update is implemented locally. Public players still receive **1.4.1 / r8**.
Candidate 1.5.0 adds independent pre-install texture selection, cached metadata for
offline switching, explicit repair/diagnostics, installation receipts, exact-target
recovery and bounded resumable downloads. Brawny help and quieter cached switches
are included. Native tests pass **351 assertions**, profile tests **52**, diagnostics
**12**, measurement fixtures **11**, and coordinated-release fixtures **16**.

The r9 public candidate contains Full/Lighter only; Lighter streams additional RC
textures. Both source folders pass complete file-hash checks and mode isolation.
`dist/profile-candidates/manifest-public-v3.json` is the asset candidate;
`dist/release-1.5.0-build/Clonedivers.exe` is the separate executable build. The
staged release will seal a format-2 compatibility feed (r8 pack plus new app metadata)
and a separate format-3 profile feed in one coordinated publication.

Current 1024/128 and 512/128 trial profiles built with zero optimizer failures and
remain separate from the public candidate. Their reduced GPU companion sizes are
identical; their streaming data differs by 621,086,080 bytes. No gameplay performance
claim is made. **RC streaming visual/startup acceptance and weak-PC A/B testing are
pending.** See [performance testing](PERFORMANCE-TESTING.md) and
[texture profile staging](TEXTURE-PROFILES.md). Nothing has been installed into the
live game or published during this implementation pass.

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

## September 13 follow-up (local changes)

The user confirmed that changing an affected player's body type from Lean to Brawny
resolved the missing Commando body armor. The public r8 assets were available with
matching hosted hashes; all 34 RC files and 126 armor resource winners matched in
the full and lighter-texture variants.

The launcher source now shows “Requires Brawny body type.” beneath the Commandodivers
description. Mode switches using already-downloaded files proceed without confirmation;
downloads and detected low disk space still prompt. These UI changes are not published.
The existing native test suite passes. The weakest PC's performance issue remains
unresolved pending hardware, settings and symptom details; the old reduced-resolution
experiment has no demonstrated gameplay benefit and is not a shipped fix.

The deeper [player update investigation](PLAYER-UPDATE-INVESTIGATION.md) records
reproduced repair/recovery/cache issues, the profile-format prerequisite, current
asset costs and a proposed scope/validation plan for one coordinated release.

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
