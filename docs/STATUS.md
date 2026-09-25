# Current status

## September 24 (late night): launcher 1.6.2 / pack r13 published (voice gap fill)

Published through the sealed workflow (feed commit `0a973e3`; [verification](releases/v1.6.2-verification.json)).

318 silent or Battlefront-clone lines now play Republic Commando or Temuera Morrison recordings,
and voice 4 is labelled "Boss/Clone". Still silent per voice: about 105-121 lines, mostly numbers,
NATO letters, directions and distances, for which no clone recording exists. Built with
`tools/build-voice-fill.py`. [Details](AIM-MARKERS-AND-FIFTH-VOICE.md).

## September 24 (late night): launcher 1.6.1 + pack r12 published (opening logo trimmed)

Published through the sealed workflow (feed commit `b4a7c7c`; [verification](releases/v1.6.1-verification.json)).

The owner asked to publish r12 as **1.6.1**: the launcher is rebuilt with only its version bumped, and
[1.6.1 notes](releases/v1.6.1.md) cover r11 and r12. The earlier pack-only staging `dist/release-ready-r12` is unused.
r12 changes only the opening: Coastlake's end logo is removed. Video 0-75.0 s plus 1.0 s black
(the shot's own fade reaches black at 74.71 s; the logo starts at 75.42 s), 1,824 frames, four-slice
Bink 2; soundtrack 76.0 s with a 74.5-76.0 s fade. Built with `tools/build-pack-candidate.py
--version 2026.09.24-r12` (the generalised r11 tool; it reproduces the r11 manifest byte for byte).

## September 24 (night): pack r11 published (launcher unchanged)

Published through the sealed workflow (feed commit `27dc6d5`; [verification](releases/pack-2026.09.24-r11-verification.json)). Owner-authorized release without a prior in-game boot: pack 2026.09.24-r11 on launcher 1.6.0.
It replaces the opening with Coastlake's Venator fleet animation (four-slice Bink 2, 1080p/24,
82.1 s, plus a PCM English soundtrack; the only header differences from the shipped RC intro
patches are length fields). Boss gains 263 Temuera Morrison fill lines, and Spider Droid eye/vent
markers ship as manifest `patch_318` (option `droids`). Inputs: `tools/build-r11-candidate.py`
→ `dist/release-r11-inputs`; notes: [pack r11](releases/pack-2026.09.24-r11.md). In game still
pending: opening playback, Boss fill lines, markers, and the unexplained exit-time crash dump of
the 19:55 session (access violation in `helldivers2.exe`, written just after settings save).

## September 24 (evening): marker candidate installed locally; fifth voice blocked

Spider Droid eye/vent marker candidate v4 passes independent Filediver validation: 28/28
original primitives intact, markers skinned to `turret` (eye) and `boss` (vents). It is
installed locally as `9ba626afa44a3aa3.patch_304` for a gameplay test, with a receipt. It is not
in any feed. A fifth selectable voice cannot be added with asset patches: the game has exactly
four voice identities, defined in protected executable data. The owner kept Boss and asked
for Temuera Morrison clone lines to fill his gaps: 263 Temuera recordings now cover calls
that played Battlefront clone lines or silence. That is installed locally as additive
`patch_305`. Markers and Boss merge await one gameplay session; neither is published.
Details: [markers and fifth voice](AIM-MARKERS-AND-FIFTH-VOICE.md).

## September 24: 1.6.0 / r10 published

The combined roster, ADS repair, expanded voices/armor, droid toggle, JohnsonPotatoMode,
vehicle replacements, Guard Dog cleanup and optional performance sharing are published
in launcher 1.6.0 / pack 2026.09.24-r10. The owner explicitly approved including the
experimental Supply FRV mesh port and Watcher probe loop. Their gameplay checks remain
open. Camera experiments remain excluded; eye/vent markers are unfinished.

