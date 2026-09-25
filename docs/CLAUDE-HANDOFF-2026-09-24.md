# Clonedivers handoff to Claude — 2026-09-24

> Picked up by Claude the same evening. Current state is in
> [AIM-MARKERS-AND-FIFTH-VOICE.md](AIM-MARKERS-AND-FIFTH-VOICE.md): the marker validation route
> is solved (`tools/validate-aim-markers.py`), v4 is installed locally as `patch_304` pending
> gameplay checks, and the fifth voice is blocked for asset patches. The notes below are Codex's.

The user is switching agents because Codex usage is nearly exhausted. Continue in this workspace; no active processes or background agent work need to be adopted from this turn.

## Read first

1. `docs/STATUS.md` and `docs/releases/v1.6.0.md` for shipped scope.
2. `docs/AIM-MARKERS-AND-FIFTH-VOICE.md` for current research, evidence, constraints and next checks.
3. This handoff for operational details and unresolved pitfalls.

## Current release and installation

- Workspace: `C:\Users\goode\OneDrive\Desktop\clonedivers`; Windows PowerShell; Node 24.13.1.
- HEAD: `a7dfccf`, main. Published launcher **1.6.0**, pack **2026.09.24-r10**, HD2 build **25480438** (7.1.1).
- Release: https://github.com/owendavidgoode/clonedivers/releases/tag/v1.6.0
- Release verification: `docs/releases/v1.6.0-verification.json`; immutable release staging: `dist/release-ready-1.6.0/release.json`; local completion receipt: `dist/release-1.6.0-local/completion.json`.
- `manifest-v3.json` has r10. Compatibility `manifest.json` intentionally keeps the r8 pack with launcher 1.6.0.
- Local launcher: `dist/local-current/Clonedivers.exe`; desktop shortcut points to that release. Last known settings use the public feed, r10, full textures, droids enabled. No current marker/voice prototypes were installed or published.
- Do not use old `tools/start-local-preview.ps1`: stale preview/hash assumptions.
- Game: `C:\Program Files (x86)\Steam\steamapps\common\Helldivers 2`.

## User's current requested work

1. Add visible enemy aim markers, specifically the Spider Droid eye/vent locations. Existing MTT cannon and Spider Droid weapon restoration already shipped; do not confuse those with the new markers.
2. Add a **true fifth in-game voice choice**, regular Clone Trooper, while retaining Sev, Fixer, Scorch and Boss. User explicitly superseded an earlier replace-one-slot fallback. Do not replace a commando, rename Random, or call a launcher profile a fifth slot.
3. Search broadly across all available Star Wars games for useful voice recordings. User objected to limiting the search to one Battlefront game. Audio sourcing is separate from fifth-slot registration.

## Fifth-slot status

Not implemented; no functional registration mechanism found. Extracted XAML receives `ScrollList`/`Items` from runtime view models. Adding a visible button alone would not establish selection, persistence or multiplayer voice identity. The current published type library has additional voice prefix enum values, but that does not demonstrate additional player-selectable slots. Do not state that a fifth slot is universally impossible; it is unproven through our current asset-patch method.

Relevant files under `dist/aim-voice-2026-09-24`:

- `ui/content/ui/shared/resources/tab_templates.xaml` around 2032 and `button_templates.xaml` around 619.
- `voice-schema.json`, `dl_library-v0.7.53.gz`, `datalib-v0.7.53.go`.
- `star-wars-installs.json`, `star-wars-audio-inventory.json` (11 installed titles).
- `sound-tool-index.json`: preliminary SoundFMVextractor repository discovery; no new extraction completed.

`tools/inspect-voice-slots.mts` parses the current LTLD schema. Member records are **72 bytes**, unlike the older 52-byte schema. Current enum names are unresolved; do not guess numeric-to-name mappings. Historical routing notes: `docs/archive/2026-09-12-session/ARMOR-AUDIO-ROUTING.md`.

Battlefront II Classic, Republic Commando, Galactic Battlegrounds/Clone Campaigns, Fallen Order, LEGO Skywalker Saga, Empire at War Gold, Force Unleashed, Squadrons, Starfighter and two X-Wing games are installed. Battlefront I was not found in the checked locations; user may own it elsewhere. Existing `dist/mods` clone audio ZIPs are inventoried in the research doc. The full-clone conversion author attributes recordings to Battlefront II (2017), which is distinct from the installed Classic game. No fresh audio extraction/transcription was performed in this task. Existing RC source pool is approximately 3,988 unique PCM clips; shipped r10 uses 496 unique clips across 1,525 mappings. Reuse earlier transcription/deduplication work under `dist/rc-dialogue-complete` and `dist/rc-audio-review-2026-09-22`.

