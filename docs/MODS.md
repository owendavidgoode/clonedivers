# Mod audit — Clonedivers pack

Checked against the live Nexus Mods pages (description, Files, Posts, Bugs tabs) on **2026-09-05**.
Game patch at the time: **Devoid of Liberty 7.0.2** (PC build 01.007.002, 25 Aug 2026), preceded by 7.0.1 (17 Aug)
and the major 7.0.0 (12 Aug). None of the three changed how mods load: loose `<hash>.patch_N` files in `data\` still work,
there was no archive-hash reset or new anti-tamper. 7.0.0 did break a handful of mods for a day or two and older
unmaintained ones can still crash.

How to re-run this check yourself: open each mod's **Posts** tab, sort by newest, and look for reports dated after the
latest Helldivers 2 patch. "Still works" comments beat "last updated" dates.

## Verdicts

| Mod | Verdict | Last updated | Works on 7.0.2? | Evidence and caveats |
|---|---|---|---|---|
| [Clone Armory BETA](https://www.nexusmods.com/helldivers2/mods/13956) | **Keep** (backbone) | 17 Aug 2026 (Main 1.2 on 16 Aug) | Yes | Author sticky: "added support for the new armors and weapons introduced in the recent update". Posts 2–4 Sep are about performance, not breakage. Open bugs 20 Aug: crashes and heavy lag in the ship; a user reported 90 → 4–20 fps from the armor module, author: "replaces over 1500 files… waiting for our mod tools to update to optimize for RAM". `--use-d3d11` reported to fix the fps drop. Six modules, ~2.8 GB, all on the same archive hash; needs Arsenal. Its *SEAF* module has a dedicated "fatal crash" bug report; its *DC-15s* module duplicates Clone Blasters. |
| [Shiny Clone Trooper Pack](https://www.nexusmods.com/helldivers2/mods/1248) | Keep as lite alternative | 22 Jun 2026 (v2.1, lean body) | Likely | Author active 21 Aug answering questions, no post-7.0 breakage reports. B-01 Tactical only. Install Meshes + exactly one legion skin. Same author co-maintains Clone Armory, so never install both. |
| [Blood and Dirt](https://www.nexusmods.com/helldivers2/mods/14774) | Keep as lite alternative | 2 Sep 2026 | Yes (post-7.0.2 update) | One armor set (O-3 Free Spirit) built on the Armory models with all legions, ARC gear, rank markings, 1K/2K textures. The low-RAM option. |
| [Clone Blasters](https://www.nexusmods.com/helldivers2/mods/6633) | **Keep** | 4 May 2026 (V1.6 "fixed for April 28 update") | Likely | No breakage reports across 7.0.0–7.0.2; newest posts are feature requests (5 Sep). 84 same-named triplets in per-weapon option folders, so Arsenal is effectively required. Models only: pair with Blue Overhaul and PEW-PEW. |
| [Blue Overhaul – Laser Bolts](https://www.nexusmods.com/helldivers2/mods/4835) | **Add** | 21 Aug 2026 | Yes | 846 endorsements. Turns tracers into blaster bolts and recolours orbital/Eagle effects. Recommended on both the Clone Blasters and PEW-PEW pages. Missing from the first draft of this list. |
| [PEW-PEW sounds](https://www.nexusmods.com/helldivers2/mods/4891) | **Keep** | 14 Jul 2026 (V3.1) | Likely | Author replied to bug reports 29 Aug; a 2 Sep post complains only that SEAF squads lack blaster sounds (by design). Modular manifest with ~60 weapon folders; use the *Republic* file (223 MB). Author warns HD2ModManager can crash removing it. |
| [Full Clone Voice Conversion](https://www.nexusmods.com/helldivers2/mods/11826) | **Keep** | 6 Mar 2026 | Yes | "Still works perfectly fine for me" 2 Sep; another happy post 4 Sep. Plain two-file pair (patch_0 + .stream, 36 MB), the one true drop-in. Author stopped maintaining it 23 Jun; all four voice options share the same lines; unmatched lines are silenced. |
| [RiqCrow sound & voice library](https://www.nexusmods.com/helldivers2/mods/633) | **Add** (replaces 2205) | 28 Aug 2026 | Yes | ~90-file modular library maintained through 7.0.x: Star Wars Mission Control + Democracy Officer (12 Aug), clone officers (12 Aug), clone SEAF with laser SFX for the new squad weapons (16 Aug). |
| [Temuera Morrison Voice Overhaul](https://www.nexusmods.com/helldivers2/mods/12524) | **In pack** (r3: DO + Mission Control only) | 1 May 2026 | Unknown | One mod for all player voices, Democracy Officer, Mission Control, Eagle-1 and (v1.6) SEAF, from real Temuera Morrison lines. Loaded before Full Clone Voice and Clone Pilot so they win the player-voice and pilot banks; Temuera supplies only the DO and Mission Control lines (asset-level check: [docs/pack/2026.09.05-r3/conflict-report.md](pack/2026.09.05-r3/conflict-report.md)). |
| [SEAF Clone NPCs 2.0](https://www.nexusmods.com/helldivers2/mods/5251) | **Keep** | 1 Sep 2026 | Yes | Rebuilt for the 7.0.0 SEAF rework four days before this audit; 356 endorsements. Requirements note: the AIO format "is no longer compatible with HDMM", i.e. Arsenal only. Known issue: lights still visible on skins (Light Remover option). Collides with Armory's SEAF module. |
| [Automaton → CIS Overhaul](https://www.nexusmods.com/helldivers2/mods/9115) | Keep **with warning** | 24 Jul 2026 | Reported broken by some | Posts 16 Aug–4 Sep: several "crashes as soon as a mission loads"; 3 Sep one user fixed it with purge + redeploy; author says it works for him and asks for crash logs. 1.6 GB *May Update 2* (use this) or 2.9 GB *Performance TEST*. ~40 option folders, Arsenal required; author advises disabling *CIS Props* and *Dead Droids* first. Its predecessor (mod 558) was hidden by Nexus staff 7 Aug as unsupported. |
| [Venator over Super Destroyer](https://www.nexusmods.com/helldivers2/mods/6490) | **Add** (replaces 1261 and 12082) | 4 Jun 2026 | Likely | 263 endorsements, Republic/grey/Helldiver liveries, covers the player ship and the background fleet, used by the Clone Wars Overhaul collection. No post-7.0 complaints found. |
| [LAAT Gunship over Pelican-1](https://www.nexusmods.com/helldivers2/mods/5778) | **Add** | 14 Aug 2026 | Yes | Republic, Muunilinst 10, Imperial, Helldiver and SEAF liveries, optional ball turrets, Battlefront audio toggles. Updated after 7.0.0. |
| [Y-Wing over Eagle-1](https://www.nexusmods.com/helldivers2/mods/12381) | **Add** | 4 Jun 2026 | Likely | Clone Wars Y-Wing in mission and hangar. Alternatives: [ARC-170 Eagle Replacer](https://www.nexusmods.com/helldivers2/mods/1433) (7 May), [Jedi Starfighters](https://www.nexusmods.com/helldivers2/mods/12192) (21 May). |
| [AT-TE exosuits + LAAT/c](https://www.nexusmods.com/helldivers2/mods/6396) | **Add** | 13 May 2026 | Likely | Exosuits → AT-TE with matching audio, vehicle/oil Pelicans → LAAT/c, wreckage → destroyed AT-TEs. |
| [TX-130 FRV](https://www.nexusmods.com/helldivers2/mods/6015) | **Add** | 7 May 2026 | Likely | FRV → TX-130 Saber tank, Republic option. |
| [Galactic Map Overhaul](https://www.nexusmods.com/helldivers2/mods/1489) | **Keep** | 8 May 2026 (6.2.2.04) | Likely | 20 Aug user report: still works after 7.0.0, "some logos a little glitched but it does not break the game". Needs US English text, pick exactly one faction and one voice option, must load last. |
| [All music replacement with Star Wars](https://www.nexusmods.com/helldivers2/mods/15612) | Optional (replaces 7057) | 29 Aug 2026 (v1.0.3) | Built for 7.0.x | Nearly every track (ship, FTL, loadout, drop, combat, flag, extraction, victory) from Battlefront II 2017 / Force Unleashed. Brand new, 3 endorsements: try it, remove it if anything goes silent. |
| [Clone Trooper Ranks](https://www.nexusmods.com/helldivers2/mods/14612) | Dropped from pack (r2) | 14 Jul 2026 | Likely | Text only: every level and warbond title → GAR ranks. Written to complement Clone Armory. Both string tables are overridden by Galactic Map, which must load last. |
| [Clone Pilot Audio](https://www.nexusmods.com/helldivers2/mods/10667) | **In pack** | 3 Feb 2026 | Unknown | Clone pilot lines for Eagle-1 and Pelican-1, a gap both voice mods leave. |
| [Republic Commando stratagem inputs](https://www.nexusmods.com/helldivers2/mods/13942) | **In pack** | 1 Jun 2026 | Likely | Stratagem beeps → tac-pad sounds. Same author: Battlefront map sounds (13944), RC radio chatter shield pack (13869). |
| [All Invisible Capes](https://www.nexusmods.com/helldivers2/mods/11657) | **In pack** | 28 Apr 2026 | Likely | Cape remover without ReShade; the Armory authors recommend it for the clean clone look. Themed alternative: [Star Wars Cape Overhaul](https://www.nexusmods.com/helldivers2/mods/1417) (Aug 2025, stale). |
| [GNK Hellbomb](https://www.nexusmods.com/helldivers2/mods/11333) | **In pack** | 3 May 2026 | Likely | Hellbomb → GNK power droid. Companion: [RX-200 Falchion over Bastion](https://www.nexusmods.com/helldivers2/mods/11329). |
| thebf333's Custom Projectiles (Nexus ID not verified, so unlinked) | **In pack** | not checked | Deployed in r3 | Battlefront-shaped bolts (Blue option, custom bot-bolt toggle on). Loads after Blue Overhaul and overrides 15 of its particle assets on purpose ([conflict-report.md](pack/2026.09.05-r3/conflict-report.md)). Not part of the 2026-09-05 Nexus check. |
| No Bullet Casings Or Ejection VFX (Nexus ID not verified, so unlinked) | **In pack** | not checked | Deployed in r3 | Removes casings and ejection particles. Loads after Blue Overhaul so the two ejection particles they share end up empty; conflict-report.ps1 confirmed it touches no weapon units. Not part of the 2026-09-05 Nexus check. |
| Background ARC-170s (Nexus ID not verified, so unlinked) | **In pack** | not checked | Deployed in r3 | ARC-170s flying past the Venator in the ship background. Overrides nothing and is overridden by nothing in the r3 conflict report. Not part of the 2026-09-05 Nexus check. |
| [Cody's Jetpack + Republic Supply Pack](https://www.nexusmods.com/helldivers2/mods/6735) | Optional, either/or | 7 May 2026 | Likely | Overlaps Armory's Backpacks module. Pick one. |
| [Clone Naval Officer NPCs](https://www.nexusmods.com/helldivers2/mods/8881) | Skip for now | 29 Apr 2026 | Likely | Bridge crew → clone naval officers, but requires the Shiny pack and [Clone Officer](https://www.nexusmods.com/helldivers2/mods/3156), which conflicts with the Armory route. |

## Dropped

| Mod | Why |
|---|---|
| [Ship Overhaul – Venator](https://www.nexusmods.com/helldivers2/mods/12082) | Bug "Newest update breaks ship orientation and location": ship rotated, out of place, sometimes invisible (13 Aug), confirmed 27 Aug, "I don't see the front of the ship" 2 Sep. No author response since 21 May. History of crashes when joining friends' missions. |
| [Arvis' Custom Venator](https://www.nexusmods.com/helldivers2/mods/1261) | Bug "Crashes Game Upon Drop Cutscene" 13 Jul with no reply; a 30 Jul post describes fps halving; GPU warning on the page; zero activity after 7.0.0 so nothing confirms it either way. Venator 6490 covers the same slots. |
| [Clone Trooper Music](https://www.nexusmods.com/helldivers2/mods/7057) | Last touched 31 Jul 2025, 5 endorsements, three cues only, last indirect signal Jan 2026 (silence when combined with another music pack). Superseded by 15612. |
| [NPC Voice Replacements](https://www.nexusmods.com/helldivers2/mods/2205) | Author's own status page still lists the Republic Democracy Officer as a placeholder speaking Imperial lines; last updated 23 Feb 2026. RiqCrow's modules (12 Aug) replace it. |

## Conflicts and load order

All of these mods patch the same archive (`9ba626afa44a3aa3`), so every installed option needs a unique consecutive
`patch_N` across the whole pack. Arsenal does this and flags real conflicts; the pairs below are the ones that overlap
on purpose and need a decision:

- **Player armor:** Clone Armory *or* Blood and Dirt *or* Shiny pack.
- **Weapon models:** Clone Blasters *or* Armory's *Movie Accurate DC-15s* module.
- **SEAF troopers:** SEAF Clone NPCs 2.0 *or* Armory's *SEAF Clone Troopers* module.
- **Backpacks:** Armory's *Backpacks* module *or* Cody's Jetpack.
- **Ship interior textures:** Armory's *Decal Sheets* module includes "Gray Ship Interior" and "Idle TV Screen" patches; harmless with Venator 6490, redundant with any interior mod.
- **Voices:** Temuera Morrison (DO + Mission Control) before Full Clone Voice + RiqCrow; load order decides who wins the shared banks.
- **Galactic Map Overhaul** must have the highest patch number (load last), otherwise other UI patches overwrite its icons.
- **Blue Overhaul** after Clone Blasters.

## Still not covered by any mod (as of this audit)

- DO/Mission Control: covered since pack r3 by Temuera Morrison 12524.
- Hellpod model and stratagem icons: nothing Star Wars themed exists on Nexus (Halo and Warhammer drop pods prove it is possible).
- Terminids → Geonosians: text rename only (Galactic Map). No model replacer.
- Illuminate → anything: text rename only. The new 7.0 Void enemies have nothing.
- HUD / reticle: only generic reticle packs exist.
- In-mission loading screen art. Intro cinematics do exist: [Clone Wars intro](https://www.nexusmods.com/helldivers2/mods/1540) (Jan 2025, stale).

## Performance: why the pack hurt 16 GB machines, and the Lighter textures variant

Measured on pack 2026.09.05-r3 (604 files, 8.81 GB) on 2026-09-06, by parsing every bundle's index table:

| Component | Size | What it is |
|---|---|---|
| `.gpu_resources` | 7.87 GB | texture surfaces and mesh buffers, all resident once loaded |
| `.patch_N` | 0.64 GB | index tables and CPU-side data |
| `.stream` | 0.30 GB | streamed payloads (audio, video) |

By asset type: textures 4.49 GB (1,067 of them, 953 with full mip chains), unit meshes 3.44 GB, Wwise sound banks 0.49 GB.
Every one of the 1,067 textures was flagged **not streamed** by the mod tooling, so the game keeps the whole 4.5 GB
resident from the moment those assets load instead of paging levels through its 1.5 GB texture-streaming budget
(`data\settings.ini`, `texture_streaming`). That is the RAM spike that crashed a 16 GB PC at boot. Formats were already
BC1/BC3/BC5/BC7, so block compression had nothing to gain; 13 textures are 8192², 65 are 4096².

**Lighter textures** (option `skinny`, off by default) is the same 604 files with every texture converted to a streamed
texture, built by [`tools\optimize-pack.ps1`](../tools/optimize-pack.ps1) with
[Stingray Texture Optimizer 0.1.4](https://github.com/Shiroiame-Kusu/StingrayTextureOptimizer) (`--stream 512`,
`--strategy quality`, `--no-dedup`; no `--max-size`, no `--add-mips`). The mip chain moves into the bundle's `.stream`
byte for byte and only the levels of 512 px and below stay resident; nothing is discarded or re-encoded.

| | r3 base | Lighter textures |
|---|---|---|
| texture bytes resident at load | 4.42 GB | 0.19 GB |
| `.gpu_resources` total (resident) | 7.87 GB | 3.63 GB |
| `.stream` total (streamed on demand) | 0.30 GB | 4.72 GB |
| files | 604 | 606 |

The 3.6 GB that stays resident is mesh data (Clone Armory bodies, CIS droids), which only the mod authors can reduce
(the Armory author's "LOD-only" test files are that work in progress). Verification: the tool compares every non-texture
payload with the original; an independent check over all 74 converted bundles found 650 of 650 streamed chains
byte-identical to the original texture data, resident tails equal to the end of each chain, and CPU headers unchanged
except the documented DDS flag and linear-size fields. The one field the tool cannot derive (the streaming flag at prefix
offset 4) is a documented best guess that matched the shipped game data 75% of the time and tested harmless when wrong.

Rejected on the way: a `--max-size 4096` cap, which measured "visible softening" (32–36 dB) on a dozen 8K CIS tank,
dropship and ARC-170 textures, while streaming already solves the memory problem without touching pixels. The six
bundles with pre-existing verifier notes are listed in [PUBLISHING.md](PUBLISHING.md#preparing-the-mods).

## Sources

- The Nexus pages linked above (Files, Posts and Bugs tabs), read 2026-09-05.
- Steam patch notes for 7.0.0 / 7.0.1 / 7.0.2; the Helldivers 2 wiki *Broken Mods* page for the `0x44415441` startup error.
- [Clone Wars Overhaul collection](https://www.nexusmods.com/games/helldivers2/collections/h2juj4), Revision 5, 11 Jul 2026, "working as of 7/11/2026 (intended for hd2 arsenal)".
- [HD2 Arsenal](https://www.nexusmods.com/helldivers2/mods/4664), listed as a requirement on most of the pages above.

## Changes since the audit

- r2 (2026-09-05): Clone Trooper Ranks off; RC stratagem beeps moved before (lower priority than) RiqCrow's ship audio so RiqCrow wins the 4 shared assets.
- r3 (2026-09-05): Temuera Morrison clone VO added for DO + Mission Control.
- r4 (2026-09-06): same mods; adds the Lighter textures variant (option `skinny`, streamed textures) for 16 GB PCs. See Performance above.
- r5 (2026-09-07): Republic Commando Delta Squad (Nexus 552, degabait; file 'Delta Squad AIO-552-1-1A') added last in load order as the `commandos` toggle, on by default. Wins 86 Clone Armory assets (the four armor sets' units and bones) and 3 Accessories assets; touches nothing of Galactic Map. Requires the Brawny body type; CE-35 shares parts with B-01 and DP-40.
- r6 (2026-09-07): ArcanePoro's Custom Scopes Compendium, 'Attachment Remover' file v1.25 (Nexus 443, served as a .7z; repacked as a zip in dist\mods) added last as the `optics` toggle 'Hide weapon sights', on by default: 50 patch sets (patch_257..306, 150 files, 0 MB), 50 of its 52 removers on; 'Reprimand Muzzle Break' and 'Solo Laser Attachment' stay off because Clone Blasters rebuilt those two units for its Reprimand, Supressor and Censor models (conflict-report.ps1). Recommended by the Clone Blasters author because that mod leaves vanilla optics in place, so they float over the blaster models. Conflict report: see docs/pack/2026.09.07-r6.