Startup passed on today's installed build 25480438 (7.1.1). All 65 rebased banks,
English text and the Watcher bank are byte-identical to the audited 7.1 sources.
All native tests, 16 option/profile combinations and the telemetry sampler passed.
Publication completed through the sealed release workflow. GitHub asset sizes and
SHA-256 digests, the downloaded launcher, latest-release target and both public feeds
were verified. This machine now uses release 1.6.0/r10 and the public feed; the desktop
shortcut no longer starts a preview server. Performance enrollment was preserved.
Older dated sections below are historical snapshots.

Details: [1.6.0 release notes](releases/v1.6.0.md).

## September 24: shutdown hang reproduced with manual Steam

Steam PID 21500 was started manually and measured outside any Windows job, with
an unrestricted token. HD2 PID 17592 and crash helper PID 11564 both exited with
code 0 and signaled handles, but Steam retains `Running=1`. No new 114 appears in
this session's launch records. Steam is left open; no game/pack settings changed.
This rules out automation job inheritance as a necessary condition for the exit
tracking hang, while leaving the separate error-114 hypothesis unresolved.
Evidence and interpretation: [GameGuard investigation](GAMEGUARD-INVESTIGATION.md).

## September 23: Steam stopped; GameGuard history reviewed

User's Steam Stop attempt stalled. Ended Steam PID 6412 at their request and
verified Steam and HD2 are absent. No automatic restart. Current settings retain
FOV 85 and 1,133 active pack files; camera loader/probe and rejected cutaway remain
absent. See [GameGuard investigation](GAMEGUARD-INVESTIGATION.md) for the evidence.

114 predates the camera work, persisted with FOV 55 and zero active mods, and
FOV 75 later worked. Today's strongest lead is Steam startup context/state:
manual Steam boot worked both vanilla and through Clonedivers. Current read-only
measurements show approved automation shells in a Windows job while Explorer and
the working launcher are outside jobs; the failed Steam job/token was not captured.
This is a hypothesis, not an established internal cause. A controlled manual /
automated / manual Steam comparison with fixed files is prepared but not run.

The latest Steam hang followed normal exit code 0 from both game and crash helper;
the historical browser-helper explanation does not account for this latest run.
No pack/public release/security changes were made during the review.

## Current local mode: Clonedivers launch confirmed; Steam exit tracking stuck

The launcher restored `2026.09.23-player-preview3` Full and recorded verification
at 21:35:53 on September 23. All 1,133 patches are active again, `mods_off` is empty,
and all six camera diagnostic/loader files remain absent. Steam PID 6412 was
manually started at 21:35:16 and has been left alone. The user confirms launching
through Clonedivers worked well, then HD2 closed and Steam remained stuck on Running.
Windows handle queries establish HD2 PID 6844 and crash-helper PID 13548 both exited
with code 0 and signaled process handles (`WaitForSingleObject(...,0)=0`). Steam
still reports `Running=1`. This is stale Steam tracking, not a live game/helper.
Asked the user to try Steam's Stop button before another client restart. Do not
repeat the mod restore or automatically restart Steam through agent tools.

## Working baseline: manual Steam startup succeeds

The user confirms HD2 launched successfully after opening Steam from Windows
Start and launching the game from the library, with all mod patches parked.
Preserve this manually started Steam session; do not restart Steam from agent
tools. This implicates launch context/state, but does not prove which inherited
process restriction or other condition caused 114. No further GameGuard repair
or compatibility relabeling is warranted on this evidence.

Next: once the user closes the successful game, restore Clonedivers through the
launcher and retest using the same manually started Steam session. The combined
pack and Lua camera probe have not yet passed this comparison. Keep the probe out.

## Startup isolation: all mods parked, GameGuard still fails

