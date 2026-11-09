# EmpireDivers Forge r45 offline build

November 8, 2026

## Delivery and disposition

Candidate folder [empire-forge-r45-v3](empire-forge-r45-v3/).

The candidate contains 32 flat patch triplets, a HellForge community-pack manifest, and five attribution/notes files. Total size is **8,435,020,883 bytes** across 102 files. It selects Full textures, Droid skins, walker weapons and the TIE Fighter. Covenant is off. These are fixed choices, with an empty manifest Options array matching the r19 export's packaging approach. A new GUID keeps the candidate separately identifiable.

**The candidate is an offline review artifact. Overall verification remains failed because two inherited ship helper-material references are unresolved.** All four required archive rules pass. Runtime, rendering, mission behavior and multiplayer acceptance remain pending. The three requested Lua features require re-authoring and are absent from this candidate.

The selected content comprises 6,780 unique typed resources. The build uses retained r41 matching-build content, qualified later asset payloads, both torso75 resources, names72, all 12 callouts64 replacements and all eight voice76 replacements. Evidence includes 869 freshly hashed source files. Candidate files are independently hashed and read back. [Build result](build-result.json), [source pins](source-pins.json), [verification summary](verification-summary.json), [component coverage](component-coverage.json).

## Source authority and revision inventory

The QA sprint report was read first from `D:\ClaudeWork\clonedivers-qa\docs\QA-SWEEP-2026-11-08.md`. The old C-drive checkout supplied read-only recipes, manifests, source assets and reports. No launcher was built from it.

The retained r20–r41 manifests declare **25480438**. r42 declares 25988413, r43 declares 26088801, and the later r44 candidates declare 26251122. Those declarations provided a starting point. Fresh Forge directories and native control bytes supplied the compatibility checks. The full inventory records 29 manifests with hashes and paths. [Revision inventory](revision-inventory.json).

| Revision | Change and implementation class | Forge disposition |
| --- | --- | --- |
| r20 | Phase I helmet visibility and roster assets. Optional camera work is Lua. | Retained through r41 asset lineage. |
| r21 | Expanded Imperial armor, film-scale AT-ST, ships, aircraft, weapons and voices. Assets plus Eagle runtime. | Asset lineage retained. Runtime deferred. |
| r22 | Five effort/injury cues and shared Full/Lighter walker geometry. Eagle support includes Lua. | Matching-build assets/audio retained. Full selection only. |
| r23 | Additive Yoda event/audio and local-LEGO death routing. Asset plus Lua. | Audio lineage retained. Death routing deferred. |
| r24 | Startup archive head, Wwise slot and row-order corrections. Archive serialization. | All output archives freshly serialized and checked. |
| r25 | First-person camera corrections, LEGO, Imperial audio, ship surfaces and Lambda cargo. Assets plus Lua. | Asset/audio lineage retained. Camera deferred. |
| r26 | Trooper, voice, vehicle, ship and camera playtest repairs. | Asset lineage retained. Camera deferred. |
| r27 | 137 persona wrapper repairs and 66 officer flavor recordings. Audio. | Matching-build audio retained and effective codecs checked. |
| r28 | Camera/head-node validation. Lua. | Deferred. |
| r29 | Startup diagnostic disabling framing. | Historical diagnostic. |
| r30 | Startup diagnostic omitting newest ship. | Historical diagnostic. |
| r31 | Startup diagnostic omitting recent armor/capes. | Historical diagnostic. |
| r32 | Restore LEGO/K2 components for isolation. | Superseded asset lineage retained. |
| r33 | Restore shared materials and leg providers. | Dependency-bearing lineage retained. |
| r34 | Restore five recent armor patches. | Superseded asset lineage retained. |
| r35 | Selectable-cape comparison. | Superseded by later cape policy. |
| r36 | Six weapon bounds, Death helmet bounds, articulated Lambda and detailed V8 ship. Assets. | Retained through r41. |
| r37 | Original ship author's cannon visual hider. Asset. | Retained through r41. |
| r38 | 155 radius repairs across 32 UNITs. Assets. | Retained through r41. |
| r39 | LEGO LOD envelopes and dormant camera diagnostic modules. Assets plus dormant Lua. | Asset lineage retained. Lua omitted. |
| r40 | Native cape controls and selective invisible texture. Assets. | Later 111-texture policy imported. |
| r41 | K2/helmet envelopes, native-rig Vader officer and private geometry. Dormant camera adapter/observer. | Matching-build asset base. |
| r42 | New-native audio/text compatibility and removal of unsupported Lua. Build-specific metadata. | Reuse authored payloads through matching Forge contracts. |
| r43 | Compatibility, material/LOD repairs, cape policy, Fighter and ship windows. | Qualifying assets imported. Two lower-leg UNITs retained at r41. |
| r44 through crew66 | LEGO refinements, E-Web, flight effects, green Orbital Laser, Lambda/AT-ST work, names, callouts and crew trial. | Clean asset subset imported. Detailed limitations follow. |
| 69 | Move the same 22 Admiral payloads into native hub archive context. Residency experiment. | Excluded. Consolidation does not establish NPC residency. |
| 72 | Seven remaining TIE name/description edits. Text. | All seven serialized values verified. |
| 75 | Complete Storm chest on two shared torso identities. Asset plus target-native controls. | Both complete payload triplets match the reviewed component exactly. |
| 76 | Eight additional Vorbis acknowledgement STREAMs. Build-specific audio contracts. | All eight complete payload triplets match the reviewed component exactly. |

