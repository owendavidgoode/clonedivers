# Discussed mod coverage — private owner preset

The current 12-mod Clones preset is installed locally. Third-party originals and
private adaptations are not bundled in the launcher review packages. Exact
versions, IDs, order, options and available archive hashes are in preset.json.

| Candidate | Owner state and decision | Dependencies / acquisition / redistribution |
| --- | --- | --- |
| HellForge Editor 0.4.2, D11 0.16.1, Vanilla Plus 0.2.0 | Enabled, initialized; bundled pinned baseline | Obtain through official HellForge 0.3.0-beta; launcher package excludes engine and bundled mods. VP is exclusive with Beacon Fix. |
| Native Smarter SEAF 1.1.0 | Enabled, initialized | Author portal https://hf-mod.com/Mods/Details/79, file 144, SDK 12. Original SHA in preset. Native alternative to Lua Smarter; never enable both. Portal restrictions/source inclusion do not grant redistribution permission. |
| Heavy SEAF Presence Everywhere 1.2.0 | Original failed initialization; private 1.2.0-clonedivers.3 repaired and owner behavior-tested | https://hf-mod.com/Mods/Details/82, file 249, SDK 13. Requires Squad. Original and private hashes in preset. No explicit redistribution grant found; local adaptation acquisition/build path is a release blocker. |
| SEAF Squad Stratagems 0.1.4 (portal label 1.0) | Private 0.1.4-clonedivers.2 enabled; owner call-ins worked in D11 | https://hf-mod.com/Mods/Details/59, file 98, original SDK 9. Registration alone is insufficient; repaired Heavy supplies compatible delivery. No permission inferred from included source. Do not ship private archives. |
| Bingus Shared Loader 20 | Enabled, initialized | https://github.com/CowboyBingus/BingusSharedLoader/releases/tag/v20. LuaJIT dependency of Lua data mods. Exact v20 source LICENSE is 0BSD; THIRD_PARTY.md notes original game Wwise resources in generated releases, so obtain the author archive directly. No loader archive bundled here. |
| Mod Options Menu 1.2.1 | Enabled, initialized | https://github.com/CowboyBingus/ModOptionsMenu/releases/tag/v1.2.1. Original asset digest matches preset; exact-tag LICENSE is 0BSD. Requires Bingus18+. Author-only acquisition remains the rollout policy. |
| Smarter Guard Dogs and Sentries 5.2.0 | Enabled; initialization passed; owner observed yellow markers before OFF fix | https://github.com/Th3chef/Smarter-Guard-Dogs-and-Sentries/releases/tag/v5.2.0; loader 19+, installed 20; options menu optional. All rights reserved/private use, no redistribution. Marker-OFF combat is unqualified. |
| Operation Reroller 0.1.2 | Enabled, initialized; actual reroll behavior unqualified | https://hf-mod.com/Mods/Details/12, file 138. SDK 6+. MIT plus SDK notices; retain attribution. No author digest available; local receipt hash recorded. |
| D11 mine boundary fix 0.1.1 | Enabled; initialized/patched; controlled drop completion unqualified | Project-owned SDK mod; MIT original source. Exact build pin and 20 native checks. No third-party archive/game file required in its source package. |
| Armored Overhaul 3.7.12 | Original rejected with Clones Bastion model conflict; handling-only private trial initialized, OFF | https://hf-mod.com/Mods/Details/15, file 299. SDK 7+; all rights reserved, private adaptation not redistributable. Actual driving/handling not tested; optional isolated trial only. |
| Reinforcement Beacons Fixed 4.6.1 | Imported, initialized, OFF because VP supplies overlapping behavior | https://github.com/CowboyBingus/ReinforcementBeaconsFixed/releases/tag/v4.6.1; loader 18+. Zero-Clause BSD. Each peer must install. Never enable beside VP. |
| Gameplay Reforged Enemy Balance / Gameplay Systems HF profiles | Imported/trialed, OFF; conflicting alternative profiles | Author portal files 177/178. Systems requires Bingus 20/LuaJIT and overlaps SEAF spawners/rosters/stat editors. Portal rights reserved; not an additive default. |
| Pick Your Bots/Bugs/Squids HF port 2.0.1 | Imported, OFF; excluded from squad preset | Author portal file 24 documents single-player limitation. Do not describe this as squad-qualified. |
| SEAF Call-In Stratagem Replacer | Considered; superseded by four native Squad call-ins | Default FRV replacement conflicts with D11 free FRV. Not installed in current squad preset. |
| SEAF Commander 1.3.3 / SEAF Drop | Commander staged; not integrated | Exact Drop dependency remains Ayaka-gated; author account/access required. No bypass, substitute port or fabricated dependency. Also resolve native Smarter ping/targeting overlap before any isolated test. |
| XS1 Goliath 1.0 | Imported/staged, OFF; optional later trial | Actual mech behavior/load/MP unqualified. Core scale-up takes priority. |
| Single-player SEAF Companion | Excluded | Explicit single-player scope conflicts with the intended squad build. |
| Supreme Command | Rejected by owner | Do not add. |

The owner completed a D11 drop with Heavy/Squad and died; no extraction or
multiplayer result was reported. Subsequent initialization/ship tests do not
extend that qualification. Archives listed as downloaded/imported can still be
disabled. Installing every discussed alternative simultaneously would add known
conflicts; the pinned baseline is the intended combination.