All 1,133 pack files are now reversibly parked in `mods_off`; zero active patch
files remain. The plain-game launch produced GameGuard 114 at September 23 21:24:07.
Following nProtect's folder-refresh instructions, preserved `bin/GameGuard` as
`bin/GameGuard.backup-20260923`; the game downloaded fresh files but again produced
114 at 21:26:32. No protection settings changed. Logs and receipts are local under
`dist/vanilla-startup-2026-09-23/`. Next test: user starts Steam manually from Windows
Start and launches HD2 directly, leaving the mods parked. Camera work is paused.

The separate launcher compatibility prompt compares installed game build 25327279
with pack `gameBuild=24826606`. The candidate records `gameBuildAudited=25327279`
but `runtimeTested=false`. The EXE and game DLL hashes are unchanged. Do not relabel
the pack gameplay-verified merely to hide the prompt. Reconcile build/audit/runtime
metadata when a working baseline and gameplay acceptance are available.

## Local launcher refreshed; Steam revalidation next

At the user's request, refreshed the local install to pack
`2026.09.23-player-preview3` and launcher `1.6.0-preview6`. All 1,133 Full-profile
pack files already matched; no asset replacement was needed. The desktop shortcut
was still opening public launcher 1.5.0. It now runs `tools/start-local-preview.ps1`,
which starts/verifies the loopback feed before opening the hash-verified preview
in `dist/local-current/`. Both initial feed startup and reuse were checked, including
the shortcut's Windows PowerShell host. Previous shortcut and install receipts
are preserved in `dist/local-refresh-2026-09-23/`.

User will revalidate Helldivers 2 in Steam next. No game launch requested during
this refresh. Camera probe/loader remain removed; original AT-TE meshes retained.
Public launcher/feed are unchanged. GameGuard 114 remains unresolved.

Steam's subsequent false "running" state was cleared by a graceful client restart
without launching HD2. No game process remained; Steam's per-app registry state
reported `Running=0`, `Installed=1`, `Updating=0`. Its console log also revealed the
earlier direct administrator attempt had redirected into a pending `ShowGameArgs`
prompt, so that attempt is not evidence of a further GameGuard failure. Ready for
the user's Steam revalidation.

## AT-TE runtime probe removed; baseline launch blocked

The camera issue also occurs at regular aspect ratios. A read-only Lua capability
probe and Bingus Shared Loader v16 were tested locally in additive patch slots
304/305 and have now been removed, with all six files verified absent. Existing
pack assets are unchanged. Build fingerprints and package
checksums match; seven offline LuaJIT probe cases passed. The latest launch hit
GameGuard 114 even after the user's reboot and produced no probe log. After
removing the diagnostic and gracefully restarting Steam, the original pack also
hit 114 at 20:53:52. A subsequent clean baseline reboot also hit 114 at 20:57:21.
Arrowhead's signed GameGuard uninstall/reinstall utilities completed with exit 0,
but the next baseline launch still hit 114 at 21:01:11. A one-time administrator
launch was approved and started (PID 20040), then exited; its user-visible outcome
is unconfirmed. No new Steam error entry or matching Windows crash event was found.
The user chose a local launcher refresh followed by Steam revalidation instead of
further launch retries. Camera/vehicle APIs remain unverified;
do not reinstall the probe yet. No permanent compatibility/security setting changes
and no team release. See
[camera investigation and exact diagnostic removal](AT-TE-CAMERA.md).

## AT-TE cutaway rejected / baseline restored

User gameplay feedback: no improvement inside the vehicle and excessive exterior
removal. The cutaway trial is rejected. Restore the prior local pack
`2026.09.23-player-preview3`; verification lives under
`dist/walker-cutaway-2026-09-23/rollback/`. Launcher `1.6.0-preview6` remains current,
including the hidden mod toggles in regular Helldivers. The cutaway is not a
release candidate; its earlier structural checks are not gameplay acceptance.
No cause has been established for the reported crash or occupied-view behavior.

## September 23 AT-TE cutaway trial

Local `1.6.0-preview6` hides mod option controls in regular Helldivers; returning
to Clonedivers restores the saved choices. Native tests and both mode previews
passed. An unavailable old texture preference falls back to Full when entering
a modded mode, avoiding a hidden-control dead end.