## What ported

The build carries 316 later authored asset resources beyond the r41 base. They comprise 75 UNITs, 147 textures, 64 resources of type `931e336d7646cc26`, 25 materials, four particle resources and one GeometryGroup. Included source groups cover 38 body LOD repairs, Commander/Snow/Sand LOD repairs, ten Shadow bounds repairs, ship windows, the 111-texture cape policy, LEGO revisions, Fighter/flight effects, green Orbital Laser and two torso75 replacements. Lambda source surfaces also port, while its newer UNIT remains deferred. [Per-resource decisions](port-decisions.json).

Torso75 deliberately replaces the shared Commander chest silhouette with the complete Storm chest on `2d9ce590011788ac` and `18902007139a6060`. The source's cached associations include B-01, B-16, B-22, SA-04, SA-12, SA-25, TR-40 and TR-7. Current Forge ownership, fit, damage and motion still need observation. [Component coverage](component-coverage.json).

Thirteen English tables were rebuilt from Forge's native key sets using candidate values on shared keys. The build reconstructs 1,139 changed STREAM resources with Forge-native wrapper metadata and retained WEM payloads. Audio uses the r41 matching-build BANK/DEP lineage. All 12 callouts64 replacements and all eight voice76 replacements match their reviewed complete payload triplets exactly, including their WEMs. Actual playback, timing and mix remain unaccepted. [Component coverage](component-coverage.json), [port decisions](port-decisions.json).

## Build-specific work and rebuild routes

The inventory indexed **81,228 typed native identities in 3,537 Forge archive families**, including 15 loose families outside `bundles.nxa`. The cross-build comparison covers 6,786 relevant identities. Family sets differ for 96 identities, including 91 present in both native indexes. Exact family hashes are listed per resource. The 15 loose MAIN archives have separate hash pins. [Cross-build families](cross-build-families.csv), [loose native archive pins](native-loose-archive-pins.json).

Absence from Forge's native STREAM index can also describe a private streamed-media override introduced by an authored bank. Such r41 providers remain paired with their matching-build banks. The complete inventory flags five later-native identities absent from Forge. They are BANK `706ca3997063cd94` and STREAMs `d54aad819995e6c7`, `4f5b0ce748966c1f`, `b33bfdfbe1441036` and `cfb136fdb379989c`. Their later replacements are excluded. [Input inventory](input-inventory.json).

