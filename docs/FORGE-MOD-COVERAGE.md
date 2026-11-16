# Clonedivers Forge Mod Coverage and Pinned Preset

The `build25480438-r4` preset (`Clonedivers/Resources/forge-gameplay-build25480438-r4.json`,
integrated in `1.14.2` / `0.8.2`, awaiting `R1` asset upload for `forge-mods-2026-11-15`
and `pack-2026.11.09-r47-files`) consists of three official bundled mods, **7 required
gameplay downloads** (`7,780,423` bytes / `~7.42 MiB`), **4 optional** project-owned
downloads (**3 client-only** `gameplay: false` + **1 host-driven gameplay** `gameplay: true`,
totaling `282,832` bytes), and one selected appearance pack (`Clones r19` or one of the
4 Compact-LEGO-baked `Empire r47` variants; Regular has none), for **15 mods** with all
defaults enabled. The launcher obtains all **11 downloads** (`8,063,255` bytes /
`~7.69 MiB` total before appearance assets) automatically on first setup with
archive/content verification and official imports.

Five required packages come directly from authors. The Heavy/Squad compatibility
archives use the owner's confirmed written author permission; all 4 optional project-owned
mods (`clean-exit` `0.2.0`, `lego-compact-leaf-writer` `0.0.9`, `empire-lego-yoda` `0.1.2`,
and `chaos-invasions` `0.2.0`) have SDK-16 source under `forge-mods/`, and the retired
mine fix (`0.1.1`) is project-owned MIT source. See [SEAF compatibility permission](SEAF-COMPATIBILITY-PERMISSION.md)
and `Clonedivers/Resources/forge-gameplay-build25480438-r4.json` for exact sources,
hashes, versions, order and options.

All 4 optional defaults are enabled once on fresh `r4` setup, and the 3 new `r4`
optionals (`lego-compact-leaf-writer`, `empire-lego-yoda`, `chaos-invasions`) are
offered once during an `r3` -> `r4` upgrade while preserving prior `clean-exit` choices.
Disabling, replacing or removing any optional mod afterwards is respected across
restarts, required-mod repair, order repair, and rollback; already disabled copies
are respected on first setup too. Clean Exit `0.2.0` is live on `forge-mods-2026-11-08`;
`lego-compact-leaf-writer-0.0.9.zip`, `empire-lego-yoda-0.1.2.zip`, and
`chaos-invasions-0.2.0.zip` target `forge-mods-2026-11-15` (pending `R1` upload and
attended `R2` verification).

## Gameplay and Client Mod Inventory