`2026.09.23-atte-cutaway1` is the new local visual experiment, opening the rear
central hull across all four AT-TE variants and visible detail levels. Only
mesh index lists/counts change; physics, rigging, shadows and unrelated assets
are unchanged. See [cutaway details and rollback](AT-TE-CAMERA.md).
The local install receipt and verification are under
`dist/walker-cutaway-2026-09-23/local-install/`; new feed port 8768.
Pilot visibility and gameplay validation remain pending. Public release unchanged.

## September 23 launcher cleanup

Local launcher `1.6.0-preview5` removes the Delta Squad and Walker weapons controls
from the combined roster and enforces both features during file selection, even
when older saved flags are false. Droid skins remains independent. One
**JohnsonPotatoMode™** On/Off control replaces Full/Lighter; it currently selects
the existing full-resolution streaming profile, not the old experimental 512px
candidate. Help text is shorter and retains the Brawny requirement.

Native tests, all 16 stale-flag/profile/droid combinations, release build, and
rendered launcher preview passed. Build/preview are under
`dist/launcher-cleanup-2026-09-23/`. No new game assets or public release.
See [AT-TE findings](AT-TE-CAMERA.md) for cutaway versus invisible-body limitations;
no camera/mesh experiment has been installed.

## September 23 automatic performance collection

The collector is deployed in Goode Works at
`https://clonedivers-telemetry.goodecraft.com/`, with a dedicated D1 database,
private R2 bucket, 90-day lifecycle/cleanup and separate owner/invitation secrets.
Production tests using the launcher client pass enrollment, compressed upload,
duplicate retry, private readback and revocation. A clearly labeled self-test
contains actual hardware and collector-process samples, not gameplay.
The local `1.6.0-preview4` launcher is enrolled as Owen; its first automatic report
arrived. It temporarily uses the workers.dev alias because the local DNS resolver
still caches the custom hostname's pre-creation NXDOMAIN. Public DNS and custom
domain TLS/health are verified. Owner credentials are DPAPI-encrypted in AppData,
outside the repository. See [collection status](AUTOMATIC-PERFORMANCE-REPORTS.md).
The Windows FPS-permission retry succeeded and group membership was verified.
A Windows sign-out/sign-in is still required to refresh the user token.
Live-game frame capture and weak-PC overhead checks remain open. Public launcher
1.5.0/r9 is unchanged; the collector deployment is live but no team update shipped.

## September 23 vehicles and local session logging

The newest local candidate is `dist/vehicle-update-2026-09-23/feed/manifest.json`
(`2026.09.23-player-preview3`). It adds the Falchion Bastion, an experimental TX-130
Supply FRV geometry port, and restored spider weapons under **Walker weapons**.
Eye/vent markers remain unresolved; no original-War-Strider fallback was applied.
All 16 selection checks pass. Launcher `1.6.0-preview2` adds bounded local session
observations and a Save report action; native and diagnostics tests pass.
See [details and limitations](VEHICLES-AND-CLIENT-LOGGING.md). Public 1.5.0/r9 is unchanged.
Installed locally and byte-verified: 1,133 Full files with Delta/droids/walker weapons
on. The loopback feed serves this newest candidate; rollback/install receipts are
in its parent directory's `local-install/` folder. Gameplay acceptance remains open.

## September 23 QA follow-up

ADS passed user testing. Cannon visibility remains unconfirmed. A follow-up local
candidate adds qualified Commando item names, expands the mixed clone roster from
7 to 15 equipment groups, and stages a probe-audio transfer from Guard Dog to one
Watcher loop. All 16 option/profile combinations and serialized resource checks
pass. Watcher playback/stop behavior and added armor visuals require gameplay QA.
See [follow-up details](PLAYER-QA-FOLLOWUP.md). Public 1.5.0/r9 is unchanged.
The follow-up is installed locally and all 1,124 Full-profile files are verified.
The loopback server now serves `dist/player-followup-2026-09-23/feed/`.
Rollback receipts are under that follow-up directory's `local-install/` folder.

