# Clonedivers Forge Mod Coverage and Pinned Preset

The live `build25480438-r3` preset (`Clonedivers/Resources/forge-gameplay-build25480438-r3.json`,
shipped in `1.14.1` / `0.8.1`) consists of three official bundled mods, **7 required
gameplay downloads** (`7,780,423` bytes / `~7.42 MiB`), **1 optional** client-only
download (`Clonedivers Clean Exit 0.2.0`, `clean-exit-0.2.0.zip`, `8,192` bytes), and
one selected appearance pack (`Clones r19` or `Empire r45-v4 Full`; Regular has none),
for **12 mods** with all defaults enabled. The launcher obtains all **8 downloads**
(`7,788,615` bytes / `~7.43 MiB` total before appearance assets) automatically on
first setup with archive/content verification and official imports.

Five required packages come directly from authors. The Heavy/Squad compatibility
archives use the owner's confirmed written author permission; Clean Exit `0.2.0`
is project-owned MIT/SDK-16 source (`forge-mods/clean-exit`), and the retired mine
fix (`0.1.1`) is project-owned MIT source. See [SEAF compatibility permission](SEAF-COMPATIBILITY-PERMISSION.md)
and `Clonedivers/Resources/forge-gameplay-build25480438-r3.json` for exact sources,
hashes, versions, order and options.

Clean Exit `0.2.0` is client-only (`gameplay: false`) and excluded from HellForge's
co-op matching. It is enabled on first `r3` setup. Disabling, replacing or removing
it afterwards is respected across restarts, required-mod repair and rollback. An
already disabled copy is respected on first setup too. Its `8,192`-byte archive is
published live on `forge-mods-2026-11-08` (SHA-256 and content hash in [Forge status](FORGE.md)).

A separate staged experimental catalog (`build25480438-r4` on branch
`claude/exp-yoda-011-20261115`, plus `r4-lego` on `claude/exp-lego-20261115` @
`ea5075a1`, not merged or published) covers optional client-only LEGO tracks:
`Empire LEGO Yoda Death (prototype)` `0.1.1` (`empire-lego-yoda-0.1.1.zip`,
`45,502` bytes, `gameplay: false`, `coop: per-player`, `build25480438-r4` total
`9` downloads / `7,834,117` bytes) and `lego-compact-leaf-writer` `0.0.9`
(`lego-compact-leaf-writer-0.0.9.zip`, `199,207` bytes). Both passed Wave 2 live
testing (`Sub-Run 1A` and `Sub-Run 1E`); release asset publication on
`forge-mods-2026-11-15` awaits Wave 3 `R1` approval and attended `R2` checks.

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
| Clonedivers Clean Exit 0.2.0 | Live optional default in `r3`; clean quits across reported 20-cycle D11 soak (note Nov 15 pre-fix `r47` mini A/B cycle 3 still dumped `+0x626296` ending inside a mission) | Project-owned client-only code mod, source in `forge-mods/clean-exit`. SDK `16`, build `25480438`. `gameplay: false` excludes it from peer matching. Published on `forge-mods-2026-11-08` (`8,192` bytes, SHA-256 `6835354fff8264c6f29c33dfac71a0b6e4c4da7a401920bb76e1b604d2435932`). |
| Empire LEGO Yoda Death (prototype) 0.1.1 | Staged optional candidate in `r4` (`claude/exp-yoda-011-20261115`); Sub-Run 1A live check passed | Project-owned client-only code mod (`gameplay: false`, `coop: per-player`), source in `forge-sdk` / `exp-yoda-011` (`45,502` bytes, SHA-256 `2a5b439a0ca9ab486ffa99b1b7fd650fd12f98b578c7acdbe903864336c7b03f`). Armed `4/4` on `r47-shuttles` (`DP-40` `started`, `FS-37` silent); awaits `forge-mods-2026-11-15` `R1` upload and owner LEGO kit decision. |
| D11 Mine Boundary Fix 0.1.1 | Retired/disabled on engine `0.1.47` | The author integrated the bounds/loading fix. Keep the old file for recovery; do not layer its hooks onto engine `0.1.47`. |
| `chaos-probe` 0.1.0 | Completed Wave 2 Sub-Run 3 (`3x` D5) cleanly; lab-only | Never included in player preset; passed `6/6 CP_GUARD`, `6 x CP_UNHOOK`, `0` dumps, `lifecycle_dropped=0` (`automaton` `2/2 op=air`, `terminid` `2/2 op=breach`, `illuminate` saturated at `10,000` lines / `0` natural `op=air`). |
| Armored Overhaul 3.7.12 | Original rejected with Clones Bastion model conflict; handling-only private trial initialized, OFF | `https://hf-mod.com/Mods/Details/15`, file `299`. SDK `7+`; all rights reserved, private adaptation not redistributable. Actual driving/handling not tested; optional isolated trial only. |
| Reinforcement Beacons Fixed 4.6.1 | Imported, initialized, OFF because VP supplies overlapping behavior | `https://github.com/CowboyBingus/ReinforcementBeaconsFixed/releases/tag/v4.6.1`; loader 18+. Zero-Clause BSD. Each peer must install. Never enable beside VP. |
| Gameplay Reforged Enemy Balance / Gameplay Systems HF profiles | Imported/trialed, OFF; conflicting alternative profiles | Author portal files `177`/`178`. Systems requires Bingus 20/LuaJIT and overlaps SEAF spawners/rosters/stat editors. Portal rights reserved; not an additive default. |
| Pick Your Bots/Bugs/Squids HF port 2.0.1 | Imported, OFF; excluded from squad preset | Author portal file `24` documents single-player limitation. Do not describe this as squad-qualified. |
| SEAF Call-In Stratagem Replacer | Considered; superseded by four native Squad call-ins | Default FRV replacement conflicts with D11 free FRV. Not installed in current squad preset. |
| SEAF Commander 1.3.3 / SEAF Drop | Commander staged; not integrated | Exact Drop dependency remains Ayaka-gated; author account/access required. No bypass, substitute port or fabricated dependency. Also resolve native Smarter ping/targeting overlap before any isolated test. |
| XS1 Goliath 1.0 | Imported/staged, OFF; optional later trial | Actual mech behavior/load/MP unqualified. Core scale-up takes priority. |
| Single-player SEAF Companion | Excluded | Explicit single-player scope conflicts with the intended squad build. |
| Supreme Command | Rejected by owner | Do not add. |