| Content | Existing tooling and required Forge adaptation |
| --- | --- |
| Archive layout | `build-player-visuals.py`, `slim-archive-preflight.py` and the new `empire_port.py`. Output uses contiguous type ranges, aligned heads and explicit Wwise slots. Every resource has one provider after consolidation. |
| UNIT visual grafts | `finish-armor-primary-build-v3.py`, `r25-cargo-lambda-build.py`, `swarm-walker-lambda-build.py`, `empire66-crew-private.py`. Feed fresh Forge targets into native-control/rig conversion, then repeat pose and material checks. Five newer UNITs failed the conservative control comparison and retain r41 providers where available. |
| Materials, textures and geometry | Reuse the component builders and payloads when native controls and typed dependencies agree. Follow referenced bones, state machines, GeometryGroups, drawn materials, shader parents and textures. |
| Text | `rc-voice-labels.py` parsing and `swarm-current-text-rebase.py`'s native-table merge approach. Retain Forge keys and rebuild offsets/counts. |
| BANK/DEP | `rebase-player-audio.py` and `hd2-r43-compose.py` provide native-routing/media-ID rebasing. Newer compatibility banks require fresh Forge graphs and codec review. The delivered candidate retains matching-build graphs. |
| Callouts64/76 | `empire64-voice-callouts.py`, `empire76-voice-expansion.py`, and their readback tools. All 20 component payloads are byte-exact in this output. Forge wrapper and effective codec checks pass. A changed target contract would require fresh timing/codec qualification. |
| Admiral69 | `admiral69-archive-trial.py`. Rebuild against Forge's `fc5b6bff0db90aab` hub ownership and test actual Admiral residency. |

The five deferred UNIT identities are Lambda `75be82ed8592a6b3`, AT-ST `79e4b3d2da5e45e3`, lower legs `620f7dcb7f6cd8f2` and `ca672d99456b3ffe`, and crew66 `5a646eee21712c8e`. Crew66's primary UNIT has no r41 replacement in the selected set. Its supporting assets alone do not establish a crew conversion.

### Lua and native-mod blockers

The old Eagle/Yoda addons bind HD2Runtime 0.28.1, Windows runtime adapters, launcher config/receipt hashes and pack versions. HellForge's pinned source requires its bridge-aware runtime and routes Lua edits through that bridge. Bingus Shared Loader v20 supplies addon loading. The old context/runtime assumptions still require adaptation.

* **Camera pullback** needs a qualified active-camera/local-player adapter. r39–r41 intentionally package dormant modules. Their own status reports framing disabled and no hooks or camera motion.
* **Per-attack TIE switching** needs distinct delivery-entity/model ownership and package residency. The inspected legacy gateway performs a solo-Ship strafing-model comparison. The later pack's Fighter/Bomber choice is session-wide. Mixed per-attack dispatch remains implementation work.
* **LEGO Yoda death** needs applied-body identity, local-player death deduplication and positional event playback. Its legacy identity/world/sound adapters and receipt context need replacement with HellForge-compatible bindings.

No native code mod was built. The repository SDK has useful player/stratagem events, while these three features still need their specific camera, delivery, wardrobe or sound bindings qualified. Relevant local authority is `hellforge-src/docs/lua-bridge.md`, `native/mod-sdk/hellforge_mod.h`, `tools/r26-vehicle-camera-entry.lua`, `next-vehicles-runtime-gateway-v3.lua`, `yoda-death-runtime-identity.lua` and `yoda-death-runtime-sound.lua`.

## Static verification and open findings

| Check | Result |
| --- | --- |
| MAIN at least aligned TOC head | 32/32 pass. Writer also reserves the existing tool's eight-byte directory trailer. |
| Wwise offset plus 16 within MAIN | 32/32 pass. |
| Rows contiguous in type-table order | 32/32 pass. |
| STREAM offset plus length within companion | 32/32 pass. GPU and MAIN extents also checked. |
| Typed-key uniqueness and payload readback | Zero duplicates, zero payload mismatches. |
| Vanilla name/type comparison | 4,867 native overrides and 1,913 private typed additions. 819 entries share a vanilla name hash with another type, recorded individually. |
| Audio | 437 effective banks, zero mapped PCM/Vorbis codec mismatches, zero missing custom external media. 2,361 Forge-native STREAM wrappers checked. Two external-media omissions already exist in native bank contracts and are recorded separately. |
| Text | 13 parsed English tables preserve Forge-native keys. All seven names72 edits match. |
| Focused regression tests | Six tests pass, including loose-native-family discovery. |
| Full dependency closure | **Failed on two ship helper references.** |

The unresolved references are thin material `093fc934` in ship UNITs **`0c6567f9e37920e1` and `7dcb6f44af36ada1`**, both in descriptor 0, mesh type 5. These are inherited r41 ship payloads. Fresh corresponding native ships do not prove the same unmapped helper usage. Their rendering significance requires source/engine review or a controlled observation. They remain findings in the verifier. Three other unresolved references were separately corroborated in fresh native controls and recorded as inherited exceptions. [Dependency findings](dependency-findings.json), [native exceptions](native-dependency-exceptions.json).