## Marker prototype status and reproduction

New untracked tools:

- `tools/inspect-aim-markers.mts`
- `tools/inspect-native-weakpoints.mts`
- `tools/inspect-voice-slots.mts`
- `tools/build-aim-markers.mts`

Successful output: `dist/aim-voice-2026-09-24/markers-v3`; v1/v2 were failed attempts. Rebuild into a **new** folder with `node tools/build-aim-markers.mts dist/aim-voice-2026-09-24/markers-v4`. Tool intentionally refuses to overwrite an existing output directory. All four tools pass `node --check`; builder's geometry preservation assertions passed. No independent model round trip, rendering or gameplay validation passed yet.

Source Spider Droid bundle: `dist/aim-voice-2026-09-24/spider/9ba626afa44a3aa3.patch_192`, extracted from recovered CIS overhaul ZIP. Target UNIT `ef570293245a17c2`, type `e0a48d0be9a7453f`. Stock unit and GLB locations are in the tool sources and research doc.

Eye anchor `dmg_eye`: [0.6784721, 1.7334894, 4.0407271], bound to turret joint 65. Vent anchor `dmg_vents`: [0, -1.6501188, 3.0071661], bound to boss joint 38. These anchor and parent matrices match stock byte-for-byte. Marker rings are trial sizes, not measured hitbox extents.

Builder adds 192 vertices / 384 triangles per visible LOD using native eye material `df762856873e639c`, preserves original geometry, and emits a standalone UNIT override. Five body meshes IDs14–18 receive a third group; selected skeleton maps gain identity remaps. Native GPU bone order indexes boss=1 and turret=4. Vertex stride44, positions offset4, half weights offset32, byte bone indexes offset40. Layouts can use 16- or 32-bit indexes. Vertex buffers have small trailing padding: insert appended vertices before that padding. Geometry header fields and pointer lists are documented in the source. Review bounds, opaque MeshData and skinning assumptions; structural preservation does not establish runtime correctness.

## Important validation pitfall and hard links

Filediver executable: `dist/player-update-2026-09-23/current-kits/filediver.exe` v0.7.53. Older source is under `dist/rc-upgrade/filediver-source` and does not necessarily match current binary/schema.

Attempted extraction with `--gamedir dist/aim-voice-2026-09-24/roundtrip-game-v2 --include '*ef570293245a17c2*' --model-format glb --model-include-lods --model-include-gibs`. It returned 6/6 but **exported the stock model**, not the patch. `candidate-model-v2` is not a validated candidate. The real game uses compressed NXA bundles; loose `.patch_0` was ignored by this extraction route. A standalone loose archive was read but failed startup because required customization hash-lookup data was absent. Need a proper standalone parser or extraction environment that actually applies the candidate.

**`roundtrip-game-v2/data` contains hard links to installed stock archives and `bundles*.nxa` files. NEVER edit these linked files in place.** They share contents with the installed game. Trial patch files are independent copies. Only read-only extraction has used the linked files; stock contents were not changed. When cleaning up links, remove only verified workspace directory entries, never source files or targets.

## Remaining steps

- Resolve actual candidate parsing/export, assert new marker primitive counts/weights, then inspect visibility, clipping and bounds.
- Perform reversible gameplay QA of moving/turning/damaged Spider Droids before shipping. Candidate currently has `installed:false`, `gameplayVerified:false`.
- Continue fifth-slot research independently; preserve all existing voices unless user changes the requirement.
- Curate broader audio sources by actual speaker/transcript, clean dialogue, compatible delivery and event semantics. Filename counts include effects, installer audio and translations; they are not unique dialogue counts.

## Other context and preservation

Camera remains an outstanding separate investigation. Cutaway failed visually. GameGuard 110/114 and Steam's stale Running state occurred repeatedly, but camera causation was not proved. Do not claim anti-cheat definitively identified the FOV/mesh change. User most recently prioritized shipping other work first; do not restart invasive camera experiments as part of this handoff.

Historical untracked logs and camera/repair/preview scripts already existed; leave them alone. Current work and both handoff/research docs are **uncommitted**. No source changes to launcher/manifests were made in this task. No live rollout is pending from these prototypes.

Disk space was limited (roughly 13GB before recent small artifacts); avoid full game copies. A uv install under `dist/uv-runtime` hit sandbox read-denials; Node works. No need to reinstall audio tooling without first checking existing tools.
