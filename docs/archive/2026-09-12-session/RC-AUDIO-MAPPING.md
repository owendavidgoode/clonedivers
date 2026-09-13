# Historical record — superseded

See [current status](../../STATUS.md). This file preserves the investigation as it happened; installation claims and pending actions below may be obsolete.

# Republic Commando audio mapping investigation

Inspected 2026-09-12. This is an asset and tooling investigation, not an installed voice mod.
No game files, active recipe, or existing voice sources were changed.

**Latest user decision:** manually selectable RC characters are now authorized: Sev on voice 1,
Fixer on voice 2, Scorch on voice 3 and Boss on voice 4, gated by Republic Commando mode.
The automatic-armor investigation below is historical context and is not a requirement of this
new authoring pass. Mapping output is described at the end of this document; game patch construction
and mode integration are handled separately.

## Result

Replacing the four existing player voice slots is supported by existing mods and tools.
Selecting a replacement from the **speaking diver's armor**, with generic clone fallback,
still needs an armor value delivered to the audio system for that same speaker. The inspected
player voice banks do not contain an ordinary Wwise Switch Container to repurpose.
This narrows the asset-only path; it does not prove that adding a selector is impossible.

The existing Clonedivers app deploys files outside the running game. Its armor option selects
installed cosmetic assets; it does not select a speaking character's audio at runtime.
A setting based only on the local player's armor would produce the wrong voices for teammates.

## Installed bank evidence

Extracted with upstream filediver v0.7.51, `--audio-format wwise`. Current game archives use
Slim/DSAR storage, so use current tools. `--raw-format main` alone does not disable audio
conversion: `--audio-format wwise` is the relevant flag for retaining the bank.

| Bank resource | Resource hash | Archive |
|---|---|---|
| `content/audio/Helldiver_Standard_VO` | `b7cc016f2537e3d3` | `f6fb08cc02d24255` |
| `content/audio/us/helldiver_soldier_female1_VO` | `0a39096a51ae9e86` | `76b0a0e66d61aa15` |
| `content/audio/us/helldiver_soldier_male1_VO` | `017f8b366af141f8` | `b98c5b68db4cca84` |
| `content/audio/us/helldiver_soldier_female2_VO` | `498646c6a1ffba95` | `e8e43494a3d38e6c` |
| `content/audio/us/helldiver_purist_VO` | `6c91a47b88d26638` | `ce6b3d08283efc3d` |

All have resource type `535a7bd3e650d799` (`wwise_bank`). Their decoded BKHD version is **154**,
using the current Audio Modder's `BANK_VERSION_KEY = 0x9211BCAC`. Historical documentation
describing version 141 is not the current installation's format.

Validated each chunk boundary and HIRC object boundary to the exact end of its chunk/file.
Saved sizes, SHA256s, chunk layout and object counts in
`dist/rc-upgrade/voice-bank-inventory.json`. Bank files are under
`dist/rc-upgrade/voice-bank-wwise/content/audio/`; the complete named bank inventory is
`dist/rc-upgrade/all-audio-banks.txt`.

The shared Standard bank has 644 Sound, 660 Action, 604 Event, 16 Random/Sequence Container,
713 Actor Mixer, two Attenuation and one FX ShareSet objects. No Switch Container (type 6),
Music Switch (type 12), or Dialogue Event (type 15).

Each US actor bank has 787 Sound, 802 Action, 797 Event, 1,158 Actor Mixer, one Random/Sequence
Container, eight Music Random/Sequence, three Music Switch, two Attenuation and one FX ShareSet.
Female banks have 129 Music Tracks/Segments each; male1 and purist have 134 each.
None has an ordinary Switch Container or Dialogue Event. The three Music Switch objects
are not evidence of an armor selector. Current upstream's `MusicSwitchContainer` parser retains
the trailing decision-tree data as opaque bytes, so group labels were not recovered here.

US banks contain BKHD/HIRC only, without embedded DATA/DIDX payloads. Do not assume that copying
these small `.bnk` files carries the actual voice recordings; stream resources and their
dependencies must be preserved or newly embedded by the authoring tool.

## Established behavior versus unverified behavior