The complete native scan resolves the earlier 72 external-media findings and confirms BANK/DEP `e8c7da3561bee799` as valid Forge content. Final v3 retains that pair. The intermediate removal ledger is explicitly marked superseded. [Superseded diagnostic ledger](obsolete-audio-repair.json).

File-level comparison against r19 records 1,028 removed names, 101 added names and one changed name. Consolidation replaces the old archive filenames with 32 triplets at slots 1000–1031. Resource-level comparison records 2,575 identical winners, 1,450 changed, 2,755 added and 685 removed. The removed resources are STREAMs superseded/omitted by the selected lineage. [File diff with sizes and hashes](r19-file-diff.csv), [resource diff](r19-resource-diff.json), [all name/type comparisons](name-type-collisions.csv).

## Empire issue map

| Open issue | Implementation route | Candidate status and acceptance scene |
| --- | --- | --- |
| Missing Storm/Commander torso | Asset, owner eyes | Torso75 included. Equip every shared named family in both body types. Inspect ship, ground, sprint, aim and damage. Commander silhouette changes deliberately. |
| Missing K2/Shadow heads, cores or legs | Asset, owner eyes | LOD/bounds lineage included. Actual matching full kits, worn appearance, distance and damage remain open. |
| Sand source quality and armor variety | Asset, owner eyes | Existing source retained. Better Sand source, optional First Order/Sith styles and missing roster pieces need authoring. |
| LEGO arms, giant caps, proportions and grip | Asset and possibly native articulation, owner eyes | Later compatible LEGO assets included. Prior owner evidence still rejects the giant caps/long proportions. Rest/pose compatibility does not accept appearance. |
| LEGO camera pullback | Lua or native mod | Deferred adapter work. Check third-person, aiming, interiors and vehicle entry after implementation. |
| Hidden capes and shadows | Asset, owner eyes | 111-texture policy included. Check Gilded Quill hidden and Foesmasher visible, then other capes, body types and shadows. |
| Lambda wings, ramp and boarding clearance | Asset, native mod for engine ownership | r41 articulated Lambda retained. Newer UNIT deferred. Observe carry, release, landing, wings and boarding. |
| AT-ST carry, cockpit projections, scale and floating variants | Asset, Lua/native camera and attachment logic | Matching-build walker retained. Latest principal UNIT deferred. Test both exosuits independently. |
| Walker gait, world speed, cannon and footsteps | Animation/audio asset, native mod if runtime coupling is required | Matching-build timing/audio retained. Ground walk, turn and fire need video/audio. |
| E-Web barrels, housing, recoil and flash | Asset, owner eyes | Qualifying later assets included. Deploy HMG Emplacement and inspect idle plus sustained fire. |
| Wider weapon models, grips and red bolts | Asset, owner eyes | Existing shared weapons/FX retained. Native weapon gaps need complete donors and first/third-person mapping. |
| TIE strafing versus bombing variants | Lua or native mod | Fixed Fighter selected. Mixed attack dispatch deferred. |
| TIE engines, flyby and cannons | Audio asset, event/native mod if the event is absent | Effective codec closure passes. Actual sortie playback remains open. |
| Ship windows, distant ISDs and ship weapons | Asset, owner eyes | Windows and ship lineage included. Two helper findings open. Inspect arrival, bridge, external fleet and mission views. Green Orbital Laser is included. |
| Voice preview, four personas and NPC dialogue | Audio asset, Lua/native caller repair | Matching-build voices and all 20 callouts64/76 replacements included. Preview caller and broader event coverage remain open. |
| LEGO-specific Yoda scream | Lua/native dispatch plus audio | Routing deferred. Later acceptance must distinguish equipped LEGO from human armor and local from remote deaths. |
| Crew66 and Vader/Admiral appearance | Asset/residency, owner eyes | r41 Vader lineage retained. Crew66 primary and Admiral69 residency trial deferred. Identify the actual NPC before accepting appearance. |
| Hellpod, transition and extraction audio | Audio asset, caller/native mod if missing event | Drop and return can be sampled by the loop. Extraction needs a completed mission scenario. |

## Exact future live test plan