## Appearance and Experimental Tracks

- **Live appearances (`1.14.1` / `0.8.1`):** `Clones` (`2026.09.27-r19`, Full/Lighter, Delta Squad, CIS droids, Covenant squids) and `Empire` (`2026.11.08-r45-v4` Full, fixed CIS droids).
- **Staged `r47-shuttles` Full candidate (`W4` A/B gate passed; awaiting `R1`/`R2`):**
  Built offline (`D:\ClaudeWork\empire-forge\empire-forge-r47-shuttles`, `96`
  archives, `8,738,707,008` bytes) with SEAF personnel Lambda `+0.20 m` ground
  clearance (`lambda-fix`), Stormtrooper lower legs, `crew66`, three Phase II sets,
  restored Foesmasher, and red Spear trails. Passed Wave 2's `W4` `12+12` D11
  Automaton A/B gate (`14` cycles, `0` events vs Arm A `r45-v4` `12` cycles / `1`
  crash event) and Sub-Run 1A flight wing-fold check; Wave 3 awaits `R1` upload
  and `R2` attended visual clearance inspection.
- **Experimental and shelved tracks:**
  - *4-variant `r47 (+ lambda-fix)` matrix (`Full`, `nodroids`, `Lighter`,
    `nodroids-lighter`):* Built and `verify_candidate.py`-verified offline on `D:\`
    (`318` unique uploads = `29,169,414,696` bytes + `34` reused `r45-v4` files),
    with conditional four-way UI staged on `claude/exp-variants-ui-20261115`
    (`0ed1ac8`); `Lighter` and CIS-off variants remain outside the live default.
  - *Per-attack TIE switching (`empire-forge-r47-tie` + `empire-tie-switch` `0.2.1`):*
    `0.2.1` (`1,005` C checks + `135` closure parts) passed Sub-Run 1D Part 1
    co-loaded with `clean-exit` `0.2.0` on patched `tie-held-root`, and
    `upstream-pr-eagle-base.patch` is prepared for `>=0.4.1-beta`; stock `0.4.0-beta`
    still lacks `eagle_base`, and live Fighter/Bomber sorties remain for `R2`.
  - *Compact LEGO arms (`lego-compact-leaf-writer` `0.0.9`) & `empire-lego-camera` `0.0.2`:*
    `0.0.2` passed Sub-Run 1A captures, and `0.0.9` passed `15,332` C checks +
    `290`-pose parity + Sub-Run 1E (`r47-lego-compact` ship/ground/return runtime
    check); attended multi-actor co-op remains in `R2`.
  - *Shelved combat tracks:* `at-st` bone-merging, `empire-weapons` (`E-11`/`DLT-19`
    lack `.unit` art), pure-data `mixed-appearances`, and cross-faction Chaos
    spawning (requires 5 RE/SDK extensions).

The owner completed a D11 drop with Heavy/Squad and died; no extraction or
multiplayer result was reported. Subsequent initialization/ship tests do not
extend that qualification. Archives listed as downloaded/imported can still be
disabled. Installing every discussed alternative simultaneously would add known
conflicts; the pinned baseline is the intended combination.
