# Clonedivers Forge Mod Coverage and Pinned Preset

The `build25480438-r5` preset (`Clonedivers/Resources/forge-gameplay-build25480438-r5.json`,
integrated in Windows `2.0.0` / Linux Forge `0.9.0`, awaiting `R1` GitHub release
asset upload and `R2` attended acceptance) consists of three official bundled mods,
**8 required gameplay downloads** (`7,905,326` bytes / `~7.54 MiB`), **6 optional**
project-owned SDK-16 downloads (**5 client-only** `gameplay: false` `coop: per-player`
+ **1 host-authoritative gameplay** `gameplay: true`, totaling `388,939` bytes), and
one selected appearance pack (`Clones 2026.11.19-r20` or `Empire 2026.11.19-r48`;
Regular has none), for **18 mods** with all defaults enabled. The launcher obtains all
**14 downloads** (`8,294,265` bytes / `~7.91 MiB` total before appearance assets)
automatically on first setup with archive/content SHA-256 verification and official
`HellForge.Cli` imports.

Because `hf-mod.com` ended public mod hosting on **19 November 2026**, `build25480438-r5`
contains **zero `hf-mod.com` URLs**:
- **3 permissive (`0BSD` / `MIT`) runtime mods** (`Vanilla Plus Megapack 0.2.1`,
  `Operation Reroller 0.1.6`, `Shared Callback Manager 0.1.0`) and **2 owner-permitted
  MP-hardened SEAF compatibility packages** (`Heavy SEAF Presence Everywhere 1.2.0-clonedivers.4`,
  `SEAF Squad Stratagems 0.1.4-clonedivers.3`) target `forge-gameplay-build25480438-r5`
  on GitHub Releases.
- **3 required data/loader archives** (`Bingus Shared Loader - v20`, `Smarter SEAF 1.0.4`,
  `Smarter Guard Dogs & Sentries 5.2.0`) download directly from their authors' official
  GitHub Releases (`All rights reserved` Th3chef archives are never mirrored or reuploaded).
- **6 project-owned SDK-16 mods** (`clean-exit 0.2.0` on `forge-mods-2026-11-08`, plus
  `lego-compact-leaf-writer 0.1.0`, `empire-lego-yoda 0.2.0`, `empire-atst-writer 0.1.0`,
  `empire-tie-switch 0.3.0`, and `chaos-invasions 0.3.0` targeting `forge-mods-2026-11-19`)
  have full C11 source under `forge-mods/`.

See [SEAF compatibility permission](SEAF-COMPATIBILITY-PERMISSION.md) and
`Clonedivers/Resources/forge-gameplay-build25480438-r5.json` for exact sources, hashes,
versions, order and options.

All 6 optional defaults are enabled once on fresh `r5` setup. When upgrading from `r4`,
enabled prior-catalog defaults (`lego-compact-leaf-writer 0.0.9`, `empire-lego-yoda 0.1.2`,
`chaos-invasions 0.2.0`) upgrade automatically to `0.1.0` / `0.2.0` / `0.3.0` and the
2 newly added `r5` optionals (`empire-atst-writer 0.1.0`, `empire-tie-switch 0.3.0`)
are offered once while preserving any disabled, replaced, or removed optional choices.
Superseded `r4` required IDs (`chef.smarter-seaf` `1.1.0` and standalone
`Mod Options Menu v1.2.1` `pack.95ef276a-6287-465f-ac5b-8512d2227b74`) are disabled
automatically during `r5` setup.

## Gameplay and Client Mod Inventory (`build25480438-r5`)