[Helldivers International's author](https://www.nexusmods.com/helldivers2/mods/14183)
documents four independently replaced voice types, with playback local to each listener.
The replacement recording is not sent to the squad. A listener with the patch can hear
replacement recordings when other divers speak, whereas each other listener uses their own assets.
This does not establish that every exertion, grunt or callout is broadcast without distance,
priority or other audibility limits. Exact network payloads and event coverage remain unmeasured.

The intended rule remains:

| Speaking diver's armor | Voice |
|---|---|
| SC-30 | Sev |
| CM-10 | Fixer |
| CE-35 | Scorch |
| DP-11 | Boss |
| Other | Existing generic clone voice |

The rule must be evaluated for the speaker even when the speaker is a remote player. It must
also handle multiple simultaneous speakers, spawning/reinforcement and armor changes. A global
switch based on the listener would fail that requirement. Four unconditional slot replacements
cannot retain a fifth generic-clone identity for other armor without an additional selector.

## Existing RC pack and current tooling

[Florida Man's Nexus 5351](https://www.nexusmods.com/helldivers2/mods/5351) uses original RC
recordings: voice 1 Sev, 2 Fixer, 3 Scorch, 4 Boss. This is a **voice-slot convention**, not an
armor binding. Its author says some callouts are missing and permits updating/reuploading.
The author stopped maintaining it; the page's last update is July 21, 2025. Its old patch was
not downloaded or used here, so its individual HD2 event-to-recording mapping is not yet recovered.

[Audio Modder v1.20.0](https://github.com/RaidingForPants/hd2-audio-modder/releases/tag/v1.20.0)
is a suitable current starting point. The upstream source explicitly selects its version-154
hierarchy parser. Prior releases added Slim support and migration/repair of older patch
references. Migration is a starting point for review, not proof of correct dialogue coverage.

[The modding wiki](https://boxofbiscuits97.github.io/HD2-Modding-Wiki/dev/audio/overview.html)
recommends Wwise 2023.1.7.8574 for conversion to WEM. WAV sources can be imported through
Audio Modder when a working Wwise conversion installation is available.

[Wwise Teller's developer findings](https://github.com/Dekr0/wwise-teller/wiki/Finding)
report tested HD2 source-ID rewiring, adding media, adding Sound/Random-Sequence/Action objects,
and changing hierarchy properties while respecting inheritance and bank layout.
These findings support custom audio graphs; they do not demonstrate a live per-armor input.

## Improved original RC source inventory

The original export already contains **5,587** valid WAV assets from eight voice packages.
The previous **3,135** Delta count is the subset labeled by `Delta_07/38/40/62` package groups.
It misses mission-specific groups containing Delta speakers.

Matched an additional **900** filenames beginning `D07`, `D38`, `D40` or `D62`, corresponding
to Delta identities. Saved a separate, derived inventory, without changing the original catalog:
`dist/rc-upgrade/rc-delta-candidates.json`. Every entry retains package, object, WAV path,
SHA256, identity evidence, and any matching original subtitle records with source file/index.
Filename-only identities are marked for validation. The game's subtitle files independently
identify these prefixes as Delta 07, 38, 40 and 62.

| Character | Group-labeled WAVs | Additional prefix candidates | Total candidates | With original subtitle text |
|---|---:|---:|---:|---:|
| Boss | 445 | 391 | 836 | 682 |
| Fixer | 943 | 181 | 1,124 | 1,046 |
| Scorch | 847 | 186 | 1,033 | 953 |
| Sev | 900 | 142 | 1,042 | 962 |
| Total | 3,135 | 900 | 4,035 | 3,643 |

Subtitle source: the installed RC `GameData/System/subtitles*.int`, read as Windows-1252,
joined by `SubtitleSound` basename. Multiple subtitle records are retained, not silently chosen.
Counts are files, not deduplicated recordings or guaranteed unique semantic lines.

For example, `Character_Voice.Delta_07.D07X4192` maps to a reload callout and `D07X4083` to
bacta application. That permits direct semantic selection from original scripts for most clips,
with listening reserved for quality, tone, uncertain labels and the untranscribed remainder.
Do not assign recordings by ordinal position within the exported files.

The export logs also name RC SoundMultiple categories such as reload, grenade throw,
enemy spotted, damage and order acknowledgement. Their `.wav` exports are empty selector objects,
not playable recordings. Their child lists were not recovered in this pass; export-log adjacency
alone is insufficient to prove which recordings belong to a selector.

## Concrete implementation sequence

1. Resolve the speaker-to-armor input with the parallel routing investigation. Demonstrate one
   callout changing for two speakers using different armor but the same selected voice slot.
   Also test the same armor with different slots. This distinguishes armor routing from a
   disguised slot replacement before building thousands of replacements.
2. Recover Nexus 5351's semantic mapping if its downloadable patch is available, importing into
   a current clean workspace. Otherwise label current US Event/Action/Sound IDs from current
   audio and community labels. Treat old IDs and cross-actor ordering as untrusted until checked.
3. Create an explicit event manifest: HD2 archive, bank, event/action/container/source IDs,
   semantic trigger, character, RC object/WAV/hash, timing/gain, and missing-line fallback.
   Use the 3,643 subtitle-associated candidates to choose semantically matching dialogue.
4. Build a small representative set: reload, grenade, enemy ping, affirmative/negative,
   reinforce/revive, stim, friendly-fire and damage/death. Preserve the existing generic clone
   branch for non-commando armor and missing RC lines if the routing proof supports that branch.
5. Encode WEMs and alter current bank graphs/media with supported tooling. Audit resource-level
   conflicts against Full Clone Voice Conversion and Temuera VO before recipe integration.
   Do not replace whole shared banks indiscriminately.
6. Test locally and with a second listener: overlapping speakers, remote callouts, same voice
   slot/different armor, same armor/different slots, random voice, reinforcement and optional
   commandos disabled. Each client still needs the patch to hear its replacements.

The source inventory and authoring path are ready for implementation. The automatic armor
selector and full replacement voice pack are not implemented or validated in game.

## Follow-up: automatic routing proof sources and fallback audit

The user explicitly rejected unconditional four-slot replacements. No such patch was built.
Republic Commando mode must gate the feature, and speaker armor must select each identity.

`tools/rc-voice-stage-proof.ps1` now stages eight original, subtitle-confirmed WAVs: affirmative
and negative for each of the four commandos. It uses `mp_voice` objects with character prefixes
`D07/D38/D40/D62` and suffixes `ZZ012/ZZ013`. Both source and copied SHA256s are checked.
Output: `dist/rc-upgrade/rc-voice-routing-proof/manifest.json` plus per-character WAV folders.
The manifest deliberately leaves HD2 event/source IDs unresolved rather than inventing a mapping.
These are small, ready-to-use sources for a two-speaker armor-routing test, not an installable patch.

`tools/rc-voice-audit.ps1` reads extracted source patch index tables and checks their boundaries.
The audit of the recovered source ZIPs confirmed:

| Source | Resources | Player/shared voice banks |
|---|---:|---:|
| Full Clone Voice Conversion | 3,794 | 5 |
| Temuera VO source | 4,526 | 15 total banks, including those same five |

All **3,794** Full Clone Voice resources overlap Temuera: five banks, five legacy audio companion
resources and 3,784 streams. This independently matches the archived r6 conflict report. The five
bank identities are exactly those listed above, including `Helldiver_Standard_VO` for shared sounds.
The source ZIP filenames/patch numbers do not define the current live deployment order.

Saved the resource-level overlap list and source patch hashes in
`dist/rc-upgrade/rc-voice-conflicts.json`; extracted sources are under
`dist/rc-upgrade/rc-voice-sources/`. An armor-conditional implementation must preserve the existing
clone branch within the affected banks or route to retained clone sources. Simply disabling the
entire clone patch would also expose overlapping Temuera content and would not implement the
requested generic-clone fallback. NPC-only Temuera banks should remain outside the RC voice change.

### Current affirmative/negative event IDs resolved

`tools/rc-voice-trace-events.py` now resolves **23** labeled media variants in the current US banks:
12 affirmative and 11 negative. Each is linked by the actual bank graph from Event to Action
to Sound to WEM media ID. All 23 referenced media IDs exist in the installed version-154 banks,
and all 23 Actions directly target the corresponding Sound. No ordinal or cross-actor alignment
was used. The machine-readable mapping includes bank and transcription-file SHA256s:
`dist/rc-upgrade/rc-voice-current-event-map.json`.

Semantic labels come from the original contributor's
[four-voice WEM transcription tables](https://gist.github.com/trashguy/a25afb2612dd56c05ac61d6d8f40d579),
saved with GitHub revision metadata under `dist/rc-upgrade/hd2-voice-transcripts/`.
These transcriptions contain errors elsewhere. Exact affirmative/negative labels are a focused
test set, not a guarantee of complete communication-wheel coverage or a substitute for playback
validation. Other responses such as yes/no are deliberately outside this pass. The audio tool's
`friendlynames.db` v17 was also inspected: it labels soundbanks only, not individual voice events.

Representative current paths, with all variants retained in the JSON:

| Native voice | Meaning | Event | Action | Sound | WEM media |
|---|---|---:|---:|---:|---:|
| 1 | Affirmative | 3648040867 | 557789182 | 622562327 | 257892485 |
| 1 | Negative | 2043371713 | 373032739 | 915214292 | 498692213 |
| 2 | Affirmative | 2762725583 | 277456803 | 570857576 | 18901217 |
| 2 | Negative | 2646295864 | 45587004 | 76470490 | 79381450 |
| 3 | Affirmative | 304609495 | 161698749 | 521616896 | 149056516 |
| 3 | Negative | 2563107619 | 122781817 | 800002812 | 431035691 |
| 4 | Affirmative | 2115053461 | 401667530 | 325680508 | 563928657 |
| 4 | Negative | 3835064671 | 671178708 | 616275252 | 134865610 |

The staging script accepts the current event map and now produces `hd2_targets` arrays rather
than a single unresolved ID. Rebuilt into a separate directory to preserve earlier staging:
`dist/rc-upgrade/rc-voice-routing-proof-mapped/manifest.json`. Each commando's affirmative WAV
has all 12 native affirmative targets and its negative WAV all 11 negative targets, spanning
**every native voice type**. This supplies a concrete routing experiment without making the
rejected assumption that selecting a particular native voice slot selects a commando identity.
The per-speaker armor input remains unresolved; no game patch was built or installed.

## Selectable voices: broad original-dialogue authoring map

The latest approved implementation uses the four manual voice slots. Created
`tools/rc-voice-map.py`, which emits `dist/rc-upgrade/voice-slots-map/mapping.json`.
The map matches exact original subtitles first, then applies explicit semantic rules. It does not
assume that different voice actors' media appear in equivalent index positions.

| Slot | Character | Mapped actor-bank targets | Existing generic-clone fallback |
|---|---|---:|---:|
| 1 | Sev | 405 | 380 |
| 2 | Fixer | 418 | 367 |
| 3 | Scorch | 372 | 413 |
| 4 | Boss | 293 | 492 |
| Total | | 1,488 | 1,652 |

The denominator is 785 transcribed actor-bank targets per native voice, not every sound used by
the game. The map selects **322 unique original recordings**. Every WAV exists and its SHA256
matches the source catalog; the longest chosen recording is 2.862 seconds. Per-row metadata
includes original object, WAV path/hash, duration, rate, channels, sample width, source subtitles,
semantic rule, original selector membership, and alternative recordings in the same source pool.
Bank identity and HD2 media/resource hashes are provided for independent current-layout validation.

Coverage includes affirmative/negative, movement, reload, grenade, terminal interaction, damage,
healing, spawn readiness, enemy sightings, battle cries and resource collection acknowledgements.
Some substitutions deliberately generalize the original line while retaining its purpose: resource
acquisition can use a short acknowledgment; Boss reloads request cover because the original Boss
package lacks the squadmates' Reloading selector. Such adaptations are labeled semantic matches,
not literal transcript matches. Coordinates, unsupported equipment/stratagem announcements and
unclear automatic transcriptions remain generic-clone fallback. This is not complete RC replacement.

Recovered **551** original Delta SoundMultiple selectors by exporting object properties as `t3d`:
`UCC.exe batchexport character_voice.uax SoundMultiple t3d <output>` in the disposable export
runtime. These properties explicitly list each selector's Sound children, solving the previously
unresolved selector membership problem. `tools/rc-voice-map-selectors.ps1` reproduces this export
without changing either game installation. Saved objects are in `dist/rc-upgrade/rc-selectors/`.

For example, `D07_Reloading` explicitly references `D07X4188` through `D07X4192`; Boss's
`D38_IncapacitateGroan` references `D38X4976` through `D38X4980`. Actor-bank injury lines use
authentic character-specific Hurt selector clips even when those clips have no spoken subtitle.
The separate shared `Helldiver_Standard_VO` bank remains untouched because its exertion-to-slot
attribution has not been established. The mapping tool leaves every unmatched target explicit
rather than silently borrowing another commando's voice or making up a callout.

Run the map builder with the existing local Python via `uv`; it only reads RC sources and writes
the authoring JSON. The separate patch builder must validate each current bank's source layout,
media ID and resource hash, encode the selected WAVs, preserve unsupported targets, and report
its actual supported coverage. In-game timing, playback and all four menu selections still require
validation after packaging. No live/app/recipe changes were made by this mapping subtask.