| Candidate | Owner state and decision | Dependencies / acquisition / redistribution |
| --- | --- | --- |
| HellForge Editor 0.4.2, Hell Forge Difficulty 0.16.1, Vanilla Plus 0.2.0 | Enabled, initialized; bundled pinned baseline | Obtain through official HellForge `0.4.0-beta`; launcher package excludes engine and bundled mods. VP is exclusive with Reinforcement Beacons Fixed. |
| Native Smarter SEAF 1.1.0 | Enabled, initialized | Author portal `https://hf-mod.com/Mods/Details/79`, file `144`, SDK `12`. Original SHA in preset. Native alternative to Lua Smarter; never enable both. Portal restrictions/source inclusion do not grant redistribution permission. |
| Heavy SEAF Presence Everywhere 1.2.0 | Original failed initialization; `1.2.0-clonedivers.3` repaired and owner behavior-tested | Author NontoxicKappa: `https://hf-mod.com/Mods/Details/82`. Requires repaired Squad. Compatibility archive is available to setup under the owner's written-permission confirmation (`forge-gameplay-build25480438-r1`); original credits/source retained. |
| SEAF Squad Stratagems 0.1.4 (portal label 1.0) | `0.1.4-clonedivers.2` enabled; owner call-ins worked in D11 | Author JohnnyGrease: `https://hf-mod.com/Mods/Details/59`. Repaired Heavy supplies compatible delivery. Compatibility archive is available to setup under the owner's written-permission confirmation (`forge-gameplay-build25480438-r1`); no broader license is inferred. |
| Bingus Shared Loader - v20 | Enabled, initialized | `https://github.com/CowboyBingus/BingusSharedLoader/releases/tag/v20`. LuaJIT dependency of Lua data mods. Exact v20 source LICENSE is 0BSD; `THIRD_PARTY.md` notes original game Wwise resources in generated releases, so obtain the author archive directly. No loader archive bundled here. |
| Mod Options Menu v1.2.1 | Enabled, initialized | `https://github.com/CowboyBingus/ModOptionsMenu/releases/tag/v1.2.1`. Original asset digest matches preset; exact-tag LICENSE is 0BSD. Requires Bingus 18+. Author-only acquisition remains the rollout policy. |
| Smarter Guard Dogs & Sentries 5.2.0 | Enabled; initialization passed; owner observed yellow markers before OFF fix | `https://github.com/Th3chef/Smarter-Guard-Dogs-and-Sentries/releases/tag/v5.2.0`; loader 19+, installed 20; options menu optional. All rights reserved/private use, no redistribution. Marker-OFF combat is unqualified. |
| Operation Reroller 0.1.2 | Enabled, initialized; actual reroll behavior unqualified | `https://hf-mod.com/Mods/Details/12`, file `138`. SDK `6+`. MIT plus SDK notices; retain attribution. No author digest available; local receipt hash recorded. |
| Clonedivers Clean Exit 0.2.0 | Live optional default in `r3`/`r4`; clean quits across reported 20-cycle D11 soak (note Nov 15 pre-fix `r47` mini A/B cycle 3 still dumped `+0x626296` ending inside a mission) | Project-owned client-only code mod, source in `forge-mods/clean-exit`. SDK `16`, build `25480438`. `gameplay: false` excludes it from peer matching. Published on `forge-mods-2026-11-08` (`8,192` bytes, SHA-256 `6835354fff8264c6f29c33dfac71a0b6e4c4da7a401920bb76e1b604d2435932`, content hash `0006a02101583cf05e3bd9fa4e431409c18ff4ac72f59ae1ece8c7d9831a888a`). |
| LEGO Compact Leaf Writer 0.0.9 | Optional default in `r4`; passed `15,332` C checks, `9/9` suites, `290`-pose parity, and Sub-Run 1E (`r47-lego-compact`); multi-actor co-op awaits `R2` | Project-owned client-only code mod (`gameplay: false`, `coop: per-player`), source in `forge-mods/lego-compact-leaf-writer`. SDK `16`, build `25480438`. Targets `forge-mods-2026-11-15` (`199,207` bytes, SHA-256 `34e683e503d525ea5d419bb3477cb0f44e9b9f6cb3eed3c0ae4daebbf55b3dfa`, content hash `71d0bbb0d550b6c312f7d660941c3b620821279c51be47d71d205eb67bedbee3`). |
| Empire LEGO Yoda Death 0.1.2 | Optional default in `r4`; `0.1.2` passed `139` C checks (`ScanVisuals == 4` on both `r47-lego-compact` and `r47-shuttles`); `0.1.1` passed Sub-Run 1A live check; Compact LEGO live audio awaits `R2` | Project-owned client-only code mod (`gameplay: false`, `coop: per-player`), source in `forge-mods/empire-lego-yoda` (copyrighted audio excluded from git). SDK `16`, build `25480438`. Targets `forge-mods-2026-11-15` (`45,907` bytes, SHA-256 `3d41fe4b6c4f49c52df786a8f83e1e9a1ff501aa3c14edfe742126069aaf9b18`, content hash `2e78173e56b6cc752ac27d810a7a6e7b72352ebe24437edae3a49b7d580f78dc`). |
| Chaos Invasions 0.2.0 | Optional default in `r4`; passed `195` C checks, `52` LuaJIT menu checks, and `15` delivery checks; live 3/4/5-way gameplay and co-op sync await `R2` | Project-owned gameplay code mod (`gameplay: true`), source in `forge-mods/chaos-invasions`. SDK `16`, build `25480438`. Spawns Dropships/Warp Ships/Breaches with 13-RVA pins, `<75%` preloader, and `BHV_PERCEPTION` narrowing. Targets `forge-mods-2026-11-15` (`29,526` bytes, SHA-256 `42498666f228ea433d4ee63fc0447b7f2ec3033893f2012a9b1349106c7d4d41`, content hash `4c941838b63ce54291a7b8571f21e61817cce66e3548cd413705dfd9101d570b`). |
| D11 Mine Boundary Fix 0.1.1 | Retired/disabled on engine `0.1.47` | The author integrated the bounds/loading fix. Keep the old file for recovery; do not layer its hooks onto engine `0.1.47`. |
| `chaos-probe` 0.1.0 | Completed Wave 2 Sub-Run 3 (`3x` D5) cleanly; lab-only diagnostic (superseded by `chaos-invasions` `0.2.0`) | Never included in player preset; passed `6/6 CP_GUARD`, `6 x CP_UNHOOK`, `0` dumps, `lifecycle_dropped=0` (`automaton` `2/2 op=air`, `terminid` `2/2 op=breach`, `illuminate` saturated at `10,000` lines / `0` natural `op=air`). |
| Armored Overhaul 3.7.12 | Original rejected with Clones Bastion model conflict; handling-only private trial initialized, OFF | `https://hf-mod.com/Mods/Details/15`, file `299`. SDK `7+`; all rights reserved, private adaptation not redistributable. Actual driving/handling not tested; optional isolated trial only. |
| Reinforcement Beacons Fixed 4.6.1 | Imported, initialized, OFF because VP supplies overlapping behavior | `https://github.com/CowboyBingus/ReinforcementBeaconsFixed/releases/tag/v4.6.1`; loader 18+. Zero-Clause BSD. Each peer must install. Never enable beside VP. |
| Gameplay Reforged Enemy Balance / Gameplay Systems HF profiles | Imported/trialed, OFF; conflicting alternative profiles | Author portal files `177`/`178`. Systems requires Bingus 20/LuaJIT and overlaps SEAF spawners/rosters/stat editors. Portal rights reserved; not an additive default. |
| Pick Your Bots/Bugs/Squids HF port 2.0.1 | Imported, OFF; excluded from squad preset | Author portal file `24` documents single-player limitation. Do not describe this as squad-qualified. |
| SEAF Call-In Stratagem Replacer | Considered; superseded by four native Squad call-ins | Default FRV replacement conflicts with D11 free FRV. Not installed in current squad preset. |
| SEAF Commander 1.3.3 / SEAF Drop | Commander staged; not integrated | Exact Drop dependency remains Ayaka-gated; author account/access required. No bypass, substitute port or fabricated dependency. Also resolve native Smarter ping/targeting overlap before any isolated test. |
| XS1 Goliath 1.0 | Imported/staged, OFF; optional later trial | Actual mech behavior/load/MP unqualified. Core scale-up takes priority. |
| Single-player SEAF Companion | Excluded | Explicit single-player scope conflicts with the intended squad build. |
| Supreme Command | Rejected by owner | Do not add. |