| Candidate | Owner state and decision | Dependencies / acquisition / redistribution |
| --- | --- | --- |
| HellForge Editor 0.4.2, Hell Forge Difficulty 0.16.1, Vanilla Plus 0.2.0 | Enabled, initialized; bundled pinned baseline | Obtain through official HellForge `0.4.0-beta`; launcher package excludes engine and bundled mods. VP coexists cleanly with `Vanilla Plus Megapack 0.2.1` (which auto-defers overlapping modules when `skyeshade.vanilla-plus` is enabled). |
| Shared Callback Manager 0.1.0 (`local.hf-shared-dispatcher`) | Required in `r5`; verified offline + `mp_sim_test.py` (`gameplay: true`) | Author `enochlophobianumerophobia`; permissive `MIT` mirror on `forge-gameplay-build25480438-r5` (`105,845` B, SHA-256 `543c4f205cde3d4bc7846277c43365f6bd956c62f492effb9b219ecf2e0b5529`, content hash `46adab6cef76bb338aac609ac70bce3b458a4ed010ee633f4a40c772c8127f60`). |
| Operation Reroller 0.1.6 (`local.operation-reroller`) | Required in `r5`; upgrades `0.1.2`; host-gated (`HF_SESSION_JOINER` blocked + demotion cleanup) | Author `enochlophobianumerophobia`; permissive `MIT` mirror on `forge-gameplay-build25480438-r5` (`1,792,536` B, SHA-256 `c085587ab32fe27970884dac03df1898496ecd0b633e201830a1c038616d9ebd`, content hash `71c1ea6214fb45c7f390d3d566103754f8d2b6224d84e5473945690a4d455842`). |
| Vanilla Plus Megapack 0.2.1 (`cowboybingus.vanilla-plus-megapack`) | Required in `r5`; fixes HF `0.4.0` startup crash; includes native Mod Options Menu 1.2.1 & Mod Bindings Menu 2.3 + 23 guarded QoL modules (`0` hook collisions) | Author `CowboyBingus`; permissive `0BSD` mirror on `forge-gameplay-build25480438-r5` (`794,057` B, SHA-256 `5fbbd10e5bca3fe7e1ac226dbb5a2501833eb4adc81c924d1d5dccace49322a8`, content hash `7c470455f012d80b77111ce3ba89fd7e9be3adaeed4d7ecfaf49222bf774ab8f`). Supersedes standalone Mod Options Menu v1.2.1 and Reinforcement Beacons Fixed 4.6.1. |
| Heavy SEAF Presence Everywhere 1.2.0-clonedivers.4 (`local.heavy-seaf-presence`) | Required in `r5`; MP-hardened from `1.2.0-clonedivers.3` (`HF_SESSION_SOLO \|\| HF_SESSION_HOST` gates, 4-slot `LocalUser` lookup, demotion cleanup) | Author `NontoxicKappa`; compatibility archive permitted under owner's written-permission confirmation on `forge-gameplay-build25480438-r5` (`95,313` B, SHA-256 `c35d2ebdba0041caa42372a2c3acaa129eb5f74c23ff5b8bc81074c40fce2397`, content hash `2d7e4f5fb591253b432a43ee580d43a2babb25578d3957a0906469e4b90f114e`). |
| SEAF Squad Stratagems 0.1.4-clonedivers.3 (`local.mod-9fd7880666f6`) | Required in `r5`; MP-hardened from `0.1.4-clonedivers.2` (standalone `host()` gates + host-loss state purge) | Author `JohnnyGrease`; compatibility archive permitted under owner's written-permission confirmation on `forge-gameplay-build25480438-r5` (`330,828` B, SHA-256 `22b4ceffb155c540ce1d0124fb74cf00f737432fb97ad643ff29b7908b14310d`, content hash `d14f424ec4cb0b4c4459fe2cdb2408fd1fcb7167b8c39337348a6c7ff1fb8bdb`). |
| Bingus Shared Loader - v20 (`pack.612eaf70-d682-43c7-9efd-16dcc695f977`) | Required in `r5`; LuaJIT dependency of Lua data mods | Direct author GitHub release (`2,604,118` B, SHA-256 `a42e43e7afe520d0ccd23fe25f9ac5d818c00419d9401de51e59553c87866742`, content hash `666a6247b6c9cd47c14fa2a13f9a80e021058fc085a1704049a5853f3c5c6954`). `0BSD` source plus upstream resource notices. |
| Smarter SEAF 1.0.4 (`pack.f7c3abbe-902c-41a7-8183-057a6ab15768`) | Required in `r5`; post-`hf-mod.com` shutdown contingency replacing `chef.smarter-seaf` `1.1.0` (`File/144`) | Direct author GitHub release `https://github.com/Th3chef/Smarter-SEAF/releases/tag/v1.0.4` (`915,566` B, SHA-256 `f393a03543642b5360c39dcfeb1c48ef34555126c9109a7ab06841d07feebb3a`, content hash `c7d181a91097a219b97e290d23b15280c84a4f8daedde9d4225ff826913f24f9`). All rights reserved — never mirrored or reuploaded (`UPSTREAM-CONTINGENCY.json`). |
| Smarter Guard Dogs & Sentries 5.2.0 (`pack.6f1d2c8e-5a47-4b93-9e0a-3c7b1f4d8a21`) | Required in `r5`; `Targeting Marker` configured `OFF` | Direct author GitHub release `https://github.com/Th3chef/Smarter-Guard-Dogs-and-Sentries/releases/tag/v5.2.0` (`1,267,063` B, SHA-256 `2ca28c425cffdef1d3e2bc6e7db44ab693ed3670d07e23fe6f332d6274d82cb3`, content hash `a79918ad021806ce2653984002e5f163911a224c3b23225b623c0d338f4bf018`). All rights reserved — never mirrored. |
| Clonedivers Clean Exit 0.2.0 (`clonedivers.clean-exit`) | Optional default (`gameplay: false`); clean quits across reported 20-cycle D11 soak | Project-owned SDK-16 C mod (`forge-mods/clean-exit`), published on `forge-mods-2026-11-08` (`8,192` B, SHA-256 `6835354fff8264c6f29c33dfac71a0b6e4c4da7a401920bb76e1b604d2435932`, content hash `0006a02101583cf05e3bd9fa4e431409c18ff4ac72f59ae1ece8c7d9831a888a`). |
| LEGO Compact Leaf Writer 0.1.0 (`clonedivers.lego-compact-leaf-writer`) | Optional default (`gameplay: false`, `coop: per-player`); `61` compact garments (`244` descriptors across Empire + `54` Clone garments); `35,631` `coop_writer_test` checks PASS | Project-owned SDK-16 C mod (`forge-mods/lego-compact-leaf-writer`), targets `forge-mods-2026-11-19` (`237,169` B, SHA-256 `3e56bb219cf2bbb6e5225dd0ffdd715db848bd0ea815723af38b76801fc36cb9`, content hash `8e62f7c7d7d801322aad33b89747479b652f220d7eaa8141be2d333d704caf12`). |
| Empire LEGO Yoda Death 0.2.0 (`clonedivers.empire-lego-yoda`) | Optional default (`gameplay: false`, `coop: per-player`); Empire + `12` Clone torso visuals (`227` `yoda_test` checks + `ScanVisuals == 12/12` PASS) | Project-owned SDK-16 C mod (`forge-mods/empire-lego-yoda`; copyrighted audio excluded from git), targets `forge-mods-2026-11-19` (`53,078` B, SHA-256 `7ecf857d719e190574fc9fe13cfe7e6c0f11126f4465c8bb2796167d4b60056c`, content hash `d896384245eae48b4a883a8cb561daae75cd61a739c9ac20f85ea54be474b043`). |
| Empire AT-ST Film-Scale Leaf Writer 0.1.0 (`clonedivers.empire-atst-writer`) | Optional default (`gameplay: false`, `coop: per-player`); S2c film-scale `EXO-45/49` leaf writer for up to `4` co-op walkers (`< 1.05e-6 m` yawed pose error, `priority = -10` hook chaining with `lego-compact-leaf-writer`, thread-private TLS stack graph, fail-closed) | Project-owned SDK-16 C mod (`forge-mods/empire-atst-writer`), targets `forge-mods-2026-11-19` (`34,001` B, SHA-256 `ce246e9a625d9d8de0f4e6cfdce722f084fc1a449d0eee63e03d7a568dec2f9e`, content hash `cc312a608ca9d3c5b64c232c20b330b814394572a924f24bf3ccefd67ee3051c`). |
| Empire per-attack TIE (lab candidate) 0.3.0 (`clonedivers.empire-tie-switch`) | Optional default (`gameplay: false`, `coop: per-player`); build `25480438` `RefcountedPackageSystem` `eagle_base` fallback + 4-player sortie tracking + coherent `full`/`lighter`/`reduced512` multi-tier SHA-256 closure gate (`8,421` C checks PASS, `18/18` `r48` selections PASS, fail-closed on mixed-tier & non-TIE packs) | Project-owned SDK-16 C mod (`forge-mods/empire-tie-switch`), targets `forge-mods-2026-11-19` (`22,644` B, SHA-256 `491f437bbed022aaf6899b259784156341e6acec80bf1ff5a6116f106da06f5b`, content hash `fe2c081780faecf75bc6f4d4a4e07df7ca9ddc332f32e4a148af4e460d2fbe7b`). |
| Chaos Invasions 0.3.0 (`clonedivers.chaos-invasions`) | Optional default (`gameplay: true`); host-authoritative Dropships/Warp Ships/Breaches on `patch_910`, all-peer preload barrier, host-migration continuity (`225` C + `52` LuaJIT checks PASS) | Project-owned SDK-16 C mod (`forge-mods/chaos-invasions`), targets `forge-mods-2026-11-19` (`33,855` B, SHA-256 `1f5f8a746c7b063e067fb3d9e9e15c4822cfab66dd1366f25578d12943b1b00f`, content hash `cd45f2b705a73e33bbe01d862d3e17b31791661e72014185eb5f1e8014a466d4`). |
| Clear Skies 0.5.4 | Excluded from public distribution & `r5` | `MIT` C source, but bundled `ee6b1ba7e22d71ed.patch_0` is marked not cleared for public redistribution (`hf-mirrors/AUDIT-REPORT.md`). |
| Faster Corpse Cleanup 1.0.6 | Excluded from default co-op preset | `MIT`; lacks `hf->session()` host gate before `native_remove_entity` on co-op joiners and allows per-client `DelaySeconds` slider drift (`hf-mirrors/AUDIT-REPORT.md`). |
| Simple Armor Passive Picker 1.4.2 | Excluded from default co-op preset | `MIT` Lua addon; `read_cache()` gates on `locals == 1` and reads slot `0` (`players + 0xe8`) only (`hf-mirrors/AUDIT-REPORT.md`). |
| Mod Options Menu v1.2.1 & Reinforcement Beacons Fixed 4.6.1 | Retired / automatically disabled by `r5` | Integrated natively inside `Vanilla Plus Megapack 0.2.1`; standalone copies are flagged by `HellForgePreset.IsConflictingAlternative`. |
| Native Smarter SEAF 1.1.0 (`chef.smarter-seaf`) | Retired / automatically disabled by `r5` | `hf-mod.com` `File/144` offline after `2026-11-19` (`All rights reserved`); replaced by official GitHub `Smarter SEAF 1.0.4` (`pack.f7c3abbe-902c-41a7-8183-057a6ab15768`). |
| D11 Mine Boundary Fix 0.1.1 (`local.clonedivers-mine-boundary`) | Retired/disabled on engine `0.1.47` | Engine `0.1.47` supplies the bounds/loading fix natively. |
| Scope Framing 0.3.5, Captain of the S.E.A.F. 4.5.1, Chaos Divers 1.1.0, Cyberstan Rising 1.0.6, Gameplay Reforged, Pick Your Bots/Bugs/Squids, Armored Overhaul, SEAF Companion, Supreme Command | Conflicting / incompatible — blocked before launch | Detected by `HellForgePreset.IsConflictingAlternative` (`SEAF Squad` conflict, `0.3.0`-only, hostile SEAF, friendly Automatons, single-player only, or vehicle/director collisions). |

