# Player customization follow-up — September 20, 2026

Player requests: investigate broken scope/ADS, independently toggle CIS enemy skins
and aiming points, increase ordinary clone armor variety, and limit Commando helmets
to exactly four equipment choices. Work below is local. Public 1.5.0/r9 and the
installed game have not been changed.

## September 23 candidate assembled

The earlier investigation notes below are historical. Current staged output is
`dist/player-update-2026-09-23/feed-7.1-final/manifest.json`; the public r9 feed and installed
pack are unchanged. The local launcher executable is in the sibling `launcher/`
folder, built with version `1.6.0-preview`. This is not a release download.

- **Combined roster:** two universe cards, Helldivers and Clonedivers. Commando
  equipment stays available with either voice preset. Existing preferences survive.
- **Delta Squad:** on selects the RC voices and intro; off uses the underlying
  regular clone voices and intro. The tooltip does not reveal the film. Four native
  voice slots remain: Sev/Fixer/Scorch/Boss or Clone Trooper 1–4. This is a listener's
  preset, including voices heard from teammates, not a fifth networked voice or
  automatic armor routing. Boss gain remains unchanged as agreed.
- **Scope candidate:** downloaded [Custom Scopes v7](https://www.nexusmods.com/helldivers2/mods/5114?tab=files),
  selected matching default reticles, clear lenses and no casings. The builder
  retains original archive ownership, resolves selected overrides in order, and
  excludes all 12 old scope-removal bundles (262–273). Other attachment removers
  remain. 288 resources in 90 archives; no gameplay claim yet.
- **Droid skins:** switches the 33 CIS visual bundles, independently of voices.
- **Walker cannons:** shows the original Factory Strider/MTT cannons by suppressing
  the cannon hider. This is the first aiming-visibility test, not a War Strider
  weak-point fix. Detached/obscured cannons would require the original-walker
  fallback or a geometry repair before release.
- **Four Commando helmets:** removes each original Delta helmet unit from its body
  mesh bundle and keeps one independent target per character. Sev moves from the
  shared base B-01 helmet to CM-09 Bonesnapper; Fixer, Scorch and Boss retain the
  three independent B-01 variants. The base B-01 and formerly duplicated helmet
  targets return to ordinary clones. Shared armor-name labels are no longer
  overwritten. The 7.1 text rebase preserves all current keys and intentional
  themed text, then changes only eight keys between voice presets.

### Mixed clone armor candidate

The original deployed recipe put Body Expansion after Material Reset. The author's
manifest includes Body Expansion first, then Material Reset, helmets and legion
textures. Expansion was therefore overriding the body material that accepts legion
colors. The candidate restores the author's order with the same original asset
bytes in both profiles.

The new roster forks materials and only the needed legion textures into separate
resource IDs, and changes unit material references. It preserves geometry, rigs,
shaders and texture pixels. Shared body-unit groups receive one style together;
it does not present a global recolor as per-player choice. 115 units change. These
are source-verified intended mappings, still awaiting an in-game appearance check:

| Legion | Body armor groups |
| --- | --- |
| 501st | Remaining ordinary clones |
| 212th | CM-09 Bonesnapper, AF-02 Haz-Master |
| 104th Wolfpack | CE-74 Breaker, FS-38 Eradicator, B-08 Light Gunner |
| Coruscant Guard | DP-00 Tactical |
| 327th | CE-81 Juggernaut, FS-34 Exterminator, IE-57 Hell-Bent, UF-50 Bloodhound |
| 41st Camo | CE-27 Ground Breaker, CW-9 White Wolf, I-92 Fire Fighter |
| 187th | DP-53 Savior of the Free |
| White regulars | DP-8 Mountain-Scaled |

Corresponding helmets use those colors where their target is independent. The
standard CM-09 helmet is reserved for Sev; do not tell players it is the orange
helmet. The AF-02 and another CM-09 kit record share an orange target, but ownership
and menu visibility of that alternate kit are not established. No unlocks change.
Commando bodies retain DP-11/Boss, CM-10/Fixer, CE-35/Scorch, SC-30/Sev and require
Brawny. Their four helmet targets are excluded from the legion patch.

The full roster patch adds about 320 MiB of files. Its Lighter variant streams 17
textures without resizing, reducing that bundle's resident GPU file from 316 to
215 MiB and adding 106.7 MiB of stream data. These are file-layout measurements,
not measured runtime VRAM or FPS improvements. New unique candidate assets across
both profiles total about 1,135 MiB after the 7.1 audio rebase; each player selects
one profile. This is the staging asset total, not every player's download size.

### Validation and reproducibility

- Native launcher tests pass, including combined-mode migration and preservation
  of voice/droid preferences. A clipped mode-card layout found in preview was fixed.
- `tools/PlayerUpdate.Check` passes all 16 Delta/droid/cannon/profile combinations:
  complete armor coverage, scope dependencies, correct predicates, unique targets,
  gap-free numbering, original unrelated payloads and SHA-verified new assets.
- The voice expansion checker passes: 496 distinct recordings, 1,525 mapped media
  entries, protected fallback lines retained. No gain change.
- New asset repackers verify resource payload round trips. The Lighter optimizer
  verifies its output against the full source; the feed builder checks both
  inventories against the optimizer receipt before including them.
- The installed game is now build 25327279. The latest official extractor's
  v0.7.53 embedded equipment definitions contain 411 kits and preserve the four
  unique helmet consumers. This is updated extractor metadata, not a live ownership
  or game-process query. The current slim-game archive catalog confirms all 90
  scope archive targets exist; checking loose filenames alone is insufficient.
- No mod was installed, game launched or public release changed during this build.
  The candidate retains the old pack's tested-build marker until gameplay testing.

Build entry points: `tools/build-player-visuals.py`, `tools/build-clone-roster.py`,
`tools/optimize-verified-pack.ps1`, `tools/rebase-player-audio.py`, and
`tools/build-player-update.py`. See [the 7.1 report](HD2-7.1-COMPATIBILITY.md) for
serialized audio validation and preserved current game routing.
The manifest builder requires both `--roster` and `--roster-lighter` together.
It also requires `--current-strings` and `--audio-rebase`.
Final paths are `roster-full-v2/`, `roster-lighter/`, `visuals/`, `audio-7.1-v4/`, and
`feed-7.1-final/` under
the candidate directory. `roster-full/` is an incomplete, superseded helmet-only
experiment and `feed-initial/` predates the roster/load-order correction; neither
should be installed or published.

The feed uses loopback asset URLs on port 8767; serve only its `feed-7.1-final/` directory
when preparing the local play-test. It requires the new launcher. Do not publish
the loopback feed or send it to players. Before a public release, stage hosted
assets, update matching launcher metadata and run the normal release checks.

Next acceptance pass: ADS on several scope/iron-sight weapons; ordinary clone
body/helmet colors; four Commando helmet slots; Delta off/on with a teammate's
voice; MTT cannon visibility/alignment; Full/Lighter on the weakest machine.

## Droid switch implemented locally

The launcher now renders optional pack extras separately from universe and texture
choices. Extras can be selected before installation; changes to installed or parked
packs use the normal verified installer and preserve vanilla mode. Cached changes
do not prompt unless a download or low disk space requires attention.

`tools/build-customization-candidate.ps1` creates a fresh format-3 candidate from r9.
The ownership report is checked against its deployment receipt. Exactly 33 CIS
bundles and all their Full/Lighter companions receive the default-on `droids` gate.
The switch includes the CIS pack's enemies, vehicles, structures, visual helpers,
and removal patches (including hidden original weapons/lights). Enemy audio remains
independent. It does not affect clone armor, player weapons, voices or the RC intro.

Candidate: `dist/customization-2026-09-20/manifest.json`. There are 165 gated manifest
entries across both profiles, with zero new asset bytes or URLs. Droid-on exactly
matches the existing r9 selections. This candidate requires the locally modified
launcher; released 1.5.0 does not render these extra buttons. It is not a release feed.

| Universe | Profile | Files on | Files off |
| --- | --- | ---: | ---: |
| Clonedivers | Full | 754 | 661 |
| Clonedivers | Lighter | 756 | 662 |
| Commandodivers | Full | 788 | 695 |
| Commandodivers | Lighter | 790 | 696 |

Validation: the native launcher suite passes, including pre-install choice, default
display, universe independence, stale-control removal and preview setting isolation.
`tools/Customization.Check` checks the actual candidate's ownership, unchanged asset
identities, all eight combinations, gap-free numbering and unchanged remaining
payloads. Disposable installer fixtures pass cached off/on and parked transitions
with no downloads. The actual launcher preview was rendered and visually inspected.
Gameplay acceptance remains pending.

```powershell
./tools/build-customization-candidate.ps1 -OutDirectory <fresh-directory>
./dist/dotnet-sdk/dotnet.exe run --project tools/Customization.Check -- manifest-v3.json <fresh-directory>/manifest.json build-inputs/r8/deploy-report.json
./dist/dotnet-sdk/dotnet.exe run --project tools/Launcher.Preview -- --manifest <fresh-directory>/manifest.json --render <fresh-directory>/launcher.png
```

## Scope / aiming investigation

The current recipe installs the attachment remover only. The [Clone Blasters author](https://www.nexusmods.com/helldivers2/mods/6633)
also lists Lens only for functional invisible scopes. That separate component is
absent from our recipe and local mod cache. This is a strong lead, not a confirmed
diagnosis. On September 21 the player clarified that nearly every weapon has a
missing or blurry scope, strengthening the shared-dependency lead. Previous wording that the reticle
remains intact was not backed by a completed first-person gameplay check.

The old [Lens only page](https://www.nexusmods.com/helldivers2/mods/6402?tab=files)
redirects users to [Custom scopes and Lens only](https://www.nexusmods.com/helldivers2/mods/5114).
Its current main file is Custom Scopes AIO - Optional no casing, file version 7,
uploaded August 24. Any candidate must select the lens and matching reticle, inspect
overlaps with the remover, and put the winning functional ADS assets in the correct
load order. Installing both indiscriminately can let the remover erase the repair.
No new scope asset has been downloaded or claimed fixed.

The September 21 review of the task **Expand lore beyond bots** resolves the
aim-point context: players complained about Factory Strider/MTT and War
Strider/spider-droid weapons and weak points, not HUD crosshairs. That discussion
proposed first testing removal of `Hide Factory Strider and Vox Machine Cannons`.
Restored cannons might remain obscured or look detached; they do not move hitboxes
or fix War Strider geometry. The fallback is independently restoring the original
Factory and War Strider models and their related weapon/VFX assets while keeping
the rest of the droids. Custom visible weak-point geometry is a separate, unproven
asset-editing route. No such repair is implemented or gameplay-validated yet.

## Commando helmet scope

The original four Delta helmet overrides remain in the AIO assets. The starter
override additionally targets four B-01 unit IDs. In the cached customization map,
the combined eight unit IDs reach 31 helmet kit records, including recolors and
records that need not appear as separately owned menu entries.

The base B-01 unit `bc20d0b4efff128c` alone has 17 kit consumers, including SA-25,
B-22, B-16, TR-7 and TR-40. B-01 variants 2–4 each have one consumer. The original
SC-30 helmet resource also has multiple consumers. This explains the player's
report; it is not an absent-download problem.

To produce exactly four: remove the original Delta helmet overrides while retaining
their bodies and needed materials/skeletons; retain the three independent starter
targets; either move Sev to an independent owned helmet or prove a kit-specific
visual mapping for base B-01. Simply restoring the shared base helmet would remove
Sev from his intended starter slot too. No kit-specific remap is currently proven.
No asset patch has been installed while that choice remains unresolved.

Evidence: `dist/rc-upgrade/starter-commandos/all-kits.json`,
`helmet-patch/variant1-shared-helmet-consumers.json`, `RETARGET-FINDINGS.md`, and
`tools/rc-armor-retarget-helmets.py`. Revalidate against the current game build before
shipping a new target mapping; these are cached extracted definitions.

## Ordinary clone variety

The recipe currently selects Phase 2 501st globally. Local recovered source ZIPs
contain the selected deployed folders, not the complete alternative legion catalog.
The original Arsenal source cache is empty. New styles require obtaining and
auditing the source options, or building verified independent material mappings.

Pending choice: mixed styles across distinct in-game armor items, or a global
legion/phase selector. A global selector does not create a diverse mixed squad on
one player's screen. Mixed styles require auditing shared materials and kit targets
so one texture override does not recolor every armor. Retain the user's four
Commando body choices and keep ordinary clone changes independent of RC mode.

September 21 shortlist for user selection: white regulars, blue 501st, orange
212th, gray Wolfpack, red Coruscant Guard, green/camouflaged 41st, yellow 327th,
and purple 187th. These are proposed visual choices drawn from the author's
[announced legion catalog](https://www.nexusmods.com/helldivers2/images/1975),
not verified downloadable options in our recovered source. Recommend a mixed
Phase 2 roster assigned across armor items, with accessory variation where
supported; a global legion selector alone recolors the local view rather than
giving each squad member an independent legion. Phase 1 is another announced
style option. Obtain the full source and audit independent material targets before
promising any particular mixed roster or in-game armor mapping.

## Combined roster and voice follow-up — September 21

The user supports combining Clonedivers and Commandodivers equipment so players
can mix armor, and asks for a regular clone voice alongside Delta Squad. The current
implementation replaces four native voice choices (Sev/Fixer/Scorch/Boss). There
is no proven fifth selectable choice. Keeping generic clone audio in one slot is
feasible but displaces one commando; an independent generic/Delta launcher preset
is another possibility. Different per-client slot mappings change how remote
players sound to that listener, so such presets must not be presented as a fifth
networked character choice. No commando has been removed or reassigned.

The user reports Boss is too loud. `tools/audit-rc-voice-levels.py` verifies the
release mapping lock and source hashes, checks PCM identities, and measures all
322 selected recordings with FFmpeg. Report:
`dist/rc-upgrade/voice-level-audit-2026-09-21.json`.

| Character | Distinct selected PCM recordings | Replaced game media entries | Median source LUFS |
| --- | ---: | ---: | ---: |
| Sev | 92 | 405 | -10.69 |
| Fixer | 89 | 418 | -9.04 |
| Scorch | 89 | 372 | -9.37 |
| Boss | 52 | 293 | -9.68 |

Total: 322 unique PCM recordings reused across 1,488 game entries. The remaining
1,652 transcribed actor-bank entries retain generic clone audio; shared exertions
also retain their existing audio. PCM encoding currently applies no loudness
normalization. Source measurements do not model bank/bus gain, radio processing,
or gameplay frequency. They do not establish that Boss sounds balanced in-game;
compare final playback and fallback lines before setting gains. No audio modified.

The historical **900** refers to additional filename-prefix candidates beyond
3,135 group-labeled Delta files, not 900 shipped lines. The resulting 4,035
candidate files have 3,988 distinct PCM payloads. The additional batch has 900
distinct file hashes, but alternate takes can share wording. Likewise, 322 unique
selected recordings does not mean 322 unique sentences. Expanding coverage needs
semantic matching and timing checks, not indiscriminately including story dialogue.