The orchestrator owns staging and live runs. Before any HellForge change, use the mandated closed-session checks, timestamped backup, atomic writes and post-write verification. Resolve or explicitly disposition the two ship helper findings first. Enable one appearance pack at a time and preserve the same gameplay preset for r19/candidate comparisons. Use only **`clonedivers-armory`**.

### Environment and photographed deployment

Run from a normal terminal, or apply the supplied packaged-app Steam-pid correction when applicable. Use the `private` harness preset for listening because `light` mutes audio. Confirm the lab profile's saved audio is enabled. Preselect each lab loadout before its run because deployment uses the saved loadout.

```powershell
$py = 'C:\Users\goode\AppData\Local\Programs\Python\Python313\python.exe'
$harness = 'D:\ClaudeWork\bingus-harness\harness.py'
$env:HD2H_HOME = 'D:\ClaudeWork\harness-home'
$env:HD2H_LOADER_ZIP = 'D:\ClaudeWork\releases\Bingus-Shared-Loader-v20.zip'
$env:HD2H_HELLFORGE_PROFILE = 'clonedivers-armory'

& $py $harness --target hellforge --hellforge-root D:\HellForge --profile clonedivers-armory preflight
& $py $harness --target hellforge --hellforge-root D:\HellForge --profile clonedivers-armory start --preset private
& $py $harness --target hellforge --hellforge-root D:\HellForge --profile clonedivers-armory ship
& $py $harness --target hellforge --hellforge-root D:\HellForge --profile clonedivers-armory shot ship-before
& $py $harness --target hellforge --hellforge-root D:\HellForge --profile clonedivers-armory mission --difficulty 3 --faction terminid --shots
```

Check each command's exit status before continuing. `mission --shots` captures briefing and landed states. It establishes deployment and the equipped kit's initial ground view. Active movement, firing, stratagem use, injury and death require an attended scene or an additional qualified harness action.

After each controlled scene, take named `shot` captures, then run `leave`, `shot ship-return`, and `stop` with the same target/root/profile arguments. Retain the run directory, result JSON, screenshots, modloader log and process-only audio/video. Confirm restored settings and files.

### Scene matrix

| Pass | Mission/state and actions | Screenshot acceptance |
| --- | --- | --- |
| A | Ship arrival and bridge. Inspect main hull, windows, two deferred helper owners and distant fleet. Identify Admiral and crew by location. | Dedicated ship shots. Mission deployment shots provide limited ship context. Owner visual review required. |
| B | D3 daylight Terminid. Full Storm B-01, then each torso75 shared family. Repeat Brawny/Lean with actual equip confirmation. Walk, sprint, crouch, aim and take damage. | Landed shot checks initial worn kit. Additional front/back/side/distance shots check missing parts and damage. Video checks motion. |
| C | Repeat B with Shadow, Death, Commander, Snow, Sand, K2 and matching LEGO kits. Test Gilded Quill and Foesmasher. | Same capture scheme. Record exact equipment names and body type for every failure. LEGO proportion and grip acceptance require owner eyes. |
| D | D3 Terminid with EXO-45, HMG Emplacement, Eagle Strafing Run and Eagle Airstrike. Capture carrier approach/release, walker mounting, idle, walk/turn/fire, E-Web idle/fire and each sortie. Repeat with EXO-49. | Additional named shots show geometry, carriage and camera. Video/audio establish gait, recoil, footsteps and aircraft sound. Deployment shots alone cover initial landing. |
| E | Separate drop with Orbital Laser, Eagle 500kg, 110mm Pods and Smoke. Compare green laser/trails and actual Fighter silhouettes. | Named timed shots show effects and aircraft. Audio/video establish flyby/cannon behavior. Mixed Fighter/Bomber dispatch remains blocked pending implementation. |
| F | Four voice-slot drops with unmuted audio. Preview each voice aboard ship, then record acknowledgement, effort, injury and death. | Screenshots identify selected slot and kit. Audio establishes playback, urgency, cuts and persona. Include all 20 callouts64/76 targets and compare the recorded source phrases. |
| G | After Lua/native implementation, compare equipped LEGO Storm/bikini and human armor deaths, local/remote players, reinforcement and mission transitions. Exercise camera ADS and vehicle transitions. | Screenshots establish armor/camera state. Logs/audio prove one LEGO scream per eligible local death and correct scope. |
| H | Complete a separate attended mission through extraction. Compare drop, transition and extraction audio. | Screenshots provide state context. The current abandon loop cannot accept extraction playback. |

