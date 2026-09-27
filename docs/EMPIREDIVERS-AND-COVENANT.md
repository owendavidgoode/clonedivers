# EmpireDivers and Covenant

The owner requested EmpireDivers and an optional Covenant conversion together, without added music. Work resumed from Claude session `492261cc-3794-4c69-8b1b-abcfa0c04552`. After the AT-ST repair, the owner explicitly authorized release without gameplay acceptance: “just assume it will work and ship 1.7”. This is authorization, not evidence of runtime compatibility.

## Scope

- **EmpireDivers** is a third universe: B-01 Tactical Stormtrooper armor on **Brawny**, Helldiverized Bloodhound armor on **Lean**, Star Destroyers, a gray LAAT, and AT-ST Patriot/Emancipator exosuits.
- Since [r18](releases/pack-2026.09.27-r18.md), the full shared setup includes all 90 scope/ADS archives, blaster models/sounds/effects, backpacks, stratagem/sentry audio, Y-Wing, LAAT/c transport, TX-130/Supply FRV, Falchion Bastion, GNK hellbomb and cape changes. The original r15 shared list missed the later scope build and much of this equipment. Droid skins and JohnsonPotatoMode remain independent options.
- **Covenant squids**, off by default, works in both modded modes. Core Illuminate models and voices change; newer units stay vanilla.
- EmpireDivers excludes Clone/Commando armor and voices, the Republic opening, Venator, and AT-TE models/animations. Other armor, player/pilot voices, and Lumberer are vanilla; Bastion uses the shared Falchion since r18. Since [1.7.1 / r17](releases/v1.7.1.md), Empire opens with the requested Helldivers vs Star Wars battle video and matching soundtrack. Clonedivers retains its Venator intro. No Imperial voice set is claimed.
- Returning to Clonedivers with Covenant off reproduces r14's content plus the [r16 SAI sound/blue-bolt bundle](releases/pack-2026.09.26-r16.md). The original r15 reproduced r14 exactly. Helldivers parks every mod. Mode changes preserve independent options and reuse verified downloads.
- New Empire predicates use format-3 metadata. Since 1.7.1, older launchers receive a separate compatible profile feed so unsupported Empire predicates cannot hide the executable update. **Update the launcher first.**

## Sources

| Source | Selected content | Pinned archive SHA-256 |
|---|---|---|
| [AT-ST v2.1, GMrecreation](https://www.nexusmods.com/helldivers2/mods/5745?tab=files&file_id=29306) | Two walkers and four weapon hiders | `a5832895f98356d8bd288b2b1935f7a06518da180b78c0e680645377193668fa` |
| [Covenant v1.34, ArcanePoro](https://www.nexusmods.com/helldivers2/mods/1670?tab=files&file_id=54935) | Core conversion and five banks | `fe3e2727ed13dea3910c23270bdefa8e9ccb2e7ba52885aeb9e05c46ae8569da` |
| [Stormtrooper AIO 1.1A, Dega](https://www.nexusmods.com/helldivers2/mods/2154?tab=files&file_id=52906) | B-01 meshes and textures | `ad49fffe49df18cd3201496219057138ba197efabd554a4493f0fb3816f7efeb` |
| [Helldiverized Stormtrooper 1.1, Derry Wong/qwrpy](https://www.nexusmods.com/helldivers2/mods/10666?tab=files&file_id=54395) | Lean Bloodhound armor and helmet | `0dcc043239649bed8fee9c051cb12ab2359b53bd21b13ee1fed4ee77eff0dc77` |
| [Star Destroyer 1.0.5, ExplosiveGeek](https://www.nexusmods.com/helldivers2/mods/3949?tab=files&file_id=58722) | All ships | `130953af44cdc1f6598dc33faaaf46cef1a7a13f8eee53c6c2bcbbd1f798ffba` |
| [LAAT 1.2, thebf333](https://www.nexusmods.com/helldivers2/mods/5778?tab=files&file_id=62049) | Gray Skin; existing pack meshes/fixes reused | `792620a1a2b4c2d5965d7d97838e386a17607f52dbf768acee4e46f6e1c01ec9` |

The current LAAT archive calls its neutral livery **Gray Skin**, not Imperial. The Stormtrooper author reports shoulder deformation. Models do not move gameplay hitboxes; the Covenant author advises aiming at Elite jaws for the original Overseer headshot zone.

## Asset work

`build-atst-candidate.py` grafts donor geometry onto current walker metadata, remaps gun joints/mesh references and updates legacy vertex-format enums. Native physics, rig and state-machine metadata remain intact; GPU payloads are unchanged. The Emancipator uses the shared native walker body. Its runtime variant behavior remains unverified.

`build-covenant-candidate.py` merges 218 resources in author order and rebases five banks onto current sound routing/dependencies, retaining 1,203 changed embedded samples. Covenant disables the Watcher probe audio bundle so the observer bank has only one replacement.

`build-empire-release.py` selects pinned visual inputs, rejects differing resource collisions and unexpected nonvisual resources, and builds a 128-resource Imperial bundle. Shared files use explicit source groups. New Imperial/Covenant textures retain the same source detail in both profiles; no extra reduction is claimed.

## Evidence and limits

- Native tests include cached Empire → Clones → Empire installs, vanilla parking, receipt-based selection and independent Covenant preference.
- `tools/Faction.Check` passes 16 option/profile checks: exact r14 Clone baseline, Imperial inclusion, Republic exclusion, gap-free names, Covenant/Watcher isolation and preserved preferences.
- Filediver 0.7.53 decoded 51 Imperial models, 13 Covenant models and two AT-ST body variants. Four AT-ST weapon hiders have zero triangles and passed structural checks.
- Geometry checks cover buffer bounds, finite positions/UVs/weights and index bounds. Filediver divides by zero on some intentionally empty donor meshes; only the temporary export copy omits those meshes. Reports enumerate omissions. Candidate bytes are untouched, and texture-usage warnings remain.
- Release staging hashes both default profile source directories and seals the executable, feeds, assets and notes before publication.

Startup, animation, aiming, weapons, entry/exit, destruction, sound playback, hitboxes and multiplayer appearance remain unverified at the owner's request. The running game and launcher are not changed by publishing.

## Local build receipts

Under `dist/empire-covenant`:

- `current` and `covenant-current`: raw current-game walker/audio inputs.
- `atst-v2/report.json` and `preview/feed/report.json`: AT-ST and Covenant build receipts.
- `atst-checked/report.json`, `covenant-checked-v4/report.json`, `empire-checked/report.json`: geometry reports.
- `release-inputs/manifest.json`, `release-inputs/report.json`, `release-inputs/assets`: r15 inputs and provenance.
- `release-inputs/faction-check.log` and `1.7-tests.log`: selection/native tests.

Use bundled uv with Python 3.11, NumPy/Pillow, Filediver and .NET. The build chain is `build-atst-candidate.py` → `build-covenant-candidate.py --atst ...` → `build-empire-release.py`; each provides `--help` and requires a fresh output directory. Default Clone source folders remain `dist/release-r14-inputs/directories.json`. Publish through `tools/release.ps1`, preserving old asset releases and the compatibility feed.
