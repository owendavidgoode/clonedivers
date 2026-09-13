# Historical record — superseded

See [current status](../../STATUS.md). This file preserves the investigation as it happened; installation claims and pending actions below may be obsolete.

# Republic Commando upgrade investigation — 2026-09-12

## Latest camera and launcher naming

User confirmed **75 degrees is better** and requested going wider. With HD2 closed, native
`vertical_fov` was changed75→**85**, preserving every other config byte and backing up75 under
`dist/rc-upgrade/fov85-test/`. The85-degree framing still needs testing.
The launcher display name is now **Commandodivers** (lowercase internal d), and help text is
**Delta Squad is elite.** The user specifically requested keeping the intro and armor reveal
out of help text; mode-confirmation notes are also generic. Previous casing/FOV status below
is historical where it differs.

## Launcher installed and FOV75 test authorized — latest

User approved keeping original body targets, then explicitly requested finishing the launcher
and FOV. This supersedes earlier instructions to wait at55 for further RC checks.

The three-helmet launcher is installed and running from `dist/Clonedivers.exe`. All303 native
tests passed. Live switching verified zero active patches for Helldivers,754 hash-correct files
for clonedivers and788 hash-correct files for CommandoDivers. The final selected mode is
CommandoDivers, optics ON, skinny OFF. Public releases and manifest remain unchanged.

Native `vertical_fov` changed55→75 while HD2 was closed, preserving all other config bytes.
Original config and receipt are under `dist/rc-upgrade/fov75-test/`. The setting is global to
all three modes. Camera framing in the AT-TE still needs runtime verification.
The user accidentally pressed Escape during the first launcher update attempt, then explicitly
authorized resuming; the launcher replacement and mode checks completed afterward.

## Accepted armor plan — supersedes further body retargeting

The user offered early Helldivers Mobilize targets or buying the missing items, then explicitly
selected **"Use the existing armor; I’ll unlock it"**. Stop starter/early-Mobilize body remapping
and helmet-driven fullbody investigations. Keep original Delta body replacements and the already
installed four B-01 helmet replacements. The user will acquire missing bodies themselves:

- Fixer: CM-10 Clinician body, listed at250 Super Credits in the Superstore Permanent Catalog's
  Democratic Detonation section. No purchase of the matching helmet is needed for this setup.
- Sev: SC-30 Trailblazer Scout body,50 Medals on Helldivers Mobilize page7, subject to page unlock.
- Boss and Scorch body models were already visible to the user. This does not establish ownership
  of specific native kits where model resources are shared.

