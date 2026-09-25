# Aim markers and fifth voice investigation

Status: 2026-09-24 (evening). Published launcher 1.6.0 and pack r10 are unchanged. The v4
marker candidate is installed **locally only** for gameplay testing.

## Spider Droid eye and vent markers

**Removed in 1.6.3 / pack r14 (2026-09-25).** The owner played with v4 (shipped in 1.6.1–1.6.2 as
manifest `patch_318`) and judged that the markers did not look right. The notes below
document the attempt. Markers did render in game, which confirms the engine accepted the
UV3 layout change, but no specific visual feedback was recorded.

### Where the weak points are

The Spider Droid replaces the War Strider's visuals only. Its physics, hitboxes and state
machine stay stock, so shots still hit the invisible War Strider zones. The unit's `damageable`
group has one node per zone (legs, knees, feet, hull, rocket pods, eye, vents). Two matter here:

- `dmg_eye` [0.678, 1.733, 4.041] (Stingray frame: x lateral, y forward, z up). The stock eye
  is a 7 cm `m_boteye` disc at exactly this point, skinned to `turret`. The other four
  `m_boteye` clusters are rocket-pod and top lights, not weak points.
- `dmg_vents` [0, -1.650, 3.007]. The nearest stock vent geometry is 0.18 m away, skinned to `boss`.

Both bind matrices, and the `turret`/`boss` parents, match the stock model byte for byte.
The whole Spider Droid ball is skinned to `boss`. As the War Strider turret yaws, the eye marker
therefore slides around the ball, which is correct: it tracks the turret-mounted hitbox.

### Candidate v4 (`tools/build-aim-markers.mts`)

`node tools/build-aim-markers.mts dist/aim-voice-2026-09-24/markers-v4` (refuses to overwrite).
It overrides only UNIT `ef570293245a17c2`; no physics or state machine is supplied. The source is
the installed `9ba626afa44a3aa3.patch_192` (= manifest `patch_193`, option `droids`). The builder
pins its SHA-256 because its measured constants belong to that unit. The JohnsonPotatoMode
(lighter) variant carries a byte-identical unit, so one marker patch serves both profiles.

- **Eye:** bullseye ring (0.16/0.12 m) plus 5 cm centre dot, facing out of the ball.
  **Vents:** three orthogonal rings (0.26/0.20 m) plus a 7 cm centre dot.
  The sizes are visual choices, not measured hitbox extents.
- 570 vertices / 552 triangles per visible LOD (meshes 14–18). Meshes 0–3 are shadow casters
  and 4–13 culling boxes; they are unchanged, so markers cast no shadow.
- Material: stock `m_boteye` (`df762856873e639c`). It is texture-free (emissive parameters only).
  Every stock unit using it (War Strider, Berserker, spawner) has **four** UV sets; the Spider
  Droid body layouts had three. v4 therefore inserts a zero UV3 (half2) into the three visible
  body layouts, taking the stride from 44 to 48. Layout records hold only kind/format/layer
  items, a count and a stride (no offsets or declaration hash), so this is a descriptor edit.
  Every original vertex byte is asserted identical apart from the inserted slot.
- Marker vertices carry real octahedral normals and the stock eye's mean UV0–UV3.
- v3 flaws fixed: it copied bytes 16–20 as "UV", but that is the packed normal in this layout,
  leaving UVs at zero. It also lacked UV3 for the eye material.

### Independent validation (`tools/validate-aim-markers.py`)

`python tools/validate-aim-markers.py <candidate.patch_N> <out> [--zoom x,y,z,hw]`

Filediver ignores loose `.patch_N` files, which is why the earlier round trip exported the stock
model. The tool builds a slim-edition view of hard links to the installed stock files (read-only
use; **never edit them**). It adds one uncompressed DSAR bundle holding the source and candidate
units under unique fake names, plus the Spider Droid materials, exports both through
Filediver's own parser, then compares and renders. Result for v4
(`dist/aim-voice-2026-09-24/validate-v4/report.json`; image paths below are relative to that stage folder):

- 28/28 original primitives: indices, UV0–2, joints, weights and materials exact; positions
  and normals equal up to the rigid transform Filediver applies in place to shared buffers.
  The new UV3 is zero on every original vertex.
- Five added `m_boteye` primitives, 570 vertices / 552 triangles each, rigid weight 1.0.
  The eye resolves to `turret` (65) and the vents to `boss` (38).
- Expected artefact: LODs 15–17 share one vertex buffer, so Filediver remaps the shared marker
  joints three times (38 → 36 → 45). The stored byte, 1, maps to 38 through each LOD's bone list.
