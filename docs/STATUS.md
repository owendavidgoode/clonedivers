# Clonedivers Release and Project Status

## November 27, 2026 (`v2.2.1` / `linux-v0.11.1` live; genuine Difficulty 12 slot enabled by default, AT-ST visibility & Parade Stormtrooper culling fixes `r54`/`r25`, `build25480438-r14`, and automatic low-space backup/export reclamation)

Live public feeds (`manifest.json`, `manifest-v3.json`, `manifest-v3-current.json` `"app"` blocks) and `releases/latest` now serve **Windows `2.2.1`** ([v2.2.1](https://github.com/owendavidgoode/clonedivers/releases/tag/v2.2.1), `Clonedivers.exe` `76,536,302` B, SHA-256 `a5b317dee12b8f68765485ede82be5876bcb3258c35cb18eda40cf03f1a7a5a1`, `Clonedivers-windows-2.2.1.zip` `71,134,020` B, SHA-256 `6865822b3314d6e001efaddf7724d0859294ffea447a23bc982044d82825e020`) alongside **Linux Forge `0.11.1`** ([linux-v0.11.1](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.11.1), `.deb` `61,217,114` B, SHA-256 `f8b1bb0aeaaa72f2b1568104b7882c95e6ad880ea54399ae63030ba224b1ea20`, `.tar.gz` `67,361,360` B, SHA-256 `51aeab204305b7190d179173585b957fbf1f94f9b562cf393c5a52ab446cb7ac`), backed by **EmpireDivers `2026.11.27-r54`** (`SHA-256 73b729e3b2b73b2b4387ab11c618c74f0277c6f8af27488086a4e8a3965ee2f0`, [`pack-2026.11.27-r54-files`](https://github.com/owendavidgoode/clonedivers/releases/tag/pack-2026.11.27-r54-files) `40` culling-fixed delta blobs / `283,075,280` B), **Clonedivers `2026.11.27-r25`** (`SHA-256 23bebcdc0f4ce150bdac54ddf00c9145f0f848c82ab00664d1d7e284d3d1ce28`, [`pack-2026.11.27-r25-files`](https://github.com/owendavidgoode/clonedivers/releases/tag/pack-2026.11.27-r25-files) `2` culling-fixed delta blobs / `16,381,040` B), and **`build25480438-r14`** (`18` archives, `8,567,133` B, `0` `hf-mod.com` URLs, Squad Match Code `r14-4470`, [`forge-mods-2026-11-27`](https://github.com/owendavidgoode/clonedivers/releases/tag/forge-mods-2026-11-27)):

- **Default Clone Wars + Genuine 12th Difficulty Slot (`12: LEVEL 12: CLONE WARS`, `chaos-invasions 0.5.0`):** Extends HellForge's moved `15`-row `0x330`-stride `g_difficultyTable` in place with a genuine 12th carousel slot (`row11 + 0x330`) while keeping `11: HELL FORGE` intact, registers and keeps unlock item `0xCD12C10E` (`copyOf` Super Helldive `0xA0AE653A`) synced in `registered-items.json` across `StratagemsInstalled` rewrites so profiles with Super Helldive (`10`) automatically unlock `12` and raise online cap `48726272 -> 12`, transactionally verifies and rolls back partial prerequisite writes before publishing all `14` `kDifficultyBounds` and `148` objective / `24` operation / `107` mission max-difficulty caps (`11 -> 12`), allocates dedicated `ExtraWidget` index `11` + `12`-entry band/distance tables, routes `Difficulty 12` Galactic War operations into collision-free Region 6 slots (`90..98`) on `<= 6`-region worlds and level-2 city/megafactory slots (`30 + 10r + 1`) on `7`–`8`-region worlds, defaults `HostTheaterPreset` to `"clone-wars"` (`HostTheaterDefaultVersion = 2` one-time migration preserving explicit `v2.2.1+` `"standard"` saves), gates `942.0 m` (`2.79 km²`) + `2,000–3,000` Clone Wars units + `24 s` SEAF drops (`heavy-seaf-presence 1.2.0-clonedivers.7`, preserving continuous SEAF waves on `D1..11` when `"level12-ultrakill"` is forced) + `0.15 s`/`0.0 s` turbo corpse cleanup (`faster-corpse-cleanup 1.0.6-clonedivers.3`) on live active difficulty `>= 12`, and removes `lod_selection_multiplier = "0.6"` from `UltrakillRuntimeProfile` (migrating legacy `v2.2.0` `0.6` configs once from SHA-256-verified `originals/` backups while preserving subsequent player edits) so close-up armor/helmet LODs stay full detail.
- **AT-ST Visibility & Parade Stormtrooper Frustum Culling Fixes (`EmpireDivers r54`, `Clonedivers r25`, `empire-atst-writer 0.2.5`):** Remaps out-of-bounds `LOD[0]` `cull_node 157 -> 83` and visible `MeshObject[4..7]` `mp+48` `cull_node 154..157 -> 80..83` (`< 106` S2c nodes) across all `4` AT-ST walkers (`EXO-45/49/51/55` in `patch_1038`) alongside `< 50 ms` Mega-City burst spawn admission, `524,288` scan ceiling, and sticky SSE `mxcsr & ~0x3Fu` masking in `empire-atst-writer 0.2.5`; repairs Stingray `@48` `LODGroup Footer` (`double_z` world-space AABB on elevated `cull_node`) and `@100` `MeshObject` bounds across `836` Empire `UNIT`s (including `bc20d0b4efff128c` `B-22 Model Citizen` / `Imperial Parade Stormtrooper`) and `74` Clones `UNIT`s across all `5` texture tiers (`10/10` `verify_candidate.py` `VERIFIER_EXIT=0`, `180/180` C# combinations).
- **Automatic Low-Space Backup & Export Reclamation (`ForgeDiskManager`, `PackArtifactEngine`, `HellForgeClient`):** Reclaims completed `"State": "applied"`/`"restored"` pack & `gameplay-*` backups and unneeded staging exports automatically during preflight and post-apply on `< 32 GiB` drives (`14.83 GiB -> 41.05 GiB` verified live on `D:\`) while strictly protecting active and retired `.json.retired-*` `"pending"`/`"outcome-unknown"`/`"recovery-needed"` recovery state (plus unattested `gameplay-*` folders) and failing closed on corrupt transaction JSON. See [v2.2.1](releases/v2.2.1.md) and [linux-v0.11.1](releases/linux-v0.11.1.md).

## November 26, 2026 (`v2.2.0` / `linux-v0.11.0` superseded by `v2.2.1` / `linux-v0.11.1`; EmpireDivers `r53` & Clonedivers `r24` `80/20` `mixed-droids`, `build25480438-r13` zero-syscall SDK-16 refactor, Level 12 ULTRAKILL `2.79 km²` / `3,095`-unit Clone Wars theater & `PackArtifactEngine`)

Live public feeds (`manifest.json`, `manifest-v3.json`, `manifest-v3-current.json` `"app"` blocks) and `releases/latest` now serve **Windows `2.2.0`** ([v2.2.0](https://github.com/owendavidgoode/clonedivers/releases/tag/v2.2.0), `Clonedivers.exe` `76,536,302` B, SHA-256 `d23ca8ed881dc1876a1cf17b6808297917ffe9daa9da221107531ad4799ce7c2`, `Clonedivers-windows-2.2.0.zip` `71,133,515` B, SHA-256 `ea345412a77098f450658d7fca8ce80d80a3b9e1af23ad21e7453587dc7b50e9`) alongside **Linux Forge `0.11.0`** ([linux-v0.11.0](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.11.0), `.deb` `61,221,424` B, SHA-256 `406882766a353a6dcc5e7f8ad1cee9a236a8d05ea512650e17d2eb6674128c05`, `.tar.gz` `67,366,696` B, SHA-256 `47e8ac568d6e740c2721eb8b9d2c1818119fe58e1baa9ebc224debc0e6997a49`), backed by **EmpireDivers `2026.11.26-r53`** (`SHA-256 85e714f9ff130160d84cb1aeecf5afd768219e4f42297788906dd3a588a340b6`), **Clonedivers `2026.11.26-r24`** (`SHA-256 7ef5e5750e6e42d9fd6b4cb2910f2b7de9631ad2be60cdb0e2e6b7d314f8baca`, sharing `52` `cis-mixed-v25` delta blobs / `5,084,990,224` B on [`pack-2026.11.26-r24-files`](https://github.com/owendavidgoode/clonedivers/releases/tag/pack-2026.11.26-r24-files)), and **`build25480438-r13`** (`18` archives, `8,542,113` B, `0` `hf-mod.com` URLs, Squad Match Code `r13-f5a7`, [`forge-mods-2026-11-26`](https://github.com/owendavidgoode/clonedivers/releases/tag/forge-mods-2026-11-26)):

- **`80% CIS Droids / 20% Native Automatons` `mixed-droids` Overhaul (`EmpireDivers r53` & `Clonedivers r24`, `cis-mixed-v25`):** Updates `Mixed Bots + CIS Droids` across all `5` performance tiers (`full`, `lighter`, `reduced1024`, `reduced512`, `potato256`) from ~50/50 (`165` keys) to **`80% CIS Droids / 20% Native Automatons`** (`58/72 = 80.6%` CIS `UNIT`s, `57/71 = 80.3%` enemy classes, `602` resources across 4 tail shards): promotes all **B2 Super Battle Droid Devastators** + **CIS Heavy Shields**, **Dwarf Spider Droid Hulks** (`<= 64`-bone compacted), **IG-100 MagnaGuards**, **MTT Spawners**, and **Sheathipede/Hardcell Fabricators** alongside **B1 Battle Droids**, **Brown STAPs**, **War Striders**, **Brown AAT Tanks & Turrets**, and **HMP Dropships** while keeping **Chainsaw Berserkers**, **Automaton Gunships**, **Factory Striders**, and **Mortar/Bunker/Iron-Fleet heavies** as the `20%` Native Automatons; privatizes all `6` shared `MATERIAL`s (`0` cross-faction overlaps, `10/10` `verify_candidate.py` `VERIFIER_EXIT=0`, `180/180` C# combinations) while reusing 100% of `r52`/`r23` non-mixed blobs (`135`-part `empire-tie-switch 0.3.2` closure byte-exact unchanged).

- **Zero-Syscall SDK-16 Performance Refactor (`build25480438-r13`, `8` Rebuilt Runtime Mods):** Eliminates `810,000/sec` `ReadProcessMemory` syscalls and MXCSR stalls on non-walker draws via a pre-resolved 64-slot seqlock `MeshObject` filter + shared-lock `AtstSlotCache` (`empire-atst-writer 0.2.4`), replaces global 5-second render-remove probe storms with O(1) scoped per-handle retirement (`lego-compact-leaf-writer 0.2.3`), replaces 2,048-slot linear `EntityManager` scans with `6`-syscall bulk snapshots + cached `PAGE_READWRITE` writes (`chaos-invasions 0.4.0`, `faster-corpse-cleanup 1.0.6-clonedivers.2` `2,048`-wreck BSS snapshot, `heavy-seaf-presence 1.2.0-clonedivers.6` mission-paused background scanner), adds O(1) hash-indexed Life lookup (`empire-lego-yoda 0.2.2`), removes mission-time `CreateFileW` disk probes (`empire-tie-switch 0.3.2`), and disarms `SpawnReqDetour` syscalls when unset (`dedicated-melee-slot 1.5.2-clonedivers.3`).
- **Experimental Level 12 ULTRAKILL · Clone Wars Theater (`2,000–3,000` Units Across `2.79 km²`):** Mutates `HfDifficultySettings` row 11 on Solo/Host (`LEVEL 12: CLONE WARS` / `12` HUD hook, role-synced on host/joiner transitions), expands the `2x` mission playable radius from `389 m` (`0.48 km²`) to **`942.0 m` (`2.79 km²`, `5.86x` Super Helldive area)**, uncaps Havok `kPhysicsPools` at boot (`8,192` ragdolls, `262,144` ragdoll actors/motions, `524,288` bodies) and Wwise (`512` voices), activates a `0.0s` `16/frame` Emergency Turbo Corpse Reaper, enforces a fail-closed `< 1,024` `GwNav::Bot` / `1,180`-free-slot `EntityManager` governor + active Director reinforcement valve (preventing the `helldivers2.exe+0x801b03` `1,024`-bot pool exhaustion wall), and pairs `~800–1,040` real `EntityManager`/`GwNav` combat AI with `~2,050` outer Virtual Regiment troopers (`185–225` Republic & CIS/enemy battalions exchanging non-explosive `LAS12_SAI` / `LAS58_TALON` sky-arc laser salvos and promoting into real AI within `185 m`) alongside reversible per-install SHA-256-backed `settings.ini` + `user_settings.config` crowd profiles and symmetric Windows/Linux Lua SEAF scan suppression (`UltrakillRuntimeProfile`).
- **Launcher Deep-Module I/O Refactor (`PackArtifactEngine`, `PinnedPackCatalog`, `HellForgeCommands`):** Consolidates streaming I/O onto `1 MiB` `ArrayPool<byte>` buffers (zero LOH churn), merges copy + SHA-256 into single-pass `CopyAndHashAsync`, adds bounded-parallel `VerifyPackFilesAsync` + a 2-second racy-clean metadata hash cache, caches embedded Star Wars manifests immutably across option switches, and adds `BeginBatch()` CLI status leases + root validation caching.
- **Locked-Seed `PresentMon 2.5.1` Benchmarks, `3,095`-Unit Clone Wars & `r53` `80/20` Live Direct3D Proof (`D:\HellForge`, `7` Clean Final Runs, `0` Crashes/Dumps, `13/13` + `41/41` Files Byte-Exact Restored):** Counterbalanced `A1 -> B1 -> B2 -> A2` Difficulty-10 runs (`Planet 157`, `seed = 3460871878`, `region_tags = [1]`) improved **Avg FPS `+10.6%`** (`99.29` `[100.08, 98.50]` -> **`109.82` `[109.13, 110.50]`**), **Median FPS `+10.7%`** (`101.88` -> **`112.79`**), **`p99` Frame Time `-30.4%`** (`20.38 ms` `[20.75, 20.01]` -> **`14.18 ms` `[15.05, 13.31]`** / `+44.2%` `p99` FPS), **1% Low FPS `+10.8%`** (`41.27` -> **`45.71`**), and **CPU Busy `-9.6%`** (`9.78` -> **`8.84 ms`**); two fresh Level 12 Clone Wars runs (`UK1`, `UK2`, `seed = 1738225598`) sustained **`2,827–3,095` active theater units** (`3,094` & `3,095` peaks; `min_em_free = 1,015` & `1,005`) across `56,426–63,172` Stingray world units at **`59.17` Median FPS** (`[54.82, 63.51]`), **`57.34` Avg FPS** (`[53.06, 61.62]`), and **`27.96` 1% Low FPS** (`[27.45, 28.47]`); and live Difficulty-10 `EmpireDivers 2026.11.26-r53` (`80/20` `mixed-droids`, `seed = 1685575659`) verified CIS B2 Super Battle Droids, DSD1 Dwarf Spider Hulks, and HMP Dropships coexisting cleanly with Native Chainsaw Berserkers at **`108.99` Median FPS** (`105.12` Avg FPS, `clean-quit` `code=0`, `0` dumps). Multi-PC human co-op and native Linux Proton gameplay remain outside single-PC Windows Direct3D scope. See [v2.2.0](releases/v2.2.0.md) and [linux-v0.11.0](releases/linux-v0.11.0.md).

## November 26, 2026 (`v2.1.5` / `linux-v0.10.5` superseded locally by `v2.2.0` / `linux-v0.11.0`; `build25480438-r12` 5-tier `empire-tie-switch 0.3.1`, diagnostics/verifier polish & UIA smoke)

Live public feeds (`manifest.json`, `manifest-v3.json`, `manifest-v3-current.json` `"app"` blocks) and `releases/latest` currently serve **Windows `2.1.4`** ([v2.1.4](https://github.com/owendavidgoode/clonedivers/releases/tag/v2.1.4)) and **Linux Forge `0.10.4`** ([linux-v0.10.4](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.10.4)) while **Windows `2.1.5`** ([v2.1.5](releases/v2.1.5.md)), **Linux Forge `0.10.5`** ([linux-v0.10.5](releases/linux-v0.10.5.md)), and **`build25480438-r12`** (`18` archives, `8,521,273` B, `0` `hf-mod.com` URLs, Squad Match Code `r12-2de0`) are staged and verified locally awaiting release approval:

- **5-Tier Empire TIE Fighter & TIE Bomber Per-Attack Switching (`clonedivers.empire-tie-switch 0.3.1`, `forge-mods-2026-11-26`):** Expands the `135`-part lockstep SHA-256 runtime closure gate (`TIE_TIER_COUNT = 5`) across all **`5` `EmpireDivers 2026.11.25-r52` texture profiles** (`full`, `lighter`, `reduced1024`, `reduced512`, `potato256`), resolving `0.3.0`'s safe fail-closed `off` on `1024px (Medium)` and `256px + Decimated Meshes (Ultra-Potato)` (`25,450` B, SHA-256 `dd800b743546a5e6804eb8eb7a3416594c38f204a8f161e3cbda3699e4cbb952`, ContentHash `a2102818ff5a6eae5da6e2089ad849557da312638c9e18480150dd72cde98952`; **`14,915` C11 checks** + **`5/5` `135`-part tier scans**). Existing `r11` installs automatically upgrade enabled `empire-tie-switch 0.3.0 -> 0.3.1` while preserving opted-out/removed optionals.
- **Launcher Diagnostics, Verifier & UIAutomation Hardening:** Replaces stale hardcoded `"14/14"` (`WindowsForgeHost`) and `"17/17"` (`PreviewForgeHost`) Gameplay Preset health strings with dynamic `18/18 (r12-2de0)`, replaces stale `"r8"` / `"r11"` preset strings in `ForgeVerifier`, `LinuxForgeHost`, and `LinuxForgePlayer` with `SquadFingerprint.ExtractCatalogPrefix()` (`"r12"`), and adds `ActionCardBorder` `ControlAutomationPeer` + `IInvokeProvider` support in `ForgeShellWindow` with full isolated end-to-end UIAutomation smoke coverage (`tools/uia-wizard-smoke.ps1` Pass A/B/C: `ALL UIA SINGLE-WIZARD SMOKE CHECKS PASSED.`).
- **Live Direct3D Multi-Tier `r12` Verification (`D:\HellForge`, `3` fresh `helldivers2.exe` runs on `Planet 157`, `0` crashes/dumps, byte-exact `hellforge-mods.json` restore `D5BA20C2F08B`):** Verified `qualified staged closure parts=135; waiting for Eagle package residency (v0.3.1)` (`0` `off:` lines), `Eagle package=2c26bc4c6592fa14` resident, and all `8/8` `assigned delivery=...` (`strafe`/`110mm` -> TIE Fighter `f62538008a0edc18`, `airstrike`/`500kg`/`smoke`/`cluster`/`gas`/`napalm` -> TIE Bomber `81787742b65c43f4`) live across **`potato256`** (`7.94 GiB` mission Dedicated VRAM, `-49.8%` vs `Full`, `0.24 GiB` Shared GPU, `exitCode=0`), **`reduced1024`** (`9.53 GiB` mission Dedicated VRAM, `-39.7%` vs `Full`, `0.25 GiB` Shared GPU, `3` live SEAF beacon callbacks, `exitCode=0`), and **`full`** (`PresentMon 2.5.1` **`92.12` median FPS**, `44.0` FPS 1% low, `15.82 GiB` mission Dedicated VRAM, live `WM_CLOSE` clean exit in `4.80 s`). Interactive unlocked-desktop `Ctrl+WASD` Eagle beacon throws (`SendInput` blocked behind Windows lock screen `fg_before=0x0` overnight), multi-PC human co-op, and native Linux Proton gameplay remain open outside locked single-PC Windows runs. See [v2.1.5](releases/v2.1.5.md) and [linux-v0.10.5](releases/linux-v0.10.5.md).

## November 25, 2026 (`v2.1.4` / `linux-v0.10.4` live; EmpireDivers `r52`, Clonedivers `r23`, `build25480438-r11` front-Armory overhaul, SEAF-free VO/text & clean Esc exit)

Live public feeds (`manifest.json`, `manifest-v3.json`, `manifest-v3-current.json` `"app"` blocks) and `releases/latest` now serve **Windows `2.1.4`** ([v2.1.4](https://github.com/owendavidgoode/clonedivers/releases/tag/v2.1.4), `Clonedivers.exe` `76,511,726` B, SHA-256 `88070bd3c22e012114c934775e16adbfbf6c57368bbfc5135d95c97891575e1a`, `Clonedivers-windows-2.1.4.zip` `71,110,253` B, SHA-256 `c8fbde7cab60822dabb375abfbd331ae7c14b267d429fc1264b1c390cc49ab15`) alongside **Linux Forge `0.10.4`** ([linux-v0.10.4](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.10.4), `.deb` `61,183,784` B, SHA-256 `81f4dc1c5e27bf79786f8425645f63ba5f7f96894066b0abbb74259311fc2df2`, `.tar.gz` `67,329,984` B, SHA-256 `70c924efbd663087512e7e5ecd7587d98b5006874bae7ebd5f94f07969a226aa`), backed by **EmpireDivers `2026.11.25-r52`** (`SHA-256 878347bbdc3a9ea5493d28c04523f81244b983659b3cc1423e5231dd0b5a2006`, [`pack-2026.11.25-r52-files`](https://github.com/owendavidgoode/clonedivers/releases/tag/pack-2026.11.25-r52-files) `185` delta blobs / `6,525,989,345` B), **Clonedivers `2026.11.25-r23`** (`SHA-256 7c63e43f7e43e71889f2b01098b924586a2845daca1a3e9e6b9aa77fd3beafd5`, [`pack-2026.11.25-r23-files`](https://github.com/owendavidgoode/clonedivers/releases/tag/pack-2026.11.25-r23-files) `108` delta blobs / `3,265,037,891` B, reusing `852` published blobs), and **`build25480438-r11`** (`18` archives, `8,518,467` B, `0` `hf-mod.com` URLs, [`forge-mods-2026-11-25`](https://github.com/owendavidgoode/clonedivers/releases/tag/forge-mods-2026-11-25)):

- **EmpireDivers `r52` & Clonedivers `r23` Front-Armory & SEAF-Free Overhaul (`v24`):** Repairs front-facing (`yaw=176`) helmet, armor, UV, MRA, and skinning defects across `447` Empire `UNIT`s (`RE-1861` ISB Kepi+Visor, `A-9`/`A-35`/`TG-122` clean `4K` Katarn UVs, `TG-8`/`FS-23` Crimson Dagger + arm, `GS-11` AT-AT, `EX-16` Arc-Shield, `FS-37` Fire Trooper, `DP-53`, `CE-67`, `B-27` K-2SO, `CM-09` Compact LEGO) and `85` Clone/Commando `UNIT`s (`B-01` 501st Phase II, `3897706997` `Delta-38` Boss Katarn, `CM-09` Sev/212th, `CW-4`, `DS-10`, `FS-05`, `RS-89`, `cha_seaf` 501st NPC) across all `5` tiers; replaces `591` Whisper-verified VO streams (`0` SEAF/CEEF/SIEF audio leaks across `437` banks / `8,362`–`8,684` native stream wrappers) and scrubs all `20,306` US English strings across all `14` `TEXT` tables (`0` SEAF leaks).
- **Code Mods & Launcher Triage (`build25480438-r11`):** `clean-exit 0.2.1` (`8,401` B, `SHA-256 f74153f38726cd440368daf1574c11d2ad4616518a47cf9fb23001d34dd792df`, `ContentHash 0eadc6cc748a4b7d1ef87fbf704fb0bf1c62fd0fdbfe146a09cf161ec3c9b8f4`) tracks actual `HWND` destruction (`!IsWindow`, ignoring `SW_HIDE`/minimized windows) + loop-stopped top-level `WM_CLOSE` hooks (disarmed if gameplay ticks continue) + an `inplayFaultPending` latch cleared only on normal `Tick()` so In-Game Esc menu **"Quit Game"**, accepted `WM_CLOSE`, and committed `+0x626296` faults exit cleanly with `0` (`16/16` C11 modes); `starwars-localization 0.1.1` (`10,577` B, `SHA-256 b0ff55d0464ee6fa3b72693bd04b0bc28384773af04d21740e4e1f5a5ee9ddba`, `ContentHash 1590c83d7907fbc033d62e3a896d2dda2bddc2c935d3e6acd7715d9bf0dc3362`) diffs all `14` US `TEXT` tables beneath `forge-credits` while globally protecting `Forge Credits` and `WARBOND LOCKED` (`56/56` C11 + real-archive checks); `CrashTriage` + `HellForgeClient` treat `0xB7520003` (`HellForgeStopExitCode`) without crash artifacts as clean and share `*console*.txt` fallback scanning (`FindEngineLogCandidates`); and `r10 -> r11` auto-upgrades enabled `0.2.0`/`0.1.0` optionals.
- **Full QA Verification:** `3/3` `.NET 8` `-warnaserror` Release builds + all 3 test suites (`Clonedivers.Tests`, `Clonedivers.Linux.Tests` `230/230`, `Clonedivers.ForgeBridge.Tests` `319/319`), `forge-lab` `unittest discover`, `12/12` `verify_candidate.py` scenarios (`VERIFIER_EXIT=0` in `asset-registry-v24.json`), `293` `v24` delta blobs SHA-256 verified, all `9` SDK-16 C11/Lua suites, and **`10` fresh Direct3D `helldivers2.exe` processes on `D:\HellForge` (`0` crashes/dumps)**: dual `EXO-49`/`EXO-45` `9.04 m` AT-ST walkers (`walkers=2`, `88,268` substitutions, `leaf_fail=0`), Republic AT-TE + 501st SEAF + Lambda/LAAT/i landings, Mission 1 -> Leave -> Mission 2 `chaos-invasions 0.3.5` `rearms=3` + completed foreign waves across all 3 factions, mid-mission & Ship `WM_CLOSE` clean exits (`exitCode=0`, `stuckQuitting=null`), `potato256` (`7.83 GiB` vs `16.23 GiB` mission Dedicated VRAM, `-51.8%`; `empire-tie-switch 0.3.0` documented as safely fail-closed `off` on `1024px`/`256px`), and a `6`-process `A-B-C-C-B-A` Difficulty-10 `PresentMon 2.5.1` benchmark (`Regular r11` `82.38` FPS `[79.53, 85.23]`, `Clones r23` `72.63` FPS `[77.14, 68.13]`, `Empire r52` `66.32` FPS `[70.46, 62.19]`). Multi-PC human co-op, native Linux Proton gameplay, and manual Eagle TIE keybind throws remain outside single-PC Windows Direct3D scope. See [v2.1.4](releases/v2.1.4.md) and [linux-v0.10.4](releases/linux-v0.10.4.md).

## November 24, 2026 (`v2.1.3` / `linux-v0.10.3` superseded by `v2.1.4` / `linux-v0.10.4`; EmpireDivers `r51`, Clonedivers `r22`, `build25480438-r10` co-op stability, frame-time & polish)

Live public feeds (`manifest.json`, `manifest-v3.json`, `manifest-v3-current.json` `"app"` blocks) and `releases/latest` now serve **Windows `2.1.3`** ([v2.1.3](https://github.com/owendavidgoode/clonedivers/releases/tag/v2.1.3), `Clonedivers.exe` `76,511,726` B, SHA-256 `6c5b7ca489b4909ee532440b7e7b92a5637dd4bc2b7b590c35dc81301beccdd3`, `Clonedivers-windows-2.1.3.zip` `71,109,491` B, SHA-256 `a5e9a40763a8ab076acd4f674e82bd83af9836cf0ae0267d59b54bca83a3b14a`) alongside **Linux Forge `0.10.3`** ([linux-v0.10.3](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.10.3), `.deb` `61,181,086` B, SHA-256 `6a8932d59abc38dcf130f48e9178b39b5d581544db6f09443b74074f8ec45db8`, `.tar.gz` `67,327,679` B, SHA-256 `312c38af9c6d54ddad48864b8c22cc5c2d72058dfc7c74caf61018e95df3410c`), backed by **EmpireDivers `2026.11.23-r51`** (`SHA-256 3192106589a6b914a8fd1f8528f0eba9d0131928323c73756ee4654f520a766a`, [`pack-2026.11.23-r51-files`](https://github.com/owendavidgoode/clonedivers/releases/tag/pack-2026.11.23-r51-files) `195` delta blobs), **Clonedivers `2026.11.23-r22`** (`SHA-256 57423fb7ebe786ede8984a0aa055564e2eee42800ab62d9c00c9cf263d1ad833`, [`pack-2026.11.23-r22-files`](https://github.com/owendavidgoode/clonedivers/releases/tag/pack-2026.11.23-r22-files) `224` delta blobs), and **`build25480438-r10`** (`18` archives, `8,516,435` B, `0` `hf-mod.com` URLs, [`forge-mods-2026-11-23`](https://github.com/owendavidgoode/clonedivers/releases/tag/forge-mods-2026-11-23)):

- **Co-op Crashes & Chaos Invasions (`#37` + Session `280cad4e`):** `chaos-invasions 0.3.5` routes co-op (`player_count > 1`) foreign Automaton waves through ground surges (`SpawnUnit` RPCs) to eliminate unreplicated host `cargo_faction` joiner crashes at `game.dll+0x684a70`, distinguishes inconclusive `PackageStateExe` reads (`-1`) from confirmed unloaded packages (`0`), and resets Chaos-owned `ref_count == 1` slots in-place when `PackageStateExe == 0` so Mission 2+ re-arms foreign preloads cleanly; `ef3a4136b21592cb.unit` Extraction Lambda has deduplicated `MeshGeometry` `mkeys` (`16 -> 15` unique keys, `palette 75 -> 11`), `17` `> 64`-bone `UNIT`s across Empire (`6`), Clones (`5`), and `cis-all` (`6`) are compacted `<= 64`, and `empire_port.py:unit_safe()` fails closed on duplicate `mkeys`, invalid skin indices, empty skin remap tables, and `palette > 64`; `empire-camera-pullback 0.1.4` adds a Proton fallback hook on `helldivers2.exe+0x1c6900` when `MinHook` returns `MH_ERROR_MEMORY_ALLOC (9)`.
- **Launcher, Linux Bridge & Low-Space Recovery (`#37`):** Upfront multi-volume capacity checks (`EnsureExportAndInstallCapacityAsync` / `EnsureHostExportAndInstallCapacityAsync`) preserving `plan.Local` sources during cleanup + automatic orphan reclaim (`ReclaimFailedInstallExport`) preventing `12 GiB` `exports\` leaks; `.bak`/`.lock`/`launcher.json` exclusion from `AccountFingerprint` + capture-matched local receipt preservation across transient Wine/shim errors (`HasLocalImportReceipt`); OS-wide duplicate `HellForge.Launcher` check; unified hex `gameExitCode` (`0xC0000005`) + `crash-*.dmp` + `engine-logs` (`Access violation` / `Crash report` / `[Crash]`) detection (`CrashTriage`, `HellForgeClient`); and `r9 -> r10` automatic upgrade of enabled `0.2.1`/`0.2.0`/`0.2.2`/`0.1.3`/`0.3.4` code mods.
- **AT-ST, Compact LEGO, Assets & Voices (`r51` / `r22`):** `empire-atst-writer 0.2.3` (`4`-byte Stingray topology at `uhdr+0x98` + carrier-link `type=2` / post-unlink `type=0, parent=26` root node `0` admission, throttled incremental `UnitManager` slice scanning eliminating `54 ms` full-table scan spikes, subnormal guards); `lego-compact-leaf-writer 0.2.2` + `empire-lego-yoda 0.2.1` (`79` Compact LEGO garments including all `12` converted Empire stilt LEGO variants such as `CM-09` `LEGO Bikini Stormtrooper`, `30` Yoda visuals, subnormal guards & pre-resolved LOD `mesh` pointers); K-2SO glowing Rogue One photoreceptor eyes; restored `FS-23`/`SA-32`/`TR-62` left forearms; `60` analytical Sutherland-Hodgman clipped Empire helmet rims & `91` satin-plastoid MRAs; upgraded `stormtrooper_helmet_comm` (`2,248` SEAF VO + `3,140` Player VO, `51.37%` studio Stormtroopers in `Mission_SEAF_VO`) + `orbital_comm`; restored `32` `TINY_MESH_AABB` + `6` zero-scaled `@52` + `DS-10` + `6` unmodded Clone helmets; and decoupled `100% unit_safe` `clones-cis-skybox-v23` (`5` CIS *Providence* Dreadnoughts & Vulture Droids) + `clones-vista-lasers-v23` (crimson CIS red vs Republic blue broadsides).
- **Verification:** `3,240` `.NET` assertions passed (`0` failures, `3/3` `-warnaserror` Release builds), `82/82` `forge-lab` Python verifier tests, `1,070+` C11/Lua checks (`314` C11 + `52` Lua + `16` delivery on `chaos-invasions 0.3.5`, `9` C suites + D11 stress bench on `empire-atst-writer 0.2.3`), `12/12` `verify_candidate.py` scenarios (`VERIFIER_EXIT=0` in `asset-registry-v23.json`), and `6` clean awake live Direct3D `D:\HellForge` runs (`exit=0`, `clean=true`, `0` crashes/dumps): active dual `EXO-49`/`EXO-45` `9.04 m` AT-ST walkers (`walkers=2`, `55,648` substitutions, `leaf_fail=0`, `ret=2`), `Immediate Extraction` Lambda (`ef3a4136b21592cb`) & Republic LAAT/i landings (`0` crashes), Republic AT-TE + SEAF Clone Squad, Star Destroyer green `Orbital Laser`, Empire & Clone Compact LEGO Yoda death screams (`result=started`) + `reinforce` (`4.5M+` corrected LEGO frames `failed=0`), Mission 1 -> Leave -> Ship -> Mission 2 `CHAOS_PRELOAD_REARM` + completed foreign waves in both Empire and Clones, and `potato256` (`8.45 GiB` mission Dedicated GPU VRAM vs `16.77 GiB` Full, `-49.6%`, `walkers=1 subs=17,672`, `failed=0`); attended 2-player co-op and Linux Proton gameplay remain verified by unit/static suites + Ubuntu `22.04`/`24.04`/`26.04` CI package QA. See [v2.1.3](releases/v2.1.3.md) and [linux-v0.10.3](releases/linux-v0.10.3.md).

## November 22, 2026 (`v2.1.2` / `linux-v0.10.2` superseded by `v2.1.3` / `linux-v0.10.3`; EmpireDivers `r50` overhaul, `starwars-localization 0.1.0`, `chaos-invasions 0.3.4`)

Live public feeds (`manifest.json`, `manifest-v3.json`, `manifest-v3-current.json` `"app"` blocks) and `releases/latest` now serve **Windows `2.1.2`** ([v2.1.2](https://github.com/owendavidgoode/clonedivers/releases/tag/v2.1.2), `Clonedivers.exe` `76,507,630` B, SHA-256 `1ed477ddfb43783028a658ada4cc5c2957745867409e3ec588306946036f73e7`) alongside **Linux Forge `0.10.2`** ([linux-v0.10.2](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.10.2), `.deb` `61,172,056` B, `.tar.gz` `67,312,101` B), backed by **`pack-2026.11.22-r50-files`** (`209` delta blobs, `444` reused live blobs) and **`forge-mods-2026-11-22`** (`chaos-invasions 0.3.4`, `starwars-localization 0.1.0`), updating `build25480438-r9` (`18` archives, `8,505,056` B, `0` `hf-mod.com` URLs):

- **EmpireDivers `2026.11.22-r50` Overhaul:**
  - **`empire-armors-v22` (`261` `UNIT`s, `12` `MAT`s, `52` `TEX`s):** Un-stretched all `74` JFO helmets, trimmed & normalized `4` oversized BF2/EAW helmets (`RE-1861`, `RS-40`, `IE-12`, `RE-2310`), replaced all `7` stray Republic Clone helmets with Imperial helmets, restored all `10` missing upper-arm configs (`IE-3`, `PH-9`, `AF-50`, `FS-05`), rebuilt `RE-2310` on native crimson UVs (zero black belly holes or spikes), flipped all `10` inside-out mirrored armor pieces, fixed all `11` avatar-rest bind mismatches (`DS-191`, `K-2SO` head, `AD-11`, `O-2`), stripped `K-2SO` hip spikes, expanded all `@48` LOD footers (`0.000 m`), normalized `98` wearable `100×` inverse-bind scales, and restored Democracy Officer `B-01` Stormtrooper shoulders/upper arms + satin plastoid MRA/AO.
  - **`empire-vehicles-v22`:** Replaces 2006 EAW AT-ST with AAA `56,886`-tri *Jedi: Fallen Order* AT-ST + dual `2048×2048` PBR across all 4 Exosuits (`EXO-45/49/51/55`); closes `18f11a4e` SEAF Lambda belly & removes Pelican nose gear; cleans `ef3a4136` Extraction Lambda nose (`front_foot` stripped); replaces all `3` remaining Republic LAATs (`39a445af`, `c392cbb8`, `3f70e350`) with Imperial Lambdas.
  - **`empire-voices-v22` & `empire-sfx-v22`:** Replaces `484` Vader Admiral lines with British RP Grand Moff Tarkin; puts British Imperial cast on all 4 default Player Voice Slots + Crew/Pilots; switches Encrypted Death Trooper Voice to opt-in OFF by default (Slot 4 only); populates `2,680/2,680` SEAF Stormtrooper VO/exertion lines; rebuilds `110` Wwise banks (`5,151` WEMs, `0` codec mismatches) for SEAF E-11 blasters, `PEW-PEW Empire v3.2.3` + remaining vanilla weapons, and *Empire at War* Star Destroyer orbital turbolasers.
- **Runtime Code Mods (`build25480438-r9`):**
  - **`clonedivers.starwars-localization 0.1.0`** (`gameplay: false`, SDK 16): Runtime US `TEXT` diff bridge serving `622` Star Wars overrides beneath HellForge's `forge-credits` patch while preserving Forge Credits (`35/35` C11 checks; live `helldivers2.exe` verified `226` live overrides, `first live override id=0xa6bdcda3 -> "PULSE BLASTER RIFLE"`).
  - **`clonedivers.chaos-invasions 0.3.4`** (`297` C11 + `52` Lua + `16` delivery checks): Fixes D11 Mega City Bot-drop stalling; live D11 Mega City run unloaded `2/2` full `6/6` Bot transport waves cleanly in `7.1 s` each with `0` fallbacks and `0` crashes.
- **Verification:** Live Direct3D showcase (`exit=0`, `clean=true`, `0` crashes/dumps) visually confirmed `16/16` focus armors & helmets rendered complete (catching and fixing a Stingray `@52` `LocalTransform` zero-scale bug prior to release), closed-belly SEAF Lambda + crashed Lambda wreck in daylight, `6/6` official `verify_candidate.py` scenarios passed, `90/90` `r50` combinations validated, and all `.NET` suites (`Clonedivers.Tests`, `Clonedivers.Linux.Tests` `229/229`, `Clonedivers.ForgeBridge.Tests` `312/312`) passed. See [v2.1.2](releases/v2.1.2.md) and [linux-v0.10.2](releases/linux-v0.10.2.md).

## November 22, 2026 (`v2.1.1` / `linux-v0.10.1` superseded by `v2.1.2` / `linux-v0.10.2`; 6 awake live Direct3D runs verified + `empire-camera-pullback 0.1.3` fix)

Live public feeds (`manifest.json`, `manifest-v3.json`, `manifest-v3-current.json` `"app"` blocks) and `releases/latest` now serve **Windows `2.1.1`** ([v2.1.1](https://github.com/owendavidgoode/clonedivers/releases/tag/v2.1.1), `Clonedivers.exe` `76,503,534` B, SHA-256 `c5467efd0b7fe1e73c1b8ca40fe89ba51652121399b97033c39f64b1d837254e`) alongside **Linux Forge `0.10.1`** ([linux-v0.10.1](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.10.1), `.deb` `61,165,776` B, `.tar.gz` `67,308,234` B), updating `build25480438-r8` (`17` archives, `8,494,544` B) to ship live-verified **`empire-camera-pullback 0.1.3`** (`forge-mods-2026-11-22`):

- **Live Direct3D Verification (6 full awake `helldivers2.exe` runs, all `exit=0`, `clean=true`, `0` crashes/dumps):**
  - **D11 Mega City (`Planet 114`, `region_tags=[3]`, `difficulty=11`):** Zero `PrepareMission` OOM crash (`chaos-invasions 0.3.3` spawned live Squid waves post-drop), zero death-cleared FPS bug (`PresentMon 2.5.1`: `35.79` median FPS before death vs `37.61` after death/reinforce), live `heavy-seaf-presence 1.2.0-clonedivers.5`, `faster-corpse-cleanup 1.0.6-clonedivers.1` (`163` removals), and `empire-lego-yoda 0.2.0` (`result=started`).
  - **Camera Pullback Live Fix (`empire-camera-pullback 0.1.3`):** Live D11 testing caught `0.1.2` staying in `cam_searching` after the Hellpod cutscene due to upstream avatar reads exhausting the shared `35 µs` slice budget. `0.1.3` probes `units[0..7]` first with a local `250 µs` discovery budget (`386/386` C unit tests), acquiring `cam_ref=0x00000001` immediately post-drop and applying `11,514` live Compact LEGO frames at `57 µs/frame` (`0` watchdog trips, clean death/respawn re-arm).
  - **Live Armor Sweeps (`204` Armors Photographed on Super Destroyer):** `105/105` `EmpireDivers r49` (+ `8` back views) and `99/99` `Clonedivers r21` visually inspected (`203/204` spotless; zero melted armors, `CW-4` chestplate restored, `CM-10` Fixer spike gone; one minor idle arm-flare on `DS-191 Scorpion` tracked for follow-up).
  - **Live `potato256` VRAM & FPS:** Peak Dedicated GPU VRAM dropped `-48.7%` (`16.75 -> 8.60 GiB`), Shared GPU spillover `-95.6%` (`5.62 -> 0.25 GiB`), Working Set RAM `-44.4%` (`15.49 -> 8.61 GiB`), at `119.9` median FPS (`11.15 ms` P99).
  - **Still Pending Unlocked Desktop Playtest:** In-mission stratagem throws (`EXO-45/49` AT-ST, Clones AT-TE, Extraction Lambda) were blocked by Windows lock screen (`SendInput` `fg_before=0x0`), so those remain verified by offline 3D renders + binary checks and await an unlocked desktop playtest alongside attended multiplayer and Linux Proton tests.
- **CI & Packaging:** PR #35 (`c5ae9ef`) passed `11/11` GitHub Actions checks, and `main` build ([run 45067141817](https://github.com/owendavidgoode/clonedivers/actions/runs/45067141817)) passed Windows + Linux + Ubuntu `22.04`/`24.04`/`26.04` GUI QA. See [v2.1.1](releases/v2.1.1.md) and [linux-v0.10.1](releases/linux-v0.10.1.md).

## November 22, 2026 (`v2.1.0` / `linux-v0.10.0` superseded by `v2.1.1` / `linux-v0.10.1`; 5-tier Star Wars packs `r49`/`r21`, `527` model/rig repairs, `r8` SDK-16 mods & launch UX)

Live public feeds (`manifest.json`, `manifest-v3.json`, `manifest-v3-current.json` `"app"` blocks) and `releases/latest` now serve **Windows `2.1.0`** ([v2.1.0](https://github.com/owendavidgoode/clonedivers/releases/tag/v2.1.0), `Clonedivers.exe` `76,503,534` B, SHA-256 `5c025e8210e0c4d4bedd51007ad087c6c55c420682883de750329c71fe25802e`) alongside **Linux Forge `0.10.0`** ([linux-v0.10.0](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.10.0), `.deb` `61,165,574` B, `.tar.gz` `67,306,830` B), backed by **`pack-2026.11.22-r49-files`** (`397` new blobs), **`pack-2026.11.22-r21-files`** (`303` new blobs), and **`forge-mods-2026-11-22`** (`6` updated `r8` archives):

- **All 5 Live Playtest Bugs Resolved & 527 3D Model/Rig Repairs:**
  - **EmpireDivers `2026.11.22-r49` (`496` `UNIT`s fixed, `509/509` `unit_safe`):** Fixes `49` melted `fmt=29` torsos, `61` cross-limb/kama spike `UNIT`s, 2005 EAW placeholders, Vader/DS-191 defects, solid folded-wing HD Lambda extraction shuttle (`135.53°` wings + dark rear boarding bay), 8-bone `9.04 m` S2c AT-ST across `EXO-45/49/51/55` (below build `25480438`'s 64-bone GPU skinning limit), and Encrypted Death Trooper voice (slot 4).
  - **Clonedivers `2026.11.22-r21` (`31` `UNIT`s fixed, `40/40` `unit_safe`):** Compacts `10` `> 64`-bone units (all 4 AT-TEs `88 -> 3`, LAAT/i `146 -> 3`, SEAF Phase II `94 -> 50/52`, Evac/Tanker/TX-130), restores `CW-4` chestplate, and fixes Fixer `1.428 m` spike.
  - **5 Native Texture Tiers & 3-Way Covenant + Squids Mix:** `full`, `lighter`, `reduced1024`, `reduced512`, and `potato256` (`-80.3%` active GPU VRAM Empire / `-73.3%` Clones), plus `Mixed Covenant + Squids` default.
  - **`build25480438-r8` (`17` downloads, `8,494,004` B, `0` `hf-mod.com` URLs):** Ships O(1) `empire-camera-pullback 0.1.2` (`0.37 µs/frame`), D11 Mega City OOM-safe `chaos-invasions 0.3.3` (zero `PrepareMission` foreign preloads + paced post-drop preload), 4-player co-op `dedicated-melee-slot 1.5.2-clonedivers.2` (`patch_920`), `empire-atst-writer 0.2.2`, host-gated `faster-corpse-cleanup 1.0.6-clonedivers.1`, and `heavy-seaf-presence 1.2.0-clonedivers.5`.
  - **Launcher UX & Safety (PR #34, `83e29ee`):** Unmistakable **`Launching Helldivers 2 — please wait…`** staging UI + secondary **`Cancel Launch`** button across Windows, Linux, and `--classic`, plus automatic Star Wars overhaul-pack collision guard (`HellForgePack.IsStarWarsOverhaulMod`) and `r8-d05d` Squad Match Code.
- **Verification:** `3,221` `.NET` assertions passed (`0` failures, `7/7` Release `-warnaserror` builds), `1,064+` native C11/C++17/Lua code-mod tests passed, `180/180` C# pack selection combinations passed, `12/12` `verify_candidate.py` checks passed (`VERIFIER_EXIT=0`), independent cross-model review (`gemini-4-neon-high`) `APPROVED` (`0` open findings), `11/11` green GitHub Actions CI on PR #34 and `main @ 83e29ee` ([run 44999196686](https://github.com/owendavidgoode/clonedivers/actions/runs/44999196686)), and direct visual inspection of offline posed-skinning renders + Windows/Linux screenshots. Attended live Direct3D playtesting of final `r49`/`r21`/`r8` fixes remains pending. See [v2.1.0](releases/v2.1.0.md) and [linux-v0.10.0](releases/linux-v0.10.0.md).

## November 21, 2026 (`v2.0.3` / `linux-v0.9.3` superseded by `v2.1.0` / `linux-v0.10.0`; simplified Star Wars UX, auto GPU presets, QA fixes & ops)

Live public feeds (`manifest.json`, `manifest-v3.json`, `manifest-v3-current.json` `"app"` blocks) and `releases/latest` now serve **Windows `2.0.3`** ([v2.0.3](https://github.com/owendavidgoode/clonedivers/releases/tag/v2.0.3), `Clonedivers.exe` `76,466,670` B, SHA-256 `2514ba2e7e5a8a6e2eaec15c02ca6a6a898aa94a37b983bcb8186144382c27c0`) alongside **Linux Forge `0.9.3`** ([linux-v0.9.3](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.9.3)):

- **Simplified Launcher UX & QA Fixes (PR #33, `3d04ab1`):** Defaults new installs to the full-variety Star Wars mix with granular options tucked under a collapsed **Advanced** drawer, adds 1-click Apply inside Customize, 1-click `UPDATE & LAUNCH`, `Resume Download` progress pill, 5-tier GPU/RAM auto-detector + VRAM/FPS auto-stepdown banner, `Verify & Repair`, Disk Usage breakdown + safe cache auto-cleaner, plain-English Crash Triage + Cloudflare `/session` & `/crash` Squad Readiness Worker, Host Gameplay Controls (`clonedivers-host-options.json`), Star Wars holoterminal polish + procedural UI sound effects + mute toggle, clickable short What's New, `TrayIcon` + minimize-to-tray, Discord Rich Presence IPC, subtle `r7-xxxx` Squad Match Code, Smart Self-Install from `Downloads`, Linux deterministic `PrepareAsync` progress, `Esc`/`Enter` modal hotkeys, embedded cross-platform `WindowIcon`, visible UI error toast, conditional Azure Trusted Signing CI hook, and nightly upstream-link health check.
- **Verification:** `2,377` automated tests passing (`1,846` Win + `213` Linux + `306` Bridge + `12` Worker), independent adversarial review `APPROVED` (`e596932`), `11/11` green CI on PR #33 and `main @ 3d04ab1` ([run 44979447304](https://github.com/owendavidgoode/clonedivers/actions/runs/44979447304)), and visual inspection of `34` rendered PNGs. See [v2.0.3](releases/v2.0.3.md) and [linux-0.9.3](releases/linux-0.9.3.md).

## November 21, 2026 (`v2.0.2` / `linux-v0.9.2` superseded by `v2.0.3` / `linux-v0.9.3`; unified Avalonia 11 launcher + `build25480438-r7` D11 FPS fix)

Live public feeds (`manifest.json`, `manifest-v3.json`, `manifest-v3-current.json` `"app"` blocks) and `releases/latest` now serve **Windows `2.0.2`** ([v2.0.2](https://github.com/owendavidgoode/clonedivers/releases/tag/v2.0.2), `Clonedivers.exe` `76,368,366` B, SHA-256 `a7150afd90cb6f76c3187b261c478dbc3d858b1688d3f24f1ca4f755d814042f`) alongside **Linux Forge `0.9.2`** ([linux-v0.9.2](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.9.2)):

- **Unified Avalonia 11 Launcher UI (PR #32):** Single-window dark Star Wars launcher shared by Windows and Linux (`--steam` and `--classic` fallbacks retained), reducing Home controls from 24 to 7, adding a slide-over Customize drawer with live pre-click download-size preview, a non-blocking update banner, an in-window 4-stage Guided Setup (multi-drive free-space picker, fixed non-jumping Steam QR card, live auto-advancing 5-step Account Sync tracker, and pre-download texture tier choice), and Settings/Diagnostics with Copy & Save Support Report.
- **Structured Progress Engine & Wave B Fixes:** Real file counts, bytes, MB/s, ETA, `Power.KeepAwake`, Windows taskbar progress (`ITaskbarList3`), and 100ms throttling across gameplay mod staging/install, pack planning/export/backup/import/verify, and Forge upgrades; post-recovery backup pruning (`HellForgeRecovery.cs`); Linux `play` CLI timeout raised 2 min -> 9 min (`OfficialCommands.cs`); Linux installer download progress; and strict Step 4 retry download-size consent matching.
- **D11 Leaf Writer FPS Fix (`build25480438-r7`, PR #31):** Ships `lego-compact-leaf-writer 0.2.1` and `empire-atst-writer 0.2.1` from [`forge-mods-2026-11-21`](https://github.com/owendavidgoode/clonedivers/releases/tag/forge-mods-2026-11-21) (`-34.25%` -> `+4.02%` across 12 live D11 runs + 15-min `S4` soak at `86.09 FPS`). Existing installs auto-upgrade enabled `0.1.0`/`0.2.0` writers.
- **Account Sync Fixes (PR #30, Issue #28):** Disables HellForge `autoApply` before sync, caches Linux Steam VDF users cleanly, suppresses background probes during active session SDK shims, and avoids duplicate HellForge manager windows.
- **Verification:** Independent Claude Opus 5.5 reviews (`APPROVED` on PR #31 `aba4496` and PR #32 `458c13a`), `11/11` green CI checks on PR #31 and PR #32 (Windows + Ubuntu `22.04`/`24.04`/`26.04` upgrade & GUI smoke), and visual inspection of 26 rendered PNGs (`100%` & `150%` DPI). See [v2.0.2](releases/v2.0.2.md) and [linux-0.9.2](releases/linux-0.9.2.md).

## November 20, 2026 (`v2.0.1` / `linux-v0.9.1` superseded by `v2.0.2` / `linux-v0.9.2`; `build25480438-r6` D11 1-FPS leaf-writer hotfix)

Live public feeds (`manifest.json`, `manifest-v3.json`, `manifest-v3-current.json`
`"app"` blocks) and `releases/latest` now serve **Windows `2.0.1`** (`v2.0.1`,
`Clonedivers.exe` `66,658,902` B, SHA-256 `c6b9b90f651325dbd5c4f33e2386382a3a32649fb15f87058c3fa505639ba659`)
alongside **Linux Forge `0.9.1`** (`linux-v0.9.1`), upgrading the gameplay catalog to
**`build25480438-r6`** (`14` downloads, `8,298,489` B) with both `0.2.0` leaf writers
from `forge-mods-2026-11-20`:

- **`clonedivers.lego-compact-leaf-writer` `0.2.0`** (`240,032` B, SHA-256 `e26ccf4100d01e51fd4afa2ae2dcd6264393b9aafaa2e1a0a37b3cc98dcf3393`, ContentHash `907f07bc93478bc56a5ad881382246af9d3907534e2fa5065a0e99e882e09c8b`):
  Replaces global `SRWLOCK` acquisition and `UnitManager` scanning in `512B80` with a
  64-byte-aligned lock-free seqlock fast filter (`__atomic_load_n` acquire loads, zero
  shared atomic writes on reject), moves garment discovery to 250 ms `WriterTick` with
  O(active) point reads and bounded 16,384-slot incremental slices, checks O(4) player
  roster avatars first in `coop_find_slot_ptr`, and gates per-invocation `.bin` capture
  behind `.arm` opt-in (eliminating log-budget `fatal reason=3` during heavy combat).
- **`clonedivers.empire-atst-writer` `0.2.0`** (`35,362` B, SHA-256 `6095726add3aafd351cc8c6269c42ebdb4ba9c691c56a9772bc1d5db63a9857c`, ContentHash `c908db59cdf6e3d6091b7403820bb22f6e14e4222be7244fc3527a4ee8d3f6aa`):
  Adds a 64-byte-aligned lock-free seqlock fast filter (zero locks and zero memory reads
  when 0 walkers are active), removes `at_registry_reconcile` from `512B80`, and scans a
  smooth 2,048-slot per-frame rolling slice with a 56-byte vtable/resource pre-check and
  per-slot fallback on bulk read failure.
- **Migration & verification:** Existing `2.0.0` / `0.9.0` (`r5`) installs auto-upgrade
  enabled `0.1.0` leaf writers to `0.2.0` while preserving explicit opt-outs. Verified by
  C unit suites (`46,456` LEGO checks including Test 17 D11 262,144-slot zero-lock/zero-scan
  guard + `16,087` leaf checks + `7/7` AT-ST tests), independent Claude Opus 5.5 review
  (`APPROVED`), and all `11/11` GitHub Actions CI jobs (PR #29). Live in-game D11
  frame-time capture remains to be confirmed in attended gameplay. See
  [v2.0.1](releases/v2.0.1.md) and [linux-0.9.1](releases/linux-0.9.1.md).

## November 19–20, 2026 (`v2.0.0` / `linux-v0.9.0` superseded by `v2.0.1` / `linux-v0.9.1`): Turnkey 4-Step Wizard, `r48` Empire, `r20` Clones & `build25480438-r5`

Live public feeds (`manifest.json`, `manifest-v3.json`, `manifest-v3-current.json`
`"app"` blocks) and `releases/latest` now serve **Windows `2.0.0`** (`v2.0.0`,
`Clonedivers.exe` `66,658,827` B, SHA-256 `ab84ca131701bb8c88f8590bc53c8b4de2b1e88ea540f46cc99b8995d7449b6b`)
alongside **Linux Forge `0.9.0`** (`linux-v0.9.0`), backed by verified `R1` asset
stores (`653/653` pack/manifest URLs and `14/14` gameplay URLs HTTP 200 with exact
size and SHA-256). Legacy root `"pack"` blocks remain untouched for backwards
compatibility with older launchers while `2.0.0` / `0.9.0` use their embedded `r48`
and `r20` catalogs. Attended `R2` multiplayer and Linux Proton tests remain in
progress. This release integrates the full v2.0 turnkey UX, post-`hf-mod.com`
**`build25480438-r5`** gameplay preset, **6 project-owned SDK-16 runtime mods**,
**18-combination EmpireDivers `2026.11.19-r48`**, and **36-combination Clonedivers
`2026.11.19-r20`**:

- **Turnkey Single Setup Wizard & UX (`F1`–`F16`):** Consolidates onboarding into a
  4-step modal wizard (`Step 1 Preflight` -> `Step 2 Forge & Game Copy` with in-app
  Steam Mobile QR + Official fallback -> `Step 3 Mandatory Steam Account Sync` with
  `hosts` capture-proxy detection -> `Step 4 Pack Download & Ready` with first-launch
  firewall notice), single-instance window activation (`F15`), fast-crash surfacing
  (`F6`), and unified **3 Enemy Skins** (`All CIS Droids`, `Mixed Bots + CIS Droids`,
  `Native Automatons`) × **3 Texture Tiers** (`Full`, `Lighter (Streamed)`,
  `Johnson Ultra-Potato (512px)` `reduced512`) × **Covenant** (`1670`) controls across
  Windows, Linux (`ForgeWindow`), and `HellForge.Launcher.ClonediversBridge`. See
  [v2.0.0](releases/v2.0.0.md) and [linux-0.9.0](releases/linux-0.9.0.md).
- **Gameplay catalog `build25480438-r5` (`14` downloads, `8,294,265` bytes, `0` `hf-mod.com` URLs):**
  - Eliminates `hf-mod.com` URLs on its `2026-11-19` public-download shutdown:
    mirrors permissive `0BSD`/`MIT` runtime mods (`cowboybingus.vanilla-plus-megapack`
    `0.2.1` `794,057` B fixing HF `0.4.0` startup crash and integrating `Mod Options
    Menu 1.2.1` + `Mod Bindings Menu 2.3`, `local.operation-reroller` `0.1.6`
    `1,792,536` B host-gated `HF_SESSION_JOINER` safe, and `local.hf-shared-dispatcher`
    `0.1.0` `105,845` B) plus owner-permitted MP-hardened SEAF packages
    (`local.heavy-seaf-presence` `1.2.0-clonedivers.4` `95,313` B and
    `local.mod-9fd7880666f6` `0.1.4-clonedivers.3` `330,828` B) on
    `forge-gameplay-build25480438-r5`, while pinning `Bingus Shared Loader v20`
    (`2,604,118` B), `Smarter SEAF 1.0.4` (`915,566` B, official `Th3chef/Smarter-SEAF`
    `v1.0.4` contingency), and `Smarter Guard Dogs & Sentries 5.2.0` (`1,267,063` B)
    directly to author GitHub Releases (`All rights reserved` never mirrored).
  - Ships **6 project-owned SDK-16 optional defaults** (`388,939` B, full source in
    `forge-mods/`): `clonedivers.clean-exit` `0.2.0` (`8,192` B),
    `clonedivers.lego-compact-leaf-writer` `0.1.0` (`237,169` B, `61` Empire + Clone
    garments, `35,631` `coop_writer_test` checks PASS), `clonedivers.empire-lego-yoda`
    `0.2.0` (`53,078` B, Empire + `12` Clone torsos, `227` `yoda_test` checks PASS),
    `clonedivers.empire-atst-writer` `0.1.0` (`34,001` B, S2c film-scale `EXO-45/49`
    leaf writer for up to 4 co-op walkers), `clonedivers.empire-tie-switch` `0.3.0`
    (`22,644` B, build `25480438` `RefcountedPackageSystem` `eagle_base` fallback +
    4-player sortie tracking + multi-tier closure gate, `8,421` C checks PASS, fail-closed on non-TIE packs),
    and `clonedivers.chaos-invasions` `0.3.0` (`33,855` B, `gameplay: true`,
    host-authoritative Dropships/Warp Ships/Breaches + preload barrier + host-migration
    continuity, `225` C + `52` LuaJIT checks PASS).
  - Excludes `clear-skies-0.5.4.zip` (bundled `.patch_0` not cleared for public
    redistribution), `faster-corpse-cleanup-1.0.6.zip` (missing `hf->session()` host
    gate on joiners), and `simple-armor-passive-picker-1.4.2.zip` (slot-0-only
    `read_cache()`). Upgrades enabled `r4` defaults automatically while preserving
    disabled/removed choices and disabling superseded `chef.smarter-seaf` and
    standalone `Mod Options Menu`.
- **EmpireDivers `2026.11.19-r48` (`patch_1000..1036` `111` files / `patch_1000..1032` `99` files across `18` combinations, SHA-256 `fea705bb80b4d76f328df2e5b37038665b03c074bcc938f8fb8cc8751e0cda73`) & Clonedivers `2026.11.19-r20` (`patch_0..37` `114` files / `patch_0..33` `102` files across `36` combinations, SHA-256 `8f6fbffe3392e37e5d07f44d4101740ae766ad4cb098e7c505562b5aee4a96be`):**
  - `r48` integrates `100%` build `25480438` Imperial wardrobe coverage (`135/135`
    armors, `158/158` helmets, `1,939/1,939` `UNIT`s, `0` empty body slots across
    all `270` Brawny+Lean armor configs and `158` helmets, `344` US text keys), S2c
    film-scale AT-ST (`106` nodes `<= 128`, `[8,1,1]` palettes `<= 64`, `9` native
    `PHYSICS` restored, `8` child weapons), E-Web UV repair, `18` green orbital
    turbolaser `PFX`s, Brawny+Lean K-2SO (`B-27`), LEGO mid-back prop tuck, `5,266/5,266`
    Imperial VO sound slots across `18` banks (`99.68%` Whisper `large-v3` ASR `>= 0.85`),
    co-op-safe `mixed-droids`, `reduced512` (`5,457,581,826` B / `2,911,650,998` B
    resident GPU vs `11,778,299,344` B / `9,981,221,504` B Full on `all-droids`), and
    `Covenant` (`patch_1032` mutual exclusion + shadow `m_shadow` repair). Visual QA
    pass fixed emissive visor RGB tinting, dark albedos/tangent-frame normals, `pt=1`
    clipping vs partial `r47` underlayers, Delta Commando skinning/normals/waist, BF2
    helmet orientation, Royal Guard pike/arm trim, AT-AT Driver/TIE Pilot spine
    alignment, Jumptrooper back-pack placement, Lord Vader (`UF-50`) helmet/torso/limbs,
    `O-44` Compact LEGO helmet, `TR-62`/`FS-23` helmet AABB culling, `CW-22`/`CW-36`
    legacy giant-LEGO removal, and `RS-20` leg coverage.
  - `r20` integrates `6` isolated Compact LEGO Clone kits (`54` 3P `UNIT`s + `94`
    hiders; helmet decal UV channel `uv1 -> uv0` and Phase I Shiny right-leg print
    alignment fixed and proved live across all `6/6` kits), LEGO mid-back prop tuck,
    Forge `25480438` `3,246`-key US text table (`18` Clone strings), `mixed-droids`,
    `reduced512` (`4,770,274,498` B vs `9,382,856,140` B Full on `commandos + all-droids`),
    and `Covenant`. Vault deduplication across all `54/54` verified scenarios produces
    `653` unique blobs (`50,167,273,994` B).
- **Verification level reached (`runtimeTested: false` until attended `R2` passes):** Official `verify_candidate.py` passed across all **`54/54`** `r48` & `r20` matrix combinations (`VERIFIER_EXIT=0`), `run-suites.ps1` (`0` failures, `0` warnings across `windows`, `bridge`, `linux-steam`, `linux-forge`, `forge-lab`), isolated `tools/uia-wizard-smoke.ps1` (`ALL UIA SINGLE-WIZARD SMOKE CHECKS PASSED`), and `mp_sim_test.py` (`65` 4-peer simulation checks PASS). Automated live `helldivers2.exe` verification passed Wave 3 (`10/10` runs) plus a 4-sweep visual audit (`0` crashes, dumps, or `writer` `failed` counts; `fatal=0` on final-catalog sweeps; `sweep1`/`sweep2` hit the diagnostic log-budget cap after cycling multiple LEGO kits with `math_fail=0`/`leaf_fail=0`): `20` Clonedivers `r20` kits (all `6/6` Compact LEGO Clone kits + `14` realistic Clone kits on the final `8f6fbffe...` catalog, plus `3` kits on `reduced512`), `48` EmpireDivers `r48` kits on pass-1 `Full` followed by `18` pass-2/legacy kits on the final `fea705bb...` `Full` catalog and `5` kits on final `reduced512`, unfolded E-Web, `Mixed Bots + CIS`, `Covenant 1670`, `chaos-invasions 0.3.0`, `Heavy SEAF`, `empire-tie-switch 0.3.0` (`135` runtime closure parts verified, `eagle_resident=true`, `8/8` deliveries on `Full` and `reduced512`), and live `empire-lego-yoda 0.2.0` firing, plus a 6-run `PresentMon 2.5.1` benchmark on a single high-VRAM test PC (`1280x720`) proving `Johnson Ultra-Potato 512px` cuts Peak Dedicated VRAM `-40.6%` (`18,154.53 -> 10,784.25 MiB`), Shared GPU spillover `-96.0%`, WorkingSet RAM `-44.5%`, and Boot-to-Ship `-27.7%` (background average FPS did not improve in this test). Attended live desktop `R2` checks (EXO-45/49 walker pod call-in, live Eagle TIE beacon throws, speaker voice mix, Empire Covenant (`patch_1032`) coexistence, Phase I Shiny and 212th Clone kits, weak-PC (low-VRAM) `512px` frame-time, 4-peer human co-op & host migration, clean-PC wizard run, and Linux Proton gameplay) remain required before feed promotion.

## November 16, 2026 (`v1.14.2` / `linux-v0.8.2` live; `R1` published, `R2` pending): 4-way Compact LEGO Empire r47 + `build25480438-r4`

Live public versions are **Windows 1.14.2** (`v1.14.2`) and **Linux Forge 0.8.2**
(`linux-v0.8.2`), backed by published `R1` prerelease asset stores
**`pack-2026.11.09-r47-files`** (`318` assets, `29,169,414,696` bytes) and
**`forge-mods-2026-11-15`** (`3` assets, `274,640` bytes) plus `34` reused
`pack-2026.11.08-r45-v4-files` assets (`455,980,288` bytes); attended `R2`
acceptance checks remain pending. **Windows 1.14.2** / **Linux Forge 0.8.2**
integrate the Wave 1 + `c90673f` launcher polish, all **4 Compact-LEGO-baked
`r47` Empire variants**, and **`build25480438-r4`** with all **4 project-owned
optional mods** (while `manifest*.json` `"pack"` blocks remain byte-for-byte unchanged):

- **Launcher polish (PR #25, `claude/polish-20261115` @ `c90673f`):** Integrates
  20 confirmed Wave 1 storage, Windows UI/Warbond, and Linux/bridge polish fixes
  alongside Wave A/B1 hardening (`70 GB` setup budget in `HellForgeSetup.cs` /
  `HellForgeGameSetup.cs`, resumable HTTP Range `.partial` appearance downloads,
  completed-backup reuse, atomic export receipts, resumable pruning, cross-target
  installer migration, sticky operation failures, deferred Warbond retry, read-only
  background polls, `pasta` guidance, and `c90673f` skipping a missing `<root>/mods`
  folder during fresh Linux `PlanAppearanceAsync`), plus headless `xvfb` Linux GUI
  smoke checks. All `11/11` GitHub Actions jobs pass (Windows and Ubuntu
  `22.04`/`24.04`/`26.04`) and Windows `run-suites.ps1` reports `0` failures. See
  [v1.14.2](releases/v1.14.2.md) and [linux-0.8.2](releases/linux-0.8.2.md).
- **`r47-shuttles` Full candidate and `W4` A/B gate (PASS):** Built offline at
  `D:\ClaudeWork\empire-forge\empire-forge-r47-shuttles` (`102` files,
  `8,738,709,413` bytes; `96` archive files = `8,738,707,008` bytes, `6,880`
  resources), overlaying the SEAF personnel Lambda `+0.200000 m` ground-clearance
  repair (`lambda-fix` `9ba626afa44a3aa3.patch_1030` + `.gpu_resources`,
  `+4,138,768` bytes) onto `empire-forge-r47` while retaining extraction Pelican
  `ef3a4136b21592cb` unchanged. Offline `verify_candidate.py` passes, and the Wave 2
  **`W4` `12+12` D11 Automaton A/B gate passed (`gate_pass=true, verdict=PASS`)**:
  Arm A (`r45-v4`) completed `12` cycles (`9` qualifying, `2,095.672s` / `97.02%`,
  `11/12` returns = `91.67%`, peak VRAM `30,020 MiB`) with `1` crash event
  (`helldivers2.exe+0x1e6b3e` in `block05 c1`, `0.477` per 1000s); Arm B
  (`r47-shuttles`) completed `14` cycles (`9` qualifying with `2` automatic top-ups,
  `1,875.498s` / `86.83%`, `14/12` returns = `116.67%`, peak VRAM `30,211 MiB`)
  with **`0` crash events (`0.000` per 1000s)**. Sub-Run 1A (`05-seaf-lambda-rear.png`)
  verified the SEAF Lambda `135°` upward wing fold cleanly in flight; landed visual
  ground clearance awaits attended `R2`. See
  [r47 Full notes](releases/pack-2026.11.09-r47-full.md).
- **4-variant Compact-LEGO-baked `r47 (+ lambda-fix)` Empire catalogs & UI (`1.14.2` / `0.8.2`):**
  All four Compact-LEGO-baked variants (`Full`, `nodroids`, `Lighter`,
  `nodroids-lighter`) passed `verify_candidate.py` (`VERIFIER_EXIT=0`, `0`
  findings): `Full + Droids` (`8,738,707,008` archive bytes, `62` `r47` + `34`
  reused `r45-v4`), `Full + Native` (`6,013,620,208` archive bytes, `64` `r47` +
  `32` `r45-v4`), `Lighter + Droids` (`8,961,480,892` archive bytes, `2,542,440,456`
  GPU vs `7,934,299,136` Full GPU), and `Lighter + Native` (`6,172,775,164`
  archive bytes). Embedded catalogs `hellforge-empire-r47.json` (`160` rows) and
  `hellforge-empire-r47-lighter.json` (`352` rows) plus 4-way UI routing (`SelectionKey`
  `r47/25480438/empire/{Texture}/{Droids}/False`) are integrated across Windows,
  Linux, and Bridge (`318` unique `pack-2026.11.09-r47-files` assets =
  `29,169,414,696` bytes + `34` reused `r45-v4` assets = `455,980,288` bytes).
- **Gameplay catalog `build25480438-r4` & 4 project-owned optional mods (`11` downloads, `8,063,255` bytes):**
  - `clonedivers.clean-exit` `0.2.0` (`8,192` bytes, `gameplay: false`, live on
    `forge-mods-2026-11-08`).
  - `clonedivers.lego-compact-leaf-writer` `0.0.9` (`199,207` bytes, SHA-256
    `34e683e503d525ea5d419bb3477cb0f44e9b9f6cb3eed3c0ae4daebbf55b3dfa`, content hash
    `71d0bbb0d550b6c312f7d660941c3b620821279c51be47d71d205eb67bedbee3`, `gameplay: false`,
    `coop: per-player`): passed `15,332` `coop_writer_test` checks, `9/9` C suites,
    `290`-pose parity (`max_arm_world_err = 5.55e-16`), and **Sub-Run 1E (`r47-lego-compact`
    + `0.0.9`)** cleanly (`clean=true, exit=0, dumps=[]`, `admissions=86, active_targets=28,
    corrected=90832, failed=0, fatal=0`).
  - `clonedivers.empire-lego-yoda` `0.1.2` (`45,907` bytes, SHA-256
    `3d41fe4b6c4f49c52df786a8f83e1e9a1ff501aa3c14edfe742126069aaf9b18`, content hash
    `2e78173e56b6cc752ac27d810a7a6e7b72352ebe24437edae3a49b7d580f78dc`, `gameplay: false`,
    `coop: per-player`): accepts both Compact LEGO and stilt (`r45`/`r47-shuttles`)
    `0xBB11C8041C14C747` torso hashes (`yoda_test.exe` `139` checks, `ScanVisuals == 4`
    on both packs; `0.1.1` passed Sub-Run 1A death routing; copyrighted audio excluded
    from git source).
  - `clonedivers.chaos-invasions` `0.2.0` (`29,526` bytes, SHA-256
    `42498666f228ea433d4ee63fc0447b7f2ec3033893f2012a9b1349106c7d4d41`, content hash
    `4c941838b63ce54291a7b8571f21e61817cce66e3548cd413705dfd9101d570b`, `gameplay: true`):
    playable cross-faction Chaos Mode (13-RVA 32-byte code pins, all-peer `<75%` curated
    foreign preloading, Host/Solo Automaton Dropship / Illuminate Warp Ship / Terminid
    Breach wave spawner, 250ms `BHV_PERCEPTION` mutual-hostility narrowing leaving
    `BHV_FACTIONS` intact); passed `195` C checks, `52` LuaJIT menu checks, and `15`
    delivery verification checks (`chaos-invasions-0.2.0.zip`). Live 3/4/5-way combat
    and co-op sync remain in `R2`.
  - All 4 optional mods are enabled by default on fresh `r4` setup and newly offered once
    on `r3` -> `r4` upgrades while preserving prior player opt-outs.
- **Non-shipping / held boundaries (`1.14.2` / `0.8.2`):**
  - `empire-tie-switch` `0.2.1` (`13,740` bytes, SHA-256 `a61ed213...`; `1,005` C
    checks + `135` closure parts) **passed Sub-Run 1D Part 1** co-loaded with
    `clean-exit` `0.2.0` on patched `tie-held-root` (`0` `+0x83dc` crashes,
    `Eagle package=2c26bc4c6592fa14 state=2`, `8/8 assigned delivery=`, `clean=true`).
    `upstream-pr-eagle-base.patch` is prepared for `>=0.4.1-beta`; stock `0.4.0-beta`
    cannot mount `eagle_base` directly, so TIE switching remains outside `build25480438-r4`.
  - `empire-lego-camera` `0.0.2` (Sub-Run 1A capture helper) and `chaos-probe` `0.1.0`
    (Sub-Run 3 `3x` D5 diagnostic) are lab-only and not shipped in `r4`.
  - **`at-st` Hulk rig** (`88` palettes / `198` nodes vs native `<=64` / `<=128` limit),
    **`empire-weapons`** (`E-11`/`DLT-19` lack `.unit` art), and **`mixed-appearances`**
    recipe remain shelved offline and do NOT ship in `1.14.2` / `0.8.2`.
- **Release preflight (Gate 3 and dry run):** Live catalog audit passed (`r19`
  `966/966`, `r45-v4` `96/96`, `build25480438-r3` `8/8`; `0` issues). Wave 2
  `publish-r1-assets.ps1` dry run verified `318` variant assets (`29,169,414,696`
  bytes) + `34/34` reused `r45-v4` assets (`455,980,288` bytes) + `lego-compact-leaf-writer`
  `0.0.9`; `forge-mods-2026-11-15` additionally ships `empire-lego-yoda-0.1.2.zip`
  (`45,907` bytes) and `chaos-invasions-0.2.0.zip` (`29,526` bytes) upon Wave 3 `R1`
  upload approval and `R2` attended acceptance.

## November 9–10, 2026: Clonedivers 1.14.1 and Linux Forge 0.8.1 published

Published **Windows 1.14.1** (`v1.14.1`: `Clonedivers.exe` `66,637,372` bytes,
SHA-256 `abf38712eec9ab83b26d7dca1d079a4a0cdcd967690ca08f9a0090169aff47f0`;
`Clonedivers-windows-1.14.1.zip` `61,269,000` bytes) and **Linux Forge 0.8.1**
(`linux-v0.8.1`: `Clonedivers-linux-x64.deb` `60,884,032` bytes;
`Clonedivers-linux-x64.tar.gz` `67,002,356` bytes) with bridge `0.4.0`,
activating `manifest*.json` app feeds at `2aa3806` (PR #22 `9d76400`, README
links updated in PR #23 `44abd2f`). Both UIs moved to one primary action per
state, kept Clones `r19` and published Empire `r45-v4` Full, preserved unavailable
saved Empire choices until explicit reset, and added retry/cache-busting fallback
on downloads. Draft PR #24 (`r47 Full`) was held pending live qualification. See
[v1.14.1](releases/v1.14.1.md), [linux-0.8.1](releases/linux-0.8.1.md), and
[release summary](RELEASE-LAUNCHER-ONLY-2026-11-09.md).

## November 8, 2026: Clonedivers 1.14.0, Linux 0.8.0, EmpireDivers r45-v4, and Clean Exit 0.2.0 published

Published **Windows 1.14.0** (`v1.14.0`), **Linux 0.8.0** (`linux-v0.8.0`),
**Clonedivers Clean Exit 0.2.0** (`forge-mods-2026-11-08`: `clean-exit-0.2.0.zip`
`8,192` bytes, SHA-256 `6835354fff8264c6f29c33dfac71a0b6e4c4da7a401920bb76e1b604d2435932`),
and **EmpireDivers Forge `2026.11.08-r45-v4`** (`pack-2026.11.08-r45-v4-files`: `96`
archive files / `8,435,474,224` bytes + `manifest.json` + `CREDITS.md`). Added
gameplay catalog `build25480438-r3` (`8` downloads: `7` required + optional Clean
Exit `0.2.0`), automatic owned premium Warbond opening on import, guided game
setup (`install-game` / `steam-download` QR), and the `r45-v4` inline cape repair
(3/3 live D11 Windows drops with clean quits). See [v1.14.0](releases/v1.14.0.md),
[linux-0.8.0](releases/linux-0.8.0.md), [r45-v4 notes](releases/pack-2026.11.08-r45-v4.md),
and [QA sweep](QA-SWEEP-2026-11-08.md).

## November 4–5, 2026: Forge migration (1.10.0–1.13.0, Linux 0.6.0–0.7.0) and HellForge 0.4.0-beta integration

Replaced the legacy Steam-only launcher flow (`--steam` retained for recovery) with
separate **HellForge `0.4.0-beta` / engine `0.1.47` / SDK `16` / game `25480438`**,
native Linux bridge `0.4.0` (`pasta` / `linux-netns` relay), retired standalone
mine-boundary `0.1.1` (integrated into engine `0.1.47`), and introduced pinned
gameplay catalogs `r1`/`r2` with written author permission for Heavy SEAF
`1.2.0-clonedivers.3` and SEAF Squad `0.1.4-clonedivers.2`. See
[v1.10.0](releases/v1.10.0.md), [0.4 integration record](FORGE-0.4-TURNKEY-2026-11-05.md),
and [migration guide](FORGE-MIGRATION.md).

## October 8 – November 1, 2026: Steam-era pack updates r22–r25

Published pack `2026.10.08-r22` (Fallen Order effort/injury cues and shared
Full/Lighter walker geometry), `2026.10.08-r23` (HD2Runtime LEGO Yoda death
scream prototype), `2026.10.09-r24` (Constitution audio MAIN padding and Star
Destroyer MAIN row-ordering repairs), and `2026.11.01-r25` (HD2 `7.2.2` / Steam
build `26251122` audio bank rebase restoring `246` native event IDs; root Steam
compatibility feeds `manifest*.json` remain at `2026.11.01-r25` for legacy `--steam`).
See [r22](releases/pack-2026.10.08-r22.md), [r23](releases/pack-2026.10.08-r23.md),
[r24](releases/pack-2026.10.09-r24.md), and [r25](releases/pack-2026.11.01-r25.md).

## October 8: cumulative Empire r21 published

Pack **2026.10.08-r21** reuses the published launcher **1.7.3** and promotes the
current reviewed combined Clonedivers, CommandoDivers and EmpireDivers pack.
All 1,794 logical rows retain their mode/profile/option conditions and order.
The cumulative Imperial armor, film-scale AT-ST, Lambda, Star Destroyer, Eagle,
weapon, audio and voice work is included, together with the final deep-QA repairs.
The two runtime helpers now have portable installation contexts; automatic walker
capture is disabled in the public pack. The baked walker remains independent of it.

Native regression checks, all 96 actual launcher selections, Full/Lighter source
hashes, 68 portable-helper fixtures and independent context rejection/path tests
pass. Publication uploaded **135 new files / 1,919,856,424 bytes**, verified all
**1,100 hosted asset identities**, and activated the coordinated feeds at commit
`4efd48c460f5d3c395721f4fd7d8c843f520850e`. Both GitHub API and raw readbacks match all three sealed feeds.
The downloaded 1.7.3 launcher and README Latest link are verified independently.

Star Destroyer rendering, mission physics/animation, Eagle dispatch and audio mix
still need gameplay acceptance. The 120 model-specific armor damage/cap routes and
separate missing native Constitution source remain open. The owner's private feed,
installed files and saved settings remain in place.

See [release notes](releases/pack-2026.10.08-r21.md),
[source credits](releases/pack-2026.10.08-r21-sources.md), and
[public verification](releases/pack-2026.10.08-r21-verification.json).

## October 5: Clonedivers 1.7.3 / combined r20 published

Launcher **1.7.3** and pack **2026.10.05-r20** retain the complete reviewed
combined Clonedivers/Commando/Empire content, including the user-confirmed
Phase I Shiny DP-8 helmet repair and experimental First Person v2.9.14.
Production validation verified 60 new hashes, all 96 requested launcher
selections, and the Full/Lighter source files. Existing mode/profile/option
conditions and game payloads remain exact.

Fresh installations receive missing FPV defaults with first person disabled;
existing choices and valid backup choices are preserved. The launcher verifies
the actual camera, options-menu and loader archives before preparing these
values. Native regression and the 44 focused FPV checks pass. First Person code
and archives are unchanged. Each game session still requires Engine camera
bridge On/APPLY then Off/APPLY while FPV is disabled before enabling it.

Publication completed through the sealed release workflow at feed commit
`89c983d5b02c7e91bd6b530668044942dd12be4f`. All hosted asset sizes/digests,
the downloaded launcher, all three API and public raw feeds, and the README
Latest download link are verified. The packaged 1.7.3 selector passes all 96
states; the original 1.6.3 reader accepts its compatibility feed and offers 1.7.3.
The complete Empire roster remains included. The owner's private development
feed and installed game files were left in place. Mission and AT-TE gameplay
acceptance remains open. See the [pack notes](releases/pack-2026.10.05-r20.md),
[launcher change](releases/launcher-1.7.3.md), and
[public verification](releases/pack-2026.10.05-r20-verification.json).

## September 27: Empire equipment parity r18 published

Pack **2026.09.27-r18** reuses launcher **1.7.1**. Empire now includes the complete
90-archive scope/ADS setup and 36 other shared equipment, audio and vehicle
bundles. No asset bytes or URLs change. Clonedivers and its Venator intro remain
identical; clone/Commando voices stay exclusive. Imperial models and battle intro
remain intact. [Scope and validation](releases/pack-2026.09.27-r18.md).
All three public feeds are verified at feed commit `ccc56cd`; the original 1.6.3
reader offers launcher 1.7.1. The live pack passes all 32 mode selections, source
hash checks and resource overlap checks. Gameplay remains unverified.
[Public verification](releases/pack-2026.09.27-r18-verification.json).

## September 27: launcher 1.7.1 / Empire intro r17 published

EmpireDivers receives the requested Helldivers vs Star Wars intro with matching
audio, a trimmed black tail and short silent logo replacements. Clonedivers keeps
the Venator intro exactly. Covenant and SAI E-11 sound/blue bolts are retained.
All 912 Bink frames decode offline using the installed game's decoder; 16 mode
combinations pass. The original 1.6.3 reader accepts the compatibility projection
and offers the launcher update. Native and publication workflow tests pass.
The new launcher polls for updates and preserves app metadata during profile-feed
fallback. Three feeds published together after hosted-asset verification (feed
commit `baad7aa`); the Latest executable and all six new intro assets have been
downloaded and hashed. Gameplay remains unverified.
[Release notes](releases/v1.7.1.md),
[public verification](releases/v1.7.1-verification.json).

## September 26: SAI sound and blaster bolts published in pack r16

Launcher **1.7.0** is unchanged. Pack **2026.09.26-r16** adds LAS-12 SAI E-11
firing sound and blue Battlefront-style blaster bolts to Clonedivers and
EmpireDivers, with either profile and Covenant setting. Gun model unchanged.
One new 2,266,656-byte asset; no existing resource collisions. All native tests,
16 SAI selection checks and Full/Lighter source hashes pass. Publication completed
through the sealed workflow (feed `7fb1c44`); the public asset was downloaded and
re-hashed, both feeds verified, and 1.7.0 remains Latest. Gameplay is unverified
under the owner's release authorization. Installed game files remain unchanged.
[Notes and credits](releases/pack-2026.09.26-r16.md),
[verification](releases/pack-2026.09.26-r16-verification.json).

## September 26: launcher 1.7.0 / pack r15 published

EmpireDivers is a third mode with Imperial armor, Star Destroyers, gray LAAT,
shared blasters, and repaired Patriot/Emancipator AT-STs. Covenant is independent
and off by default. Native installer round trips, 16 selection checks and model
decoding pass. The owner requested shipping 1.7 without gameplay acceptance;
runtime remains unverified. Published through the sealed workflow (feed commit
`35af359`; [verification](releases/v1.7.0-verification.json)). Hosted assets, both
live feeds, the downloaded executable and the Latest release target are verified.
Installed mods/settings remain unchanged while the game is running.
[Scope, credits, receipts and limitations](EMPIREDIVERS-AND-COVENANT.md).

## September 25: launcher 1.6.3 / pack r14 published (markers removed)

Published through the sealed workflow (feed commit `ff172af`; [verification](releases/v1.6.3-verification.json)).

The owner saw the Spider Droid markers in game, judged they didn't look right, and asked for
them to be removed. r14 is r13 without manifest `patch_318`: no new assets, nothing renumbered,
and the Spider Droid unit falls back to the CIS patch. They rendered in game, so the UV3 layout
change was accepted; there is no other gameplay evidence yet.

## September 24 (late night): launcher 1.6.2 / pack r13 published (voice gap fill)

Published through the sealed workflow (feed commit `0a973e3`; [verification](releases/v1.6.2-verification.json)).

318 silent or Battlefront-clone lines now play Republic Commando or Temuera Morrison recordings,
and voice 4 is labelled "Boss/Clone". Still silent per voice: about 105-121 lines, mostly numbers,
NATO letters, directions and distances, for which no clone recording exists. Built with
`tools/build-voice-fill.py`. [Details](AIM-MARKERS-AND-FIFTH-VOICE.md).

## September 24 (late night): launcher 1.6.1 + pack r12 published (opening logo trimmed)

Published through the sealed workflow (feed commit `b4a7c7c`; [verification](releases/v1.6.1-verification.json)).

The owner asked to publish r12 as **1.6.1**: the launcher is rebuilt with only its version bumped, and
[1.6.1 notes](releases/v1.6.1.md) cover r11 and r12. The earlier pack-only staging `dist/release-ready-r12` is unused.
r12 changes only the opening: Coastlake's end logo is removed. Video 0-75.0 s plus 1.0 s black
(the shot's own fade reaches black at 74.71 s; the logo starts at 75.42 s), 1,824 frames, four-slice
Bink 2; soundtrack 76.0 s with a 74.5-76.0 s fade. Built with `tools/build-pack-candidate.py
--version 2026.09.24-r12` (the generalised r11 tool; it reproduces the r11 manifest byte for byte).

## September 24 (night): pack r11 published (launcher unchanged)

Published through the sealed workflow (feed commit `27dc6d5`; [verification](releases/pack-2026.09.24-r11-verification.json)). Owner-authorized release without a prior in-game boot: pack 2026.09.24-r11 on launcher 1.6.0.
It replaces the opening with Coastlake's Venator fleet animation (four-slice Bink 2, 1080p/24,
82.1 s, plus a PCM English soundtrack; the only header differences from the shipped RC intro
patches are length fields). Boss gains 263 Temuera Morrison fill lines, and Spider Droid eye/vent
markers ship as manifest `patch_318` (option `droids`). Inputs: `tools/build-r11-candidate.py`
→ `dist/release-r11-inputs`; notes: [pack r11](releases/pack-2026.09.24-r11.md). In game still
pending: opening playback, Boss fill lines, markers, and the unexplained exit-time crash dump of
the 19:55 session (access violation in `helldivers2.exe`, written just after settings save).

## September 24 (evening): marker candidate installed locally; fifth voice blocked

Spider Droid eye/vent marker candidate v4 passes independent Filediver validation: 28/28
original primitives intact, markers skinned to `turret` (eye) and `boss` (vents). It is
installed locally as `9ba626afa44a3aa3.patch_304` for a gameplay test, with a receipt. It is not
in any feed. A fifth selectable voice cannot be added with asset patches: the game has exactly
four voice identities, defined in protected executable data. The owner kept Boss and asked
for Temuera Morrison clone lines to fill his gaps: 263 Temuera recordings now cover calls
that played Battlefront clone lines or silence. That is installed locally as additive
`patch_305`. Markers and Boss merge await one gameplay session; neither is published.
Details: [markers and fifth voice](AIM-MARKERS-AND-FIFTH-VOICE.md).

## September 24: 1.6.0 / r10 published

The combined roster, ADS repair, expanded voices/armor, droid toggle, JohnsonPotatoMode,
vehicle replacements, Guard Dog cleanup and optional performance sharing are published
in launcher 1.6.0 / pack 2026.09.24-r10. The owner explicitly approved including the
experimental Supply FRV mesh port and Watcher probe loop. Their gameplay checks remain
open. Camera experiments remain excluded; eye/vent markers are unfinished.

Startup passed on today's installed build 25480438 (7.1.1). All 65 rebased banks,
English text and the Watcher bank are byte-identical to the audited 7.1 sources.
All native tests, 16 option/profile combinations and the telemetry sampler passed.
Publication completed through the sealed release workflow. GitHub asset sizes and
SHA-256 digests, the downloaded launcher, latest-release target and both public feeds
were verified. This machine now uses release 1.6.0/r10 and the public feed; the desktop
shortcut no longer starts a preview server. Performance enrollment was preserved.
Older dated sections below are historical snapshots.

Details: [1.6.0 release notes](releases/v1.6.0.md).

## September 24: shutdown hang reproduced with manual Steam

Steam PID 21500 was started manually and measured outside any Windows job, with
an unrestricted token. HD2 PID 17592 and crash helper PID 11564 both exited with
code 0 and signaled handles, but Steam retains `Running=1`. No new 114 appears in
this session's launch records. Steam is left open; no game/pack settings changed.
This rules out automation job inheritance as a necessary condition for the exit
tracking hang, while leaving the separate error-114 hypothesis unresolved.
Evidence and interpretation: [GameGuard investigation](GAMEGUARD-INVESTIGATION.md).

## September 23: Steam stopped; GameGuard history reviewed

User's Steam Stop attempt stalled. Ended Steam PID 6412 at their request and
verified Steam and HD2 are absent. No automatic restart. Current settings retain
FOV 85 and 1,133 active pack files; camera loader/probe and rejected cutaway remain
absent. See [GameGuard investigation](GAMEGUARD-INVESTIGATION.md) for the evidence.

114 predates the camera work, persisted with FOV 55 and zero active mods, and
FOV 75 later worked. Today's strongest lead is Steam startup context/state:
manual Steam boot worked both vanilla and through Clonedivers. Current read-only
measurements show approved automation shells in a Windows job while Explorer and
the working launcher are outside jobs; the failed Steam job/token was not captured.
This is a hypothesis, not an established internal cause. A controlled manual /
automated / manual Steam comparison with fixed files is prepared but not run.

The latest Steam hang followed normal exit code 0 from both game and crash helper;
the historical browser-helper explanation does not account for this latest run.
No pack/public release/security changes were made during the review.

## Current local mode: Clonedivers launch confirmed; Steam exit tracking stuck

The launcher restored `2026.09.23-player-preview3` Full and recorded verification
at 21:35:53 on September 23. All 1,133 patches are active again, `mods_off` is empty,
and all six camera diagnostic/loader files remain absent. Steam PID 6412 was
manually started at 21:35:16 and has been left alone. The user confirms launching
through Clonedivers worked well, then HD2 closed and Steam remained stuck on Running.
Windows handle queries establish HD2 PID 6844 and crash-helper PID 13548 both exited
with code 0 and signaled process handles (`WaitForSingleObject(...,0)=0`). Steam
still reports `Running=1`. This is stale Steam tracking, not a live game/helper.
Asked the user to try Steam's Stop button before another client restart. Do not
repeat the mod restore or automatically restart Steam through agent tools.

## Working baseline: manual Steam startup succeeds

The user confirms HD2 launched successfully after opening Steam from Windows
Start and launching the game from the library, with all mod patches parked.
Preserve this manually started Steam session; do not restart Steam from agent
tools. This implicates launch context/state, but does not prove which inherited
process restriction or other condition caused 114. No further GameGuard repair
or compatibility relabeling is warranted on this evidence.

Next: once the user closes the successful game, restore Clonedivers through the
launcher and retest using the same manually started Steam session. The combined
pack and Lua camera probe have not yet passed this comparison. Keep the probe out.

## Startup isolation: all mods parked, GameGuard still fails

All 1,133 pack files are now reversibly parked in `mods_off`; zero active patch
files remain. The plain-game launch produced GameGuard 114 at September 23 21:24:07.
Following nProtect's folder-refresh instructions, preserved `bin/GameGuard` as
`bin/GameGuard.backup-20260923`; the game downloaded fresh files but again produced
114 at 21:26:32. No protection settings changed. Logs and receipts are local under
`dist/vanilla-startup-2026-09-23/`. Next test: user starts Steam manually from Windows
Start and launches HD2 directly, leaving the mods parked. Camera work is paused.

The separate launcher compatibility prompt compares installed game build 25327279
with pack `gameBuild=24826606`. The candidate records `gameBuildAudited=25327279`
but `runtimeTested=false`. The EXE and game DLL hashes are unchanged. Do not relabel
the pack gameplay-verified merely to hide the prompt. Reconcile build/audit/runtime
metadata when a working baseline and gameplay acceptance are available.

## Local launcher refreshed; Steam revalidation next

At the user's request, refreshed the local install to pack
`2026.09.23-player-preview3` and launcher `1.6.0-preview6`. All 1,133 Full-profile
pack files already matched; no asset replacement was needed. The desktop shortcut
was still opening public launcher 1.5.0. It now runs `tools/start-local-preview.ps1`,
which starts/verifies the loopback feed before opening the hash-verified preview
in `dist/local-current/`. Both initial feed startup and reuse were checked, including
the shortcut's Windows PowerShell host. Previous shortcut and install receipts
are preserved in `dist/local-refresh-2026-09-23/`.

User will revalidate Helldivers 2 in Steam next. No game launch requested during
this refresh. Camera probe/loader remain removed; original AT-TE meshes retained.
Public launcher/feed are unchanged. GameGuard 114 remains unresolved.

Steam's subsequent false "running" state was cleared by a graceful client restart
without launching HD2. No game process remained; Steam's per-app registry state
reported `Running=0`, `Installed=1`, `Updating=0`. Its console log also revealed the
earlier direct administrator attempt had redirected into a pending `ShowGameArgs`
prompt, so that attempt is not evidence of a further GameGuard failure. Ready for
the user's Steam revalidation.

## AT-TE runtime probe removed; baseline launch blocked

The camera issue also occurs at regular aspect ratios. A read-only Lua capability
probe and Bingus Shared Loader v16 were tested locally in additive patch slots
304/305 and have now been removed, with all six files verified absent. Existing
pack assets are unchanged. Build fingerprints and package
checksums match; seven offline LuaJIT probe cases passed. The latest launch hit
GameGuard 114 even after the user's reboot and produced no probe log. After
removing the diagnostic and gracefully restarting Steam, the original pack also
hit 114 at 20:53:52. A subsequent clean baseline reboot also hit 114 at 20:57:21.
Arrowhead's signed GameGuard uninstall/reinstall utilities completed with exit 0,
but the next baseline launch still hit 114 at 21:01:11. A one-time administrator
launch was approved and started (PID 20040), then exited; its user-visible outcome
is unconfirmed. No new Steam error entry or matching Windows crash event was found.
The user chose a local launcher refresh followed by Steam revalidation instead of
further launch retries. Camera/vehicle APIs remain unverified;
do not reinstall the probe yet. No permanent compatibility/security setting changes
and no team release. See
[camera investigation and exact diagnostic removal](AT-TE-CAMERA.md).

## AT-TE cutaway rejected / baseline restored

User gameplay feedback: no improvement inside the vehicle and excessive exterior
removal. The cutaway trial is rejected. Restore the prior local pack
`2026.09.23-player-preview3`; verification lives under
`dist/walker-cutaway-2026-09-23/rollback/`. Launcher `1.6.0-preview6` remains current,
including the hidden mod toggles in regular Helldivers. The cutaway is not a
release candidate; its earlier structural checks are not gameplay acceptance.
No cause has been established for the reported crash or occupied-view behavior.

## September 23 AT-TE cutaway trial

Local `1.6.0-preview6` hides mod option controls in regular Helldivers; returning
to Clonedivers restores the saved choices. Native tests and both mode previews
passed. An unavailable old texture preference falls back to Full when entering
a modded mode, avoiding a hidden-control dead end.

`2026.09.23-atte-cutaway1` is the new local visual experiment, opening the rear
central hull across all four AT-TE variants and visible detail levels. Only
mesh index lists/counts change; physics, rigging, shadows and unrelated assets
are unchanged. See [cutaway details and rollback](AT-TE-CAMERA.md).
The local install receipt and verification are under
`dist/walker-cutaway-2026-09-23/local-install/`; new feed port 8768.
Pilot visibility and gameplay validation remain pending. Public release unchanged.

## September 23 launcher cleanup

Local launcher `1.6.0-preview5` removes the Delta Squad and Walker weapons controls
from the combined roster and enforces both features during file selection, even
when older saved flags are false. Droid skins remains independent. One
**JohnsonPotatoMode™** On/Off control replaces Full/Lighter; it currently selects
the existing full-resolution streaming profile, not the old experimental 512px
candidate. Help text is shorter and retains the Brawny requirement.

Native tests, all 16 stale-flag/profile/droid combinations, release build, and
rendered launcher preview passed. Build/preview are under
`dist/launcher-cleanup-2026-09-23/`. No new game assets or public release.
See [AT-TE findings](AT-TE-CAMERA.md) for cutaway versus invisible-body limitations;
no camera/mesh experiment has been installed.

## September 23 automatic performance collection

The collector is deployed in Goode Works at
`https://clonedivers-telemetry.goodecraft.com/`, with a dedicated D1 database,
private R2 bucket, 90-day lifecycle/cleanup and separate owner/invitation secrets.
Production tests using the launcher client pass enrollment, compressed upload,
duplicate retry, private readback and revocation. A clearly labeled self-test
contains actual hardware and collector-process samples, not gameplay.
The local `1.6.0-preview4` launcher is enrolled as Owen; its first automatic report
arrived. It temporarily uses the workers.dev alias because the local DNS resolver
still caches the custom hostname's pre-creation NXDOMAIN. Public DNS and custom
domain TLS/health are verified. Owner credentials are DPAPI-encrypted in AppData,
outside the repository. See [collection status](AUTOMATIC-PERFORMANCE-REPORTS.md).
The Windows FPS-permission retry succeeded and group membership was verified.
A Windows sign-out/sign-in is still required to refresh the user token.
Live-game frame capture and weak-PC overhead checks remain open. Public launcher
1.5.0/r9 is unchanged; the collector deployment is live but no team update shipped.

## September 23 vehicles and local session logging

The newest local candidate is `dist/vehicle-update-2026-09-23/feed/manifest.json`
(`2026.09.23-player-preview3`). It adds the Falchion Bastion, an experimental TX-130
Supply FRV geometry port, and restored spider weapons under **Walker weapons**.
Eye/vent markers remain unresolved; no original-War-Strider fallback was applied.
All 16 selection checks pass. Launcher `1.6.0-preview2` adds bounded local session
observations and a Save report action; native and diagnostics tests pass.
See [details and limitations](VEHICLES-AND-CLIENT-LOGGING.md). Public 1.5.0/r9 is unchanged.
Installed locally and byte-verified: 1,133 Full files with Delta/droids/walker weapons
on. The loopback feed serves this newest candidate; rollback/install receipts are
in its parent directory's `local-install/` folder. Gameplay acceptance remains open.

## September 23 QA follow-up

ADS passed user testing. Cannon visibility remains unconfirmed. A follow-up local
candidate adds qualified Commando item names, expands the mixed clone roster from
7 to 15 equipment groups, and stages a probe-audio transfer from Guard Dog to one
Watcher loop. All 16 option/profile combinations and serialized resource checks
pass. Watcher playback/stop behavior and added armor visuals require gameplay QA.
See [follow-up details](PLAYER-QA-FOLLOWUP.md). Public 1.5.0/r9 is unchanged.
The follow-up is installed locally and all 1,124 Full-profile files are verified.
The loopback server now serves `dist/player-followup-2026-09-23/feed/`.
Rollback receipts are under that follow-up directory's `local-install/` folder.

## HD2 7.1: rebuilt candidate ready for gameplay acceptance

Local test installed September 23 at 21:21 UTC after user authorization. All 1,124
Full-profile files are byte-verified with Delta, droids and walker cannons enabled.
Steam launch requested; gameplay outcome is pending. Previous replaced files are
preserved in `mods_old`; settings, prior receipt, baseline manifest and install
journal are under `dist/player-update-2026-09-23/local-install/`. The loopback feed
on port 8767 serves `feed-7.1-final/`; local settings point there for test toggles.
Public 1.5.0/r9 is unchanged. Use the staged preview launcher during this test.

The [7.1 rebase](HD2-7.1-COMPATIBILITY.md) is complete in staging. Both voice presets
retain all 3,246 current English keys, restoring the 287 missing entries. The audio
rebuild refreshes 70 bank occurrences across 55 bundles, including three later
weapon-bank overrides, plus all four RC banks. Current routing/properties and
new-only embedded samples are retained; all 1,525 expanded RC streams are unchanged.
Serialized audio/provenance checks and all 16 selection combinations pass.

Use `dist/player-update-2026-09-23/feed-7.1-final/manifest.json` with the existing
`launcher/Clonedivers.exe` preview. Earlier `feed/`, `feed-7.1/` and partial audio
stages are superseded. This candidate is now installed locally, not published. In-game
ADS, armor, interrupted voices and sustained-fire checks remain required.

## September 23 combined player update — local test candidate

`dist/player-update-2026-09-23/feed-7.1-final/manifest.json` now combines the scope candidate,
shared clone/Commando armor roster, four independent Commando helmet targets,
expanded Delta voices, regular clone voice preset, droid switch and original
Factory Strider/MTT cannon visibility switch. Seven additional legion styles have
independent material/texture IDs on audited equipment groups. The armor source's
intended Body Expansion → Material Reset load order is restored.

Full and Lighter roster variants are built and verified without reducing texture
resolution. Native launcher tests and all 16 selection combinations pass; the final
launcher preview is visually checked. A self-contained local preview launcher is
under `dist/player-update-2026-09-23/launcher/`. No installed game files or public
release feeds changed. Gameplay acceptance is still required, particularly ADS,
material loading, four helmet choices, cannon alignment and weaker-PC memory.
War Strider weak-point geometry is not fixed by the cannon option.

The machine now has Steam build 25327279. Updated Filediver v0.7.53 equipment
definitions retain the four unique helmet targets (411 kit records vs 402 cached).
The current game archive catalog contains all 90 scope targets. Neither check
establishes runtime compatibility. See [the detailed candidate notes](PLAYER-CUSTOMIZATION-UPDATE.md).

## September 20–22 local follow-up

The [player customization update](PLAYER-CUSTOMIZATION-UPDATE.md) records the new
ADS report, optional droid skins, aiming-point request, clone variety and shared
Commando helmet mappings. Droid-toggle UI and a zero-new-asset candidate pass local
tests. The user clarified widespread missing/blurry ADS and enemy weapon/weak-point
visibility, and supports a mixed clone/commando equipment roster. Asset work and
gameplay validation remain pending.
The [local RC audio review](RC-AUDIO-REVIEW.md) processed all 3,988 distinct Delta
recordings with free local transcription, followed by an independent caption pass
for shipped and flagged clips. It records uncertainties and additional Boss candidates;
it has not changed voice slots, audio levels, or shipped mappings.
The [September 23 RC voice expansion](RC-VOICE-EXPANSION.md) is built in staging:
496 distinct recordings across 1,525 mapped entries, up from 322 across 1,488.
Source, actor, fallback and archive checks pass. In-game playback and Boss volume
balancing remain pending. No follow-up changes have been installed or published;
the release below remains live.

## September 13 release: launcher 1.5.0 / pack 2026.09.13-r9

This release adds independent Full/Lighter selection before installation, quieter
cached mode switches, offline cached metadata, explicit repair and diagnostics,
installation receipts, exact-target recovery and bounded resumable downloads.
Commandodivers help states the Brawny body-type requirement.

The r9 profile feed reuses the existing tested r8 assets in all four combinations
of Clonedivers/Commandodivers and Full/Lighter. Players keeping their selection
need only the launcher update. The format-2 compatibility feed retains pack r8
and advertises app 1.5.0; the new launcher reads the format-3 r9 feed. Both feeds
are published together after hosted assets and the public executable are verified.

The final release inputs are under `dist/release-final/`; its `staged-verified/release.json`
records publication completion and the feed commit. Earlier stages containing
additional RC streaming assets are superseded and must not be published.

## Validation and performance limits

- Native launcher suite passes; profile, diagnostics, release, builder and recovery
  fixtures pass. The new launcher preview was rendered and visually inspected.
- All four final mode/profile sets retain their existing asset names, sizes and
  hashes. The profile migration requires zero new game-asset uploads.
- The user confirmed normal visuals during the local shadows and distant-detail
  geometry trials. Their combat captures do not establish an FPS improvement.
  Original geometry was restored. See [trial results](PERFORMANCE-TRIAL.md).
- Additional RC texture streaming passed byte-for-byte payload and container
  checks, but a live startup/visual check was blocked by a stale GameGuard process.
  Windows elevation consent was canceled; no protection bypass or reboot was used.
- Additional RC streaming, reduced-resolution profiles and mesh experiments remain
  private. None is included in r9. Weak-PC performance remains unproven; this
  launcher release does not claim a gameplay FPS improvement.

## Existing game content

- Modes: **Helldivers**, **Clonedivers**, **Commandodivers**. Commando help starts
  with “Delta Squad is elite.” Scope removal is included in both modded modes.
- The full RC opening picture and dialogue were confirmed in game.
- Voices are selected manually: 1 Sev, 2 Fixer, 3 Scorch, 4 Boss. Armor does not
  automatically route audio. The named voice menu was confirmed visible.
- Original Delta body replacements remain; players unlock their chosen body.
  Four starter B-01 helmets map to the same character order. Use **Brawny** body
  type: the user confirmed this resolved the reported missing Commando bodies.
- Host FOV is 85, independent of pack modes, and was present during the local
  combat captures. Actual camera pullback remains unresolved and paused by choice.
- Four starter helmet appearances, hearing all four character voices from another
  player, and reinforcement/fallback behavior still need multiplayer acceptance.
  See [playtest](PLAYTEST.md). Hash checks do not establish multiplayer behavior.

## Build state and recovery

The complete deployment report includes all 34 RC-only files. Deployment and
variant receipts bind the recipe/report to exact hashes. The verified existing
Lighter source is `dist/pack-optimized-current`. Voice source hashes, dependency
versions and encoding settings are in [build inputs](../build-inputs/rc/README.md).

Use [the publishing workflow](PUBLISHING.md) for future releases. Never overwrite
or delete old content-addressed asset releases: current manifests reuse them.
The deeper [player investigation](PLAYER-UPDATE-INVESTIGATION.md) preserves the
architecture findings and experimental history.

To return to vanilla, close the game and choose **Helldivers**. To undo personal
FOV changes, edit only `vertical_fov` with the game closed; do not restore an entire
old settings file over newer preferences. Prior backups remain under
`dist/release-1.4.1/` and maintenance evidence under `dist/maintenance-review/`.