## Appearance and Qualification Boundaries (`2.0.0` / `0.9.0`)

- **Feeds-last staging:** Root public feeds (`manifest.json`, `manifest-v3.json`, `manifest-v3-current.json`) remain at live `1.14.2` / `r45-v4` until `R1` asset uploads and `R2` attended acceptance complete.
- **Clonedivers `2026.11.19-r20` & EmpireDivers `2026.11.19-r48` (`3` enemy skins × `3` texture tiers × `2` Covenant states):**
  - Both packs support `All CIS Droids`, `Mixed Bots + CIS Droids` (`32` CIS + `45` native Automaton `UNIT`s), and `Native Automatons` across `Full`, `Lighter (Streamed)`, and `Johnson Ultra-Potato (512px)` (`reduced512`), plus opt-in `Covenant` (`1670`, using `patch_1032` mutual exclusion in `r48` against Empire Viper Probe Watcher audio).
  - `r48` adds `100%` build `25480438` Imperial wardrobe coverage (`135/135` armors, `158/158` helmets, `1,939/1,939` `UNIT`s), S2c film-scale AT-ST (`106` nodes `<= 128`, `[8,1,1]` palettes `<= 64`, paired with `empire-atst-writer 0.1.0`), E-Web UV repair, `18` green orbital turbolaser `PFX`s, Brawny+Lean K-2SO (`B-27`), LEGO mid-back prop tuck, and `5,266/5,266` Imperial VO sound slots across `18` banks.
  - `r20` adds `6` Compact LEGO Clone kits (`54` 3P `UNIT`s + `94` hiders, paired with `lego-compact-leaf-writer 0.1.0` and `empire-lego-yoda 0.2.0`), LEGO mid-back prop tuck, and Forge `25480438` `3,246`-key US localization (`18` Clone kit labels).
- **Open live `R2` gates (`runtimeTested: false`):** Offline `verify_candidate.py`, strict C11/LuaJIT/Wwise checks, 5-suite `run-suites.ps1`, isolated `uia-wizard-smoke.ps1`, and 65-check `mp_sim_test.py` pass; attended live desktop `R2` gameplay, multi-PC co-op, AT-ST/TIE/Chaos/Compact-LEGO in-mission checks, weak-PC `512px` telemetry, and Linux Proton sessions remain open.