## Appearance and Non-Shipping Boundaries (`1.14.2` / `0.8.2`)

- **Live baseline vs `1.14.2` / `0.8.2` candidates:** Live feeds remain `Clones` (`2026.09.27-r19`) and `Empire` (`2026.11.08-r45-v4` Full) until `R1`/`R2`. In `1.14.2` / `0.8.2`, `Empire` ships all **4 Compact-LEGO-baked `2026.11.09-r47`** variants (`Full` / `Lighter` × `CIS droids` ON / OFF, with `lambda-fix` `+0.20 m` SEAF personnel Lambda clearance, Stormtrooper lower legs, `crew66`, three Phase II sets, restored Foesmasher, and red Spear trails): `8,738,707,008` B (`Full + Droids`), `6,013,620,208` B (`Full + Native`), `8,961,480,892` B (`Lighter + Droids`, `2,542,440,456` B GPU), and `6,172,775,164` B (`Lighter + Native`). All 4 passed offline `verify_candidate.py` (`0` findings), and `W4` A/B passed on `r47-shuttles` (`14` cycles, `0` events vs Arm A `r45-v4` `12` cycles / `1` crash event).
- **Non-shipping / held tracks:**
  - *Per-attack TIE switching (`empire-tie-switch` `0.2.1`):* `0.2.1` (`1,005` C checks + `135` closure parts) passed Sub-Run 1D Part 1 co-loaded with `clean-exit` `0.2.0` on patched `tie-held-root`, and `upstream-pr-eagle-base.patch` is prepared for `>=0.4.1-beta`; stock `0.4.0-beta` lacks `eagle_base`, so TIE switching remains excluded from `build25480438-r4`.
  - *Lab-only capture & diagnostic mods:* `empire-lego-camera` `0.0.2` (Sub-Run 1A capture helper) and `chaos-probe` `0.1.0` (Sub-Run 3 diagnostic) stay outside player presets.
  - *Shelved non-shipping tracks:* `at-st` Hulk rig bone-merging (`88` palettes / `198` nodes exceed native `<=64` / `<=128` limits), `empire-weapons` (`E-11`/`DLT-19` lack `.unit` art), and per-instance `mixed-appearances` recipe remain shelved offline and are NOT shipped in `1.14.2` / `0.8.2`.

The owner completed a D11 drop with Heavy/Squad and died; no extraction or
multiplayer result was reported. Subsequent initialization/ship tests do not
extend that qualification. Archives listed as downloaded/imported can still be
disabled. Installing every discussed alternative simultaneously would add known
conflicts; the pinned baseline is the intended combination.