Price/acquisition references checked2026-09-12:
[Clinician](https://helldivers.wiki.gg/wiki/CM-10_Clinician_Armor),
[Trailblazer Scout](https://helldivers.wiki.gg/wiki/SC-30_Trailblazer_Scout).
The existing English labels may display Fixer — CM-10 and Sev — SC-30 in shared-name menus.
No purchase has been performed by the agent. Next runtime checks: allfour complete appearances,
correct voice playback, then the previously authorized FOV75 test. FOV is still55.

The user also requested a launcher with three helmet-icon choices: Helldivers (vanilla),
clonedivers (baseclonepack), and CommandoDivers (RCmode). Subagent `launcher_three_modes` owns
source/UI/tests and a staged preview; do not replace the live launcher until RC work is finished.

## Starter equipment request — latest 2026-09-12

The user requested moving the replacements to equipment everyone starts with. **Helmet portion
installed; four independent starter body conversions remain unresolved.**

- B-01 helmet variants 1/2/3/4 now target Sev/Fixer/Scorch/Boss, matching voice slots 1/2/3/4.
  Patch261 follows the original Delta patches253–260; later optics patches shift by one.
  The production installer verified all **788** files with RC ON, optics ON, skinny OFF.
  Local version is `2026.09.12-rc-starter-helmets-local`. No remote downloads were required.
- The helmet patch contains four renamed unit resources and 17 dependencies. Every payload
  matches the corresponding installed Delta donor byte for byte; all 17 dependency resources
  are unchanged from existing winning resources. Four helmet unit targets change. Variant1's
  unit has17 kit consumers, including B-01 recolors, SA-25 Steel Trooper, B-22 Model Citizen,
  B-16 Battle-Scarred Veteran, TR-7 Ambassador of the Brand and TR-40 Gold Eagle: those helmets
  also become Sev with RC ON. Variants2–4 each have one consumer. This alias discovery arrived
  immediately after installation and was disclosed to the user. No body units changed.
  The new starter helmets have not been viewed in game yet.
- Existing Delta body replacements remain on their original armor. Four starter B-01 bodies
  share many model units; variant 1 has no exclusive body unit on either body type. Direct
  four-way swapping would mix characters or alter unrelated armor. Blender mesh merging alone
  does not resolve those shared selection references. No body remap was installed.
- All four starter bodies and helmets share normal/uppercase B-01 localization keys, so a
  strings-only patch cannot name each variant separately. Starter names stay **B-01 Tactical**;
  existing Delta body names and individual voice labels remain as before.
- An optional design question is pending: would selecting a starter helmet to choose the whole
  commando appearance be acceptable? This is a design direction, not a verified implementation;
  fitting/hiding the equipped body would still need investigation.
- All eight mode combinations pass file uniqueness, contiguous numbering and exact RC-OFF
  baseline restoration checks. FOV remains **55**. The existing full RC intro is user-confirmed
  working with dialogue; actual character voice playback still needs confirmation.

Evidence and staged ZIP: `dist/rc-upgrade/starter-commandos/helmet-patch/` (`build-report.json`,
`live-validation.json`, `install-receipt.json`). Reproducible builder:
`tools/rc-armor-retarget-helmets.py`. Shared-part and source identity audits are in the parent
`starter-commandos` directory. The local server and RC mode arrangement below still apply.

## Installed selectable RC mode — latest 2026-09-12 20:25

The user accepted manual voice selection after the armor-routing investigation and requested
matching in-game voice/armor names. This supersedes the earlier rejection of manual slots below.

- Installed and SHA256-verified all **785** target files through the production transactional
  installer, with commandos ON, optics ON, skinny OFF. No baseline downloads were needed.
- Local mode includes four added patch sets: full four-slice intro249, separate intro audio250,
  RC voice patch251 and voice/armor labels252. Original Delta armor follows these. All seven new
  files carry option `commandos`; the existing armor shares it. The readable app button is
  **RC mode: ON**, visually verified. Mode OFF restores the prior recipe's file set exactly in
  all eight option-combination checks; a live OFF/ON round trip is still pending.
- Voice1=Sev,2=Fixer,3=Scorch,4=Boss. Both normal/uppercase in-game labels replaced; armor labels
  are Sev — SC-30, Fixer — CM-10, Scorch — CE-35, Boss — DP-11. Random and unrelated text retained.
  The text override is English US and uses shared item keys, so every menu reusing those keys
  receives the label. In-game labels still need visual verification.
- **1,488/3,140 actor speech media** use322 original RC WAVs: Sev405,Fixer418,Scorch372,Boss293.
  The remaining1,652 actor targets and shared Standard exertion bank retain existing clone audio.
  These are semantic replacements, not complete new recordings of every HD2 line.
- Full voice validation independently decoded all322 unique WEMs extracted from the patch to
  PCM identical to source WAVs. All nonempty/nonsilent, no full-scale clipping. Four current154
  banks differ only in mapped Sound codec fields; no replaced stream also has a MusicTrack or
  other detected alternate codec reference. Live winning clone banks matched the template.
- Current app config points to **http://127.0.0.1:8766/manifest.json**, an unpublished local test
  manifest under `dist/rc-upgrade/rc-mode-local/`. The loopback server must run for the app's mode
  controls; restart it after reboot with `tools/start-rc-mode-server.ps1`. App binary/public
  manifest are unchanged. Original app config is `rc-mode-local/before-install-config.json`;
  pre-install inventory is `before-install-inventory.json`, and displaced files remain parked
  by the installer in the game's managed folders. Do not reset to published r6 during this test.
- **FOV remains55.** Launched through Clonedivers/Steam at20:24:54 for first combined runtime
  test. At20:26:18, native screenshot confirmed the game had reached the armory with normal
  armor rendering. The user appears to be navigating; no game input was automated. This proves
  successful startup of the combined patch, but not full intro playback (it may have been skipped).
  Asked the user whether names/voice previews work and whether the intro played or was skipped.
  Full intro playback, voice playback and target menu label checks are pending. Only proceed
  to the user-requested FOV75 test after the RC-mode checks are resolved.

### Runtime feedback after launch

- User confirmed the **full RC intro played correctly with dialogue**.
- All four named voice choices appear. Actual voice audio correctness is not yet confirmed.
- User sees Boss/Scorch body armor and Sev/Scorch helmets, but says the other items are **absent
  from the list**, not present with ordinary clone models. Ownership and menu filtering need
  checking before treating this as a resource conflict. Asked whether the missing original
  pieces have been unlocked; an installed model replacement does not establish item ownership.
- The mod author's Nexus552 page also documents missing armor-selection previews and requires
  Brawny body type. Those affect model appearance; they are not proof of missing entitlements.
- Game UI control stopped when physical Escape was pressed during the prior turn. No subsequent
  game input or FOV change was made. Read-only resource audit is continuing.

Artifacts: `voice-slots-map/mapping.json`, `voice-slots-patch/build-report.json`,
`voice-slots-full-validation/validation-report.json`, `voice-labels/patch/build-report.json`.
Reusable ZIPs in `dist/mods` and recipe now include voices and labels under the same mode.
No publication or commit has been made.

All historical status below is superseded by this section where it differs.

Status: RC remastered intro built and locally installed; global FOV restored to original 55 for startup diagnosis. Armor-audio investigation complete,
implementation remains unproven. Local recipe updated; release manifest and published pack unchanged.

## Current priority and installation — 2026-09-12 20:02

The user requires automatic **speaking-diver body-armor routing**, including remote speakers;
manual voice-slot replacement was explicitly rejected. Finish that voice patch, then deploy
intro and voices under Republic Commando mode only, then test FOV75. Keep FOV55 until then.

- Full four-slice encode completed successfully: 3840x2160, 30fps, 4,913 frames, 163.766667sec.
  `intro-four-slice-patch/` contains the complete video patch; live patch299 was replaced and
  hash-verified. The working separate RC soundtrack remains live at patch300.
- User confirmed RC dialogue in the two-second video test. Full picture/audio synchronization,
  playback past the original intro duration, and transition to menu remain unverified. The user
  stopped Computer Use before the full-video launch; it was not relaunched afterward.
- `dist/mods/Republic Commando Remastered Intro.zip` now contains the **full four-slice video
  and separate English audio**, four files total, each verified against its staging source.
  The old failed two-slice distribution ZIP has been replaced.
- Both intro and armor recipe entries use option `commandos`, displayed as **Republic Commando
  mode**, so the existing manifest builder assigns the video, soundtrack and armor to one toggle.
  Mode-off exposes the prior Clone Armory opening and soundtrack. This recipe change is staged;
  the live app still uses the published r6 manifest, and its current test intro is not yet gated.
- Native installer/toggle tests pass. No new voice override is installed, no release is published,
  and no FOV change was made. Do not claim armor-conditioned voice routing works yet.
- A manifest preview under `dist/rc-upgrade/mode-check/` was exercised through the production
  `Pack.EffectiveFiles` implementation for all eight combinations of commandos/optics/skinny.
  Every result has unique, gap-free names; RC OFF matches the prior pack's filenames, sizes and
  hashes exactly, and RC ON includes all four intro files. This is staging validation, not a
  live app toggle or a full-video playback test.
- Eight subtitle-confirmed affirmative/negative WAVs (two per commando) are staged under
  `dist/rc-upgrade/rc-voice-routing-proof/`. These are test inputs, not a proof that routing works.
  The follow-up `rc-voice-routing-proof-mapped/manifest.json` connects them to 23 affirmative/
  negative variants across all four current native voice banks. Explicit Event/Action/Sound/media
  edges are verified; semantic labels come from saved community transcription tables. Only the
  armor selector remains unresolved for this small proof. The source audit confirms all
  3,794 Full Clone Voice resources overlap Temuera, so the clone fallback must be retained
  inside the eventual routing design rather than simply removing the existing voice patch.
- Follow-up routing investigation decoded the public voice-component and kit schemas, inspected
  the actual Delta Squad ZIP and extracted all ten installed Lua resources. No owner-voice
  override is declared in the kit schema, the armor ZIP supplies only visual resources, and
  the Lua assets contain no equipment/voice-selection gameplay module. The verified asset-only
  routes are exhausted for now. Further progress requires identifying the gameplay mechanism
  that connects a selected kit to its owning avatar's dialogue prefix or per-emitter switch.
  No automatic voice patch can yet be built from the established interfaces. Manual slots are
  explicitly rejected, so do not deploy them as a substitute or proceed to the FOV75 test.

This section supersedes the historical installation and pending-encode notes below.

## Confirmed separate intro audio — latest status 2026-09-12 19:53

- Four-slice two-second video test at live patch299 played successfully (user confirmed); two-slice encode crashed in Bink. The two-second picture was followed by black with the old Star Wars soundtrack.
- Located the separate soundtrack: Wwise stream resource `27d6e984491581b2`, media193288922, sound983687583 in English cutscenes bank `e8c7da3561bee799`. Original mod stream is93.541667sec.
- Built a lossless48kHz stereo PCM WEM from the full163.766667sec RC WAV with independent MIT tool `adamXbot/wwav` (cloned in staging). Vgmstream decoded it back to exactly the original PCM samples.
- New `tools/build-rc-intro-audio.py` builds an English intro audio override: stream, bank, bank dependency. Only changed bank bytes are target sound's codec ID (Vorbis0x40001 to PCM0x10001); every unrelated byte preserved and target source parsed after serialization.
- **Installed audio patch300 and stream; user confirmed RC dialogue now plays.** Video patch299 remains the four-slice TWO-SECOND SAMPLE. FOV stays55. All original pack files remain present.
- Full four-slice encode v1 was interrupted (only73MB temp remained). Restarted with a supervised exec session55332, encoder PID9484, on19:44:20. Output `video/republic-commando-remastered-four-slice-v2.bk2` (until complete, only `.tmp` exists). Job metadata `four-slice-encode-job.json`.
- Next: wait for full encode, build video patch, close test normally, replace299 with full video (retain300 audio), test full playback/sync/skip/menu. Combine both patches in the reusable zip and update pack recipe description. Do not distribute the old two-slice zip still under `dist/mods`.
- Latest test Steam launch19:51:21. Game client starts a `crs-handler` during ordinary startup too; its presence alone is NOT proof of a crash. Only actual new dump+exception/user observation establishes one.

This section supersedes all earlier runtime/audio status below.

## Video-only retest after user's clean reboot — 2026-09-12 19:30 onward

User confirmed both Clones OFF and ON booted cleanly after reboot. RC patch299 was absent; original pack0..298 had no gaps and FOV55 remained. Reinstalled only the two RC patch299 files, hash-verified, and launched with Clonedivers' Launch button.

This launch closed before any video (user confirmed). Fresh dump `dump-2026-09-12-19.30.45-c54d6f70-GBOX1-41.dmp` records access violation0xC0000005 in **bink2w64.dll+0x22AD9**, distinct from earlier GameGuard114. Original encode flags0x40010 (two slices), working Clone Armory flags0x40012 (four slices). HD2 Audio Modder core/video conversion uses `/V6344`; official RAD command-line help defines this as Bink2+BA2 with four slices (200+6144).

Two-second four-slice sample built at `dist/rc-upgrade/four-slice-test-patch`, but installation was blocked by leftover game processes. Termination attempt returned Access denied; no sample files were copied to the game. User was asked to close HD2/crash reporting via Steam or Task Manager. **Current live patch299 is still the failing two-slice full video.**

Full four-slice re-encode running with `/V6344 /D95 /M10 /#`; job metadata `dist/rc-upgrade/four-slice-encode-job.json`, output `video/republic-commando-remastered-four-slice.bk2`. Builder now rejects two-slice inputs. Need finish encoding, validate/build, test runtime, and replace the staging/distribution zip only after success. Slice mismatch is a concrete compatibility hypothesis, not yet proven as root cause by a successful retest. FOV stays55.

## Post-reboot diagnosis — 2026-09-12 evening

- Direct executable launch failed; subsequent tests used Steam Play, matching Clonedivers' `steam://rungameid/553850` route.
- FOV was restored from 75 to 55, changing only that value. Backup: `dist/rc-upgrade/user_settings.before-fov-baseline.config`.
- Steam launch at 19:16:37 with FOV55 and all mods installed failed with GameGuard114 at 19:16:44; user independently confirmed the same error.
- All 756 mod patch files were then SHA256-inventoried and temporarily moved out of `data` to sibling `mods_diagnostic_20260912`. Zero mod patches remained in data. FOV55 retained.
- Steam launch at 19:18:22 with no mod patches failed with GameGuard114 at 19:18:29. Evidence: Steam `logs/gameprocess_log.txt` tracked `ggerror.des` with argument114. Thus active mod patches and FOV75 are not required to reproduce this startup failure.
- All 756 files, including the new RC intro, were restored and every size/SHA256 verified. Inventory: `dist/rc-upgrade/vanilla-test-mod-inventory.json`. The empty diagnostic directory remains. FOV stays55.
- Steam Verify integrity completed at 19:21:21: one file reacquired (`bin/GameGuard/nplsm.des`), 1 updated / 0 moved / 0 deleted. No other validation repairs were reported. Steam content log records build24826606 unchanged.
- After verification, Steam launch at 19:22:14 again showed the game-owned Fatal Error dialog: GameGuard Initialize error114 (visually and accessibility-text confirmed; Steam tracked `ggerror.des`114 at19:22:21).
- No executable/DLL edits or GameGuard bypass made during these tests. Root cause remains unresolved; intro playback and AT-TE framing still untested. Repeated launches without a new diagnostic change are not useful.

The resume notes below describe the pre-reboot savepoint; this post-reboot status supersedes their FOV75 instruction.

## Resume after reboot — 2026-09-12

The user requested reboot before any rollback. **Leave the RC intro installed.**
The brief baseline test parked only the new RC patch; both files were restored and SHA256-verified afterward.
Current installation: `9ba626afa44a3aa3.patch_299` and `.stream` in the HD2 data directory.
Install receipt: `dist/rc-upgrade/intro-install.json`. No existing patch was replaced or removed.
FOV remains `vertical_fov = 75`; its original value was 55.

The full 4K/30 fps remaster was encoded successfully with official RAD Video Tools:
`radvideo64.exe Bink2c <input.mp4> <output.bk2> /V200 /D95 /M10 /#`.
The earlier embedded license text was not evidence that the encoder was blocked: the actual command succeeds.
Output is KB2n, 4,913 frames, 163.766667 seconds, one embedded audio track, 260,481,264 bytes.
RAD BinkConv decoded its audio successfully to `dist/rc-upgrade/video/bink-decoded-audio.wav`.
The existing Clone Armory opening also has embedded Bink audio, so the new patch replaces only resource
`c4038098ec6384ab`/type `5ee65304478f8db5` and retains the existing pack's audio banks.
No new Wwise encoding is required for this approach; runtime audio synchronization remains untested.

Builder: `tools/build-rc-intro.py` (stdlib, Python 3.13+). Requires an existing working Clone Armory intro
patch as metadata template and an encoded Bink2n with embedded audio. Header/frame index, resource count,
metadata and exact payload read-back checks passed for the full cut and a 2-second test clip.
Output and report: `dist/rc-upgrade/intro-patch/`; reusable zip copied to
`dist/mods/Republic Commando Remastered Intro.zip` and added to `pack-recipe.json` before optional groups.
Only local installation/recipe changed; nothing published.

Launch testing: the first RC launch exited before a game window. A second launch with the new patch
temporarily disabled showed **GameGuard: Initialize error, code 114** in the game's Fatal Error dialog.
The same error was recorded in earlier benchmark work. Closed that game error and restored the RC patch.
Do not claim video playback or the AT-TE framing is verified yet. After reboot, launch normally, verify the
full 2:43.77 intro plays past 1:38, confirm audio/skip/menu behavior, then check AT-TE framing at FOV75.
Do not bypass or disable GameGuard. User explicitly prefers reboot before rollback.

Armor audio findings: see `docs/ARMOR-AUDIO-ROUTING.md` and `docs/RC-AUDIO-MAPPING.md`.
Per-emitter switches exist, but no verified speaker-armor signal. Five current voice banks are Wwise154
and have no ordinary Switch Container or Dialogue Event. Existing RC mod5351 uses manual voice slots.
Agent expanded the RC candidate catalog to 4,035 Delta clips, 3,643 matched to original RC subtitles:
`dist/rc-upgrade/rc-delta-candidates.json`. No armor-routed voice patch has been made.

Earlier investigation notes below are historical where superseded by this status.

## AT-TE camera

User clarified that a more zoomed-out camera would resolve the problem. Target an increased
exosuit follow-camera distance; increasing FOV is a different, global framing workaround.
An editable mech-camera distance field has not yet been identified. No camera fix was found on the
[author's page](https://www.nexusmods.com/helldivers2/mods/6396), Posts, or Bugs during this check.
This is not proof that no fix exists.

Applied a reversible local test with the game closed: `vertical_fov = 55` -> `vertical_fov = 75`
in `%APPDATA%/Arrowhead/Helldivers2/user_settings.config`. Verified that this is the only text change.
Original saved to `dist/rc-upgrade/user_settings.before-fov75.config`. This is a global FOV change,
not a mech-specific follow-camera translation. Its in-game result still needs checking.
Restore by setting vertical_fov back to 55 with the game closed; do not overwrite newer unrelated
settings with the full backup. The combat walker bone list has no clearly named follow-camera control.
The boot Lua resource did not expose a camera-distance setting either.

Tooling note: Obsoletes/filediver v2.3.4 cannot read this installation's compressed DSAR/NXA archives;
xypwn/filediver v0.7.51 works. Saved inventories and extracted walker bones/boot script are in
`dist/rc-upgrade/`. Use upstream current CLI flags; its old `-c` option is deprecated.

The r6 asset conflict report puts the AT-TE after Clone Blasters; its shared unit resources win.
That makes a simple load-order change a weak first candidate, but does not prove the model or rig is correct.

Recommended diagnostic sequence, with the game closed between configurations:

1. Compare the same exosuit with the pack off. If the fault persists, investigate vanilla settings/game behavior.
2. Compare the walker with its `Walker Animation Edits` component excluded. This isolates animation/rig behavior.
3. Compare with the affected walker mesh excluded. If that removes the fault, inspect mesh scale, rig and
   camera obstruction; a camera offset or mesh adjustment is a candidate only after reproducing it.
4. Reliable fallback: use vanilla exosuits while retaining the separate LAAT/c carrier components.
   An increased in-game field of view may help framing, but is not a demonstrated clipping fix.

The source has separate Patriot, Emancipator, Lumberer, Breakthrough, wreckage, textures,
carrier and animation components. Build diagnostic variants through the recipe into staging;
do not remove numbered files from the live pack and leave gaps. Filenames in the archived r6 report
are historical and must not be assumed to match an installation with different optional groups.

## Dialogue

The shipped pack uses Full Clone Voice Conversion. Delta Squad's AIO archive contains armor meshes and
textures, not its members' voices. RC stratagem beeps are enabled; the shield-pack radio chatter recipe is disabled.
The local Clonedivers settings also had `commandos: false` when inspected.

[RC Helldiver Voices, Nexus 5351](https://www.nexusmods.com/helldivers2/mods/5351) uses original game
lines for all four members. Its author labels it outdated and allows updates. Compatibility must be verified;
do not deploy its old banks blindly. The alternative Nexus 260 is a Sev-only AI voice conversion,
not the original four-character dialogue requested here.

Preserve 5351's established slot convention if adapting its mapping:

| Armor | Character | Voice slot |
|---|---|---|
| SC-30 | Sev | 1 |
| CM-10 | Fixer | 2 |
| CE-35 | Scorch | 3 |
| DP-11 | Boss | 4 |

These slots are manually selected. The current Clonedivers app only manages files outside the running game;
it has no armor-change detection or armor-to-voice routing. Automatic matching is unverified and requires
separate investigation. A four-slot replacement affects those slots for every armor, not just commandos.
Do not promise that ordinary armor will retain generic clone voices under that implementation.

Follow-up: the user wants rules applied to the speaking diver's armor, including remote squadmates,
not a requirement that players manually coordinate voice slots. The intended local playback rule is
SC-30 -> Sev, CM-10 -> Fixer, CE-35 -> Scorch, DP-11 -> Boss, other armor -> generic clone.
Apply it per speaking character; a global switch based on the listener's armor would be incorrect.

[Helldivers International's author documentation](https://www.nexusmods.com/helldivers2/mods/14183)
confirms that current replacements route by the four voice slots and that each client hears its own
audio. Thus an audio replacement can affect how remote divers sound on the listener's machine,
but does not transmit the replacement recording to other machines. The precise network payload,
event replication coverage, and audibility distances have not been inspected or measured.
Armor-conditioned playback remains a feasibility investigation: determine whether the speaking
unit's armor is available to the audio switch/event system before promising an asset-only solution.

Original game found at `C:\Program Files (x86)\Steam\steamapps\common\Star Wars Republic Commando\GameData`.
`tools/export-rc-dialogue.ps1` exported all eight `*voice.uax` archives through the
[community UCC commandlet](https://wiki.swrc-modding.net/index.php?title=Republic_Commando_UCC),
using UCC from SWRCFix 2.15 in a separate workspace runtime copy. The game installation was untouched.

Output: `dist/rc-dialogue-complete/catalog.json`, `sources.json`, per-package logs, and `audio/`.
The catalog contains 5,587 nonempty RIFF/WAVE clips, including Boss 445, Fixer 943, Scorch 847 and Sev 900.
Each exported Sound was checked for a WAV header and hashed. SoundMultiple selector objects produce empty
files and are excluded from the catalog. UCC reported zero errors with missing non-audio dependency warnings;
these counts describe the eight voice archives, not every sound resource in the entire game.
Playback quality and HD2 event coverage are not yet tested.

Next: obtain/adapt the old pack's event mapping or map the extracted lines against current HD2 player banks;
build with current audio tools; check callouts, exertions and missing-event fallback; inspect conflicts with
Full Clone Voice and Temuera's banks. Only enable a finished, tested voice option in the recipe.

## Opening montage

Confirmed the requested scene in `System/subtitles_pro.int`: Taun We addresses the infant and introduces
the commandos' training and Delta Squad. Its scene is `Maps/pro.ctm`, with dialogue in `prologue_voice.uax`;
20 Taun We WAVs were exported. No standalone movie of this sequence was found in `Movies/`.
The sequence therefore needs a recording/render before it can be packaged as an intro replacement.
The user approved sourcing an existing YouTube recording. Acquired the
[original intro uploaded by Jeff Morris](https://www.youtube.com/watch?v=RStr41flbpc),
168 seconds, with a 960x720 video stream and a separate audio stream, under
`dist/rc-upgrade/video/original-intro.136.mp4` and `original-intro.140.m4a`.
Source metadata is saved alongside them. Download completed; full audiovisual review, muxing,
game-format conversion and patch construction remain. A user-supplied recording is no longer needed.

Superseded by the user's chosen [B3rthi recording](https://www.youtube.com/watch?v=rN_52WzBq8M).
Downloaded its 1280x720 video and audio, then losslessly muxed to
`dist/rc-upgrade/video/republic-commando-intro.mp4` (195.46 seconds). Extracted stereo 48 kHz
PCM audio to the matching `.wav` for audio authoring. Full FFmpeg decoding passes; sampled images
confirm the prologue, training, squad and Geonosis sequence. This recording is now superseded.

The user's latest selection is [WoofWoofWolffe's remastered intro](https://www.youtube.com/watch?v=CL5i33CTld8).
Downloaded the 3840x2160, 30 fps VP9 source and original AAC track to
`dist/rc-upgrade/video/remastered-source.313.webm` and `remastered-source.140.m4a`.
The full source metadata, including the creator's description and collaborator credits, is preserved in
`remastered-intro.metadata.json`. The selected cut is source **00:08.400 through 02:52.166667**:
remove the opening menu/loading screen, keep the infant/training/Delta Squad sequence, and remove
the subsequent loading screen, credits and promotional footage. The source switches to white at 8.4s,
black at 172s, and the ending loading screen at 172.167s; these boundaries were checked with frame samples.
Apply a 0.5s video fade starting at source 171.5s and a 0.416667s audio fade starting at 171.75s.
The full untrimmed source and its credits remain available alongside the cut.

Prepared output: `dist/rc-upgrade/video/republic-commando-remastered-intro.mp4`, 4K H.264/AAC,
**2:43.767**, with matching stereo 48 kHz PCM16 `.wav` for audio authoring.
This remaster is now the selected source. It has not been installed as the game's intro.
Full output decoding passes with no errors, all 4,913 video frames were encoded, and sampled ending
frames confirm a fade to black without the loading screen or credits. Trim/encoding settings are saved
in `remastered-cut.json`; output SHA256 hashes and checks are in `remastered-validation.json`.

Current game inventory confirms video resource `c4038098ec6384ab`, type `5ee65304478f8db5` (`bk2`),
in archive `860f3b0262bd229d`. The game-format video conversion and separate Wwise intro-audio patch
are still outstanding. Official RAD Video Tools 2026.06 were unpacked locally (no system installation);
their embedded UI text says Bink 2 encoders require a license. The Bink2c help attempt did not return
console output and was stopped. No encoded Bink 2 or finished intro patch was produced.

### Original duration and shorter fallback

Extracted the shipped intro from archive `860f3b0262bd229d` with current filediver to
`dist/rc-upgrade/original-intro-extracted/0xc4038098ec6384ab.bk2`.
Its Bink header gives **2,941 frames at 30/1 fps = 98.033333 seconds (1:38.033)**,
3840x2160, and zero embedded audio tracks. The declared file size matches the extracted
898,521,500 bytes; the complete frame index is strictly increasing and ends at that file size.
The header layout was checked against [FFmpeg's Bink demuxer](https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/libavformat/bink.c).
The actual codec revision is `KB2n`, which the bundled FFmpeg cannot probe/decode; duration was
read from the header and validated against the frame index, not measured by playing the movie.
The machine-readable findings are in `dist/rc-upgrade/original-intro-duration.json`.

The native video duration is **not a verified maximum replacement duration**.
[Skip Intro's author changelog](https://www.nexusmods.com/helldivers2/mods/7217)
documents reducing both audio and video to 0.1 seconds and subsequent compatibility updates,
so exact original-length playback is not a universal requirement for a complete intro mod.
This does not prove that a longer replacement will play fully on this installation. No current
runtime maximum or fixed cutoff has been established, and the remaster has not been tested in game.
Video completion and the separate audio events both need checking during eventual packaging/testing.
Do not truncate the preferred remaster solely on the assumption of a 98-second hard limit.

User supplied [Coastlake's Venator fleet animation](https://www.youtube.com/watch?v=hkVDSAyDkhE)
as a **fallback if the preferred intros fail**, not a replacement of the preferred selection.
Saved 1080p/24 fps video and audio, muxed without re-encoding to
`dist/rc-upgrade/video/venator-fleet-alternative.mp4`, duration **1:22.11** (about 15.9s below
the original). Source metadata is `short-alternative.metadata.json`. The preferred remastered
cut remains **2:43.767**, about 65.7s longer than the original. Neither candidate is installed.

The pack already enables `Clone Armory Opening Videos`, which includes both video and audio resources.
Replace the startup cinematic specifically, checking video duration, separate audio, skipping and the return
to the menu. Do not confuse it with the short in-engine Super Destroyer arrival music described as
"Intro Cinematic" in the audio wiki. The r6 report also shows a shared audio resource from Opening Videos
overridden by AT-TE audio, so a whole-bank copy can affect unrelated ship/vehicle sounds.

Next investigations: mech-camera distance, per-speaker armor-conditioned voice routing, and intro
conversion/packaging. No finished RC voice patch or intro patch has been built or installed.