## HD2 7.1: rebuilt candidate ready for gameplay acceptance

Local test installed September 23 at 21:21 UTC after user authorization. All 1,124
Full-profile files are byte-verified with Delta, droids and walker cannons enabled.
Steam launch requested; gameplay outcome is pending. Previous replaced files are
preserved in `mods_old`; settings, prior receipt, baseline manifest and install
journal are under `dist/player-update-2026-09-23/local-install/`. The loopback feed
on port 8767 serves `feed-7.1-final/`; local settings point there for test toggles.
Public 1.5.0/r9 is unchanged. Use the staged preview launcher during this test.

The [7.1 rebase](HD2-7.1-COMPATIBILITY.md) is complete in staging. Both voice presets
retain all 3,246 current English keys, restoring the 287 missing entries. The audio
rebuild refreshes 70 bank occurrences across 55 bundles, including three later
weapon-bank overrides, plus all four RC banks. Current routing/properties and
new-only embedded samples are retained; all 1,525 expanded RC streams are unchanged.
Serialized audio/provenance checks and all 16 selection combinations pass.

Use `dist/player-update-2026-09-23/feed-7.1-final/manifest.json` with the existing
`launcher/Clonedivers.exe` preview. Earlier `feed/`, `feed-7.1/` and partial audio
stages are superseded. This candidate is now installed locally, not published. In-game
ADS, armor, interrupted voices and sustained-fire checks remain required.

## September 23 combined player update — local test candidate

`dist/player-update-2026-09-23/feed-7.1-final/manifest.json` now combines the scope candidate,
shared clone/Commando armor roster, four independent Commando helmet targets,
expanded Delta voices, regular clone voice preset, droid switch and original
Factory Strider/MTT cannon visibility switch. Seven additional legion styles have
independent material/texture IDs on audited equipment groups. The armor source's
intended Body Expansion → Material Reset load order is restored.

Full and Lighter roster variants are built and verified without reducing texture
resolution. Native launcher tests and all 16 selection combinations pass; the final
launcher preview is visually checked. A self-contained local preview launcher is
under `dist/player-update-2026-09-23/launcher/`. No installed game files or public
release feeds changed. Gameplay acceptance is still required, particularly ADS,
material loading, four helmet choices, cannon alignment and weaker-PC memory.
War Strider weak-point geometry is not fixed by the cannon option.

The machine now has Steam build 25327279. Updated Filediver v0.7.53 equipment
definitions retain the four unique helmet targets (411 kit records vs 402 cached).
The current game archive catalog contains all 90 scope targets. Neither check
establishes runtime compatibility. See [the detailed candidate notes](PLAYER-CUSTOMIZATION-UPDATE.md).

## September 20–22 local follow-up

The [player customization update](PLAYER-CUSTOMIZATION-UPDATE.md) records the new
ADS report, optional droid skins, aiming-point request, clone variety and shared
Commando helmet mappings. Droid-toggle UI and a zero-new-asset candidate pass local
tests. The user clarified widespread missing/blurry ADS and enemy weapon/weak-point
visibility, and supports a mixed clone/commando equipment roster. Asset work and
gameplay validation remain pending.
The [local RC audio review](RC-AUDIO-REVIEW.md) processed all 3,988 distinct Delta
recordings with free local transcription, followed by an independent caption pass
for shipped and flagged clips. It records uncertainties and additional Boss candidates;
it has not changed voice slots, audio levels, or shipped mappings.
The [September 23 RC voice expansion](RC-VOICE-EXPANSION.md) is built in staging:
496 distinct recordings across 1,525 mapped entries, up from 322 across 1,488.
Source, actor, fallback and archive checks pass. In-game playback and Boss volume
balancing remain pending. No follow-up changes have been installed or published;
the release below remains live.

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