### Stability comparison

Run r19 first, then the candidate with identical gameplay mods and lab loadout. Start with three D11 Automaton cycles per pack at 300 seconds, followed by one photographed D3 scene. After visual/audio review, schedule the longer soak with the orchestrator's clean-exit work.

```powershell
& $py D:\ClaudeWork\clonedivers-lab\drop_loop.py --label empire-r19-control --cycles 3 --difficulty 11 --faction automaton --hold 300 --preset private --out D:\ClaudeWork\clonedivers-lab\results
# Swap appearance packs through the orchestrator's closed-session procedure.
& $py D:\ClaudeWork\clonedivers-lab\drop_loop.py --label empire-forge-r45-v3 --cycles 3 --difficulty 11 --faction automaton --hold 300 --preset private --out D:\ClaudeWork\clonedivers-lab\results
```

The existing wrapper omits `--shots`. Use the explicit photographed deployment sequence for visual evidence. Record in-mission survival separately from teardown crashes/hangs. Inspect raw crash lines alongside `cycles.jsonl` and `result.json`.

## Commands, retained failures and work boundary

Executed with the specified Python 3.13 interpreter and `-B`.

* `inspect_inputs.py` generated the Forge native index.
* `empire_port.py inventory` completed the r19/later-resource inventory.
* `empire_port.py build` assembled final v3 against the complete native index.
* `inventory_revisions.py` located all 29 requested revision/candidate manifests.
* `verify_candidate.py` completed archive, payload, collision, dependency, audio, text and file-diff checks. Exit 1 reflects the two open ship helper findings.
* `python -B -m unittest -v test_empire_port.py` passes all six tests. [Test log](tests.log).

The first draft used vanilla codec expectations over authored r41 bank graphs and was rejected. It is retained under `rejected-r45-audio-v1` with `build-attempt1.json` and its log. Automatic approval review rejected its recursive cleanup with “blocked by policy”. A non-destructive workspace move isolated it. The first fixture test encountered an inaccessible Python secure-temporary directory. A normal workspace fixture directory allowed the tests to pass. That inaccessible `tmpovjrolfo` directory remains untouched.

The initial native scan omitted 15 loose archive families. This incorrectly deferred callouts64/76 and classified a valid bank pair as obsolete in v2. The final scan unions bundled and loose native families. Final v3 restores the valid pair, includes all 20 callout replacements, and reruns the complete checks. The loose-family regression test now guards this case. Intermediate v2 is isolated under `rejected-r45-incomplete-index-v2`. Its removal ledger and old verification snapshot are superseded historical evidence.

No HellForge installation changes, game launches, Steam game-folder access, launcher builds, public feed changes, releases or pushes were performed. No native code mod was produced. This output workspace has no Git repository, so there is no commit. Remaining blockers are the two ship helper references, the three runtime-feature re-authorings, deferred crew/vehicle work, and live acceptance.


## 2026-11-08 dependency-closure finding resolved

**Final disposition: `empire-forge-r45-v3` passes the complete offline verifier (exit 0).** This dated section supersedes the earlier unresolved-helper disposition and its static verification result. The two ship references are resolved as option **(c)**: native-equivalent materialless helper sections. Candidate payloads and all 102 candidate files remain unchanged, independently rehashed against the prior candidate file receipt. No v4 rebuild was necessary.

### Identity and source evidence

`093fc934` is the thin hash of the material **slot name** `lambert1`; its full string hash is **`093fc9345f166874`**. This is not a bound 64-bit material resource reference. The primary Filediver `stingray/hash.go` defines Thin as the high 32 bits of the Murmur64A string hash. Its retained `hashes.txt` and `thinhashes.txt` contain `lambert1`; hashing it reproduces both forms. The corresponding fresh native ship slot is `Default`, full string hash **`cd121f4b2c09f389`**, thin **`cd121f4b`**. Neither candidate `lambert1` nor native `Default` is bound in the respective UNIT material map.