- Renders (`markers-v4-preview.png`, `validate-v4/zoom-*.png`) show the bullseye flush on the
  lower-left front of the ball. The vent reticle sits in open air behind the ribbed neck:
  visible from behind, the sides and below, and hidden from the front by the lower pod.

### Local test install (reversible)

`9ba626afa44a3aa3.patch_304` (+ `.gpu_resources`, `.stream`) in the game `data` folder: the next
contiguous index after the installed 0–303. It loads after `patch_192`, and no other installed
patch overrides this unit. Receipt with hashes: `dist/aim-voice-2026-09-24/local-install/markers-v4-receipt.json`.
To remove it, delete exactly those three files, or switch mode / Update pack in the launcher,
which parks unmanaged patch files into `mods_old`. LAUNCH itself does not touch them.

**Gameplay checks still required:**
1. The Spider Droid still renders normally, confirming the engine accepts the UV3 layout change.
2. Both markers are visible, glowing and readable at combat range.
3. The eye marker follows turret yaw; both markers stay aligned while walking and turning.
4. Hits on each marker register as eye/vent weak-point damage.
5. Behaviour after damage and destruction.

Not shipped. To ship: add the patch at the end of the 9ba626afa44a3aa3 recipe with option
`droids` (no renumbering), for both texture profiles, after the checks pass.

**Off the shelf?** Only the weapon half. "Walker weapons" omits the CIS mod's cannon hider and
restores the stock War Strider weapon units. No published mod adds visible weak-point markers.
AyakaMods' WeakPoint Lock-On mods retarget sentries/Guard Dogs instead, and a vanilla-model
highlight mod would override the same unit as the Spider Droid. The off-the-shelf alternative
is to keep the original War Strider model (visible eyes) while the rest of the CIS roster
stays. Custom markers were chosen on 2026-09-24 ("lets do the aim markers").

**Missed weak point:** hashing the `damageable` node names resolves `dmg_eye`, `dmg_eye_top`
and `dmg_l/r_rocketpod`. Players describe the reworked War Strider (Nov 2025) as having front
and top eyes, hip joints, a groin/lower plate and a back vent. `dmg_eye_top` [0, 0.73, 5.07] sits
about 1 m inside the Spider Droid ball (radius 1.77 m). A depth-tested marker there is invisible,
and one projected to the front of the ball is accurate only head-on. v4 does not mark it.
The leg/hull node names did not resolve.

## Fifth in-game voice (regular Clone Trooper, keeping Sev/Fixer/Scorch/Boss)

**Result: a fifth selectable voice cannot be added with asset patches.** It is not universally
impossible, but every route found needs changes to the protected game executable or process
memory. That risks GameGuard/anti-cheat action and is out of bounds.

Evidence, current game 7.1.1 (build 25480438):

1. **Exactly four player voice identities exist.** Each language has four Helldiver voice banks:
   `helldiver_purist`, `soldier_female1`, `soldier_female2`, `soldier_male1` (~790 sounds each).
   The only two unnamed banks in all 479 are small effect banks (11 and 46 sounds), not a
   hidden fifth voice.
2. **The labels match.** The stock English strings (3,246 keys) contain "Default Helldiver
   Voice 1–4", Random and Randomize Voice, and nothing else voice-selectable. "Voice of the
   People" is a cape; the "Voice Pack" strings sit with audio-language and voice-chat settings.
3. **The picker has no data source in the archives.** Its XAML binds to runtime `ScrollList`/`Items`.
   None of the voice label IDs, the four voice-bank hashes or the eight voice-prefix entity deltas
   (prefixes 1–8, component 83) appear in any patchable data resource. Searched: all 300
   entities, 5 hash lookups, 3 configs, the `ah_bin`, the network config, 264 XAML and
   620 prefabs. The deltas exist only in the engine's compiled data library, which Filediver
   ships as pre-dumped blobs.
4. **That data is inside the protected executable.** `helldivers2.exe` is encrypted on disk
   (entropy 8.0 bits/byte) and contains none of these identifiers in plain form.

Selection, saving and multiplayer replication all key off one of the four prefixes, so a new
asset-side identity would have nothing to select it. No existing mod adds a voice either:
Full Clone Voice Conversion (Nexus 11826) and Temuera Morrison's overhaul (12524) replace
the existing slots.

### Decision: keep Boss, fill his gaps with Temuera Morrison (owner, 2026-09-24)

Voices stay Sev, Fixer, Scorch and Boss. Boss's RC recordings are Temuera Morrison, so the
Temuera Morrison Voice Overhaul (Nexus 12524) supplies his missing calls. It is already in
the pack (installed `patch_159`), but loads before Full Clone Voice (`patch_160`), so there it
only wins Democracy Officer and Mission Control. A brief slot-4 Clone Trooper candidate was
built on a misread of this request; it has been removed from the game and the repo.