`inspect_ship_helper.py` freshly reread every directory from **3,537 Forge native families**, including loose archives: **81,228 typed identities**, exactly matching the complete 25480438 index. No identity of any type has thin prefix `093fc934`. The same search found none in all four retained Steam-era native indexes (October 21 v1/v2, October 25 and October 29). The selected r41 matching-build source set contains **6,618 resources**, no provider with this thin prefix, and both candidate ship payload triplets are byte-exact r41 inheritances. This run freshly verified **738 r41 source-file pins**. Therefore r45 did not drop a private r41 material provider.

### Section proof and verifier fix

Both affected UNITs, `0c6567f9e37920e1` and `7dcb6f44af36ada1`, use the slot only in descriptor 0: mesh type **5**, group bone **`g_hellpod`** (`fe58f1bb981e0616`), **121 vertices / 600 indices**. This is a one-centimeter planar helper, not the authored ship hull. Descriptor 0 is absent from **every LOD entry** in each candidate and each corresponding native control. The selected descriptor lists are `[6,5], [4,5], [3], [2]`; native lists are identical for the first ship and `[6,1], [4,1], [3], [2]` for the second. All exclude descriptor 0. The first ship has one native provider (`18235e0c9ec0e636`); the second has eight. **All nine native provider payloads were freshly read and passed the comparison.**

Native and candidate helpers have exactly equal vertex-position multisets, unoriented triangle multisets (including multiplicity), and section radius/type/bone/render-frame fields. Their canonical geometry SHA-256 is `beda532ec834bb27bf7a260a22b8ed34ceffab41e10b108fad08cdb2a517ebfb`. Raw buffers differ because the export reordered vertices/triangles and added vertex attributes. The native equivalent itself has no material-map binding for `Default`. Together these facts establish inherited materialless helper behavior and exclusion from the standard LOD selections; they do not require an invented material or a ship remap.

`verify_candidate.py` now records a detailed native exception only when this precise proof succeeds: expected slot-name pair, no material-map binding on either side, exact helper role and counts, no other nonempty section using that slot, descriptor absent from all nonempty LOD tables, bounded buffer/layout reads, and native-equivalent geometry/render fields. **Mesh type 5 is not generally exempted**: other type-5 ship sections are rendered and remain checked. The prior three independently evidenced native exceptions remain; total native exceptions are now five, with zero missing dependencies.

Reproduction and source/payload hashes are retained in [inspect_ship_helper.py](inspect_ship_helper.py), [ship-helper-evidence.json](ship-helper-evidence.json) and [ship-helper-evidence.log](ship-helper-evidence.log). Small test fixtures under `archive-fixture/ship-helper/` preserve the actual native/export MAIN and helper vertex/index bytes; only buffer extents and offsets were rebased to keep fixtures small.

### Validation and handoff

All commands used `C:\Users\goode\AppData\Local\Programs\Python\Python313\python.exe -B` from this workspace:

* `inspect_ship_helper.py`: **exit 0**, complete fresh native directory agreement, five native-index searches, r41 inheritance/source hashes, all nine native ship providers and unchanged candidate file hashes.
* `-m unittest -v test_empire_port.py`: **exit 0; 13 tests passed**. Seven added regressions cover the positive native/export case and rejection of LOD activation on either side, geometry changes, changed section role, rendered reuse of the slot, native material-map binding, and another unresolved slot. [Test log](tests.log).
* `verify_candidate.py`: **exit 0**, `passed=true`, `dependencyClosurePassed=true`, **0 missing dependencies**, **32/32 archive checks passed**, **6,780 resources**, **0 duplicates / payload mismatches**, **437 banks / 2,361 native wrappers / 0 codec mismatches**, **13 text tables**, **0 missing custom external media**. [Verification summary](verification-summary.json), [verification log](verification.log), [native exceptions](native-dependency-exceptions.json).

Changed tooling: `verify_candidate.py`, `test_empire_port.py`; added evidence tool, JSON/log and four small fixture files; regenerated verifier receipts/logs; appended this report section. There is no Git repository in this workspace, so no commit or push was made. Existing CRLF endings in Python files and LF endings in this report were preserved.

The candidate is now ready for the orchestrator's controlled live test. Runtime rendering, animated/independently dispatched helper behavior, mission stability, multiplayer and the previously deferred Lua/features remain unaccepted; this fix changes only the offline classification and preserves the native materialless contract. No HellForge writes, game launches, Steam game-folder access, releases, public feeds or system settings were performed.