Boss's slot (`helldiver_purist`, 785 Sounds) before and after, from
`tools/build-boss-temuera-merge.py --out <fresh dir>`:

| Sound | r10 plays | Candidate plays | Count |
| --- | --- | --- | --- |
| RC-mapped | Boss (RC) | Boss (RC), unchanged | 301 |
| Temuera has a line, clone has a line | Battlefront clone line | Temuera | 231 |
| Temuera has a line, clone silent | silence | Temuera | 32 |
| Temuera silent | clone line | clone line, unchanged | 42 |
| both silent | silence | silence | 179 |

"Silent" means a mod's reused silence placeholder: the same payload appears 100+ times in
that mod (Temuera: 374-byte ×833 and 747-byte ×480; clone: 1320-byte ×889 and 1073-byte ×273).
The 263 added streams are 117 distinct Temuera clips; the Temuera author reuses some generic
acknowledgements across events. The rebased banks keep the current game's hierarchy, so a
Sound plays whichever bundle last supplies `content/audio/us/<media>`. The tool therefore only
adds Temuera's stream resources, asserting Vorbis on both the bank and the WEM. Boss's bank,
his 301 RC recordings and all Sev/Fixer/Scorch resources keep their r10 bytes. Levels are not
normalized (Boss was already reported loud).

- Ship form: `rc/` replaces manifest `patch_251` (38.1 MB stream, same name, no renumbering).
- Local test (additive): `patch_305` holds only the 263 streams; it wins all 263 over
  `patch_160`. Receipt: `dist/boss-temuera-2026-09-24/local-install/receipt.json`.
- Gameplay check: Boss still sounds like Boss on RC calls; formerly silent or Battlefront-clone
  calls now use Temuera. Check a few common calls (reload, stratagem, ping, affirmative) locally
  and from a teammate on voice 4.

### Voice gap fill and "Boss/Clone" label (prepared, not published)

`tools/build-voice-fill.py --out <fresh dir>` edits the published r12 Delta bundle (manifest
`patch_251`) and label bank (`patch_252`). It changes only lines that are currently silent
(the Full Clone Voice mod mutes lines it could not match) or Battlefront-clone-voiced.
RC-mapped lines and Boss's Temuera lines are never replaced.

1. Curated RC recordings per character, chosen by exact transcript from the audited
   catalog: duration ≤ 2.8 s, subtitle agreement ≥ 0.8, no uncertain/no-speech flag. Two Sev
   lines failed on subtitle disagreement and were dropped. Categories:
   - target/ordinal pings → "Enemy spotted.", "Eliminate target.";
   - walker ping → "Spider Droid!";
   - "strategy selected" / "loadout confirmed" → "Ready, sir.";
   - sample/resource pickups → "Got it.";
   - "found something" and item pings → "There's something here.";
   - "nice", supplies, equipment, wildlife ("Something's moving."), low visibility and
     fortifications.
2. Still-silent lines whose identical text Temuera voiced in another slot: that Temuera recording.

Result: 318 lines filled (42 distinct RC recordings plus Temuera). Silent lines: Sev 193 → 105,
Fixer 190 → 107, Scorch 202 → 116, Boss 179 → 121. RC fills are PCM, with the bank codec
field set to PCM for exactly those Sounds. Temuera fills are Vorbis. Restoring the codec
fields reproduces each base bank, and all other base resources are byte-identical. Sampled
streams decode with vgmstream.

Still silent, with no clone recording in any available source (both clone voice mods also
mute these): numbers, NATO letters, compass directions, distances, "code is", "deploying
flare", "dropping item", and some legendary-item flavour lines. Voice 4 is relabelled
"Boss/Clone" (keys 3149181118/534138438 only).

### Star Wars audio inventory (unchanged)

11 installed titles inventoried in `dist/aim-voice-2026-09-24/star-wars-installs.json` and
`star-wars-audio-inventory.json`: Battlefront II Classic, Republic Commando, Galactic
Battlegrounds (+ Clone Campaigns), Fallen Order, LEGO Skywalker Saga, Empire at War Gold,
Force Unleashed, Squadrons, Starfighter, X-Wing Alliance and X-Wing vs TIE Fighter. Battlefront I
was not found in the checked locations. Counts are files/containers, not unique clone lines.
Also available: the Full Clone Voice Conversion, whose author cites Battlefront II (2017), plus
Clone Officers, Clone Trooper SEAF and Temuera Morrison ZIPs under `dist/mods`. No new
extraction was done, pending the slot decision.

Primary references: [Filediver schema](https://github.com/xypwn/filediver/tree/v0.7.53/datalibrary),
[clone conversion author page](https://www.nexusmods.com/helldivers2/mods/11826).
