# JohnsonMode(tm)

## Late November 2026 (`v2.1.0`–`v2.1.4` / `linux-v0.10.0`–`0.10.4`): 5-Tier Texture & Decimated-Mesh Matrix (`r52` & `r23`)

Starting in **Clonedivers 2.1**, both **EmpireDivers (`2026.11.25-r52`)** and **Clonedivers (`2026.11.25-r23`)** ship **five** native Format-3 `textureProfiles` across every enemy-skin (`All CIS Droids`, `Mixed Bots + CIS Droids`, `Native Automatons`) and `Covenant + Squids` (`Mixed Covenant + Squids`, `All Covenant`, `Native Squids`) combination, paired with automatic GPU VRAM/RAM tier detection in the launcher:

1. **`full` (`Full (Ultra)`)**: Uncapped mip chains resident in `.gpu_resources` for `16+ GB VRAM` desktop GPUs.
2. **`lighter` (`High (Streamed)`)**: Lossless full-resolution streaming (`--stream 512 --strategy quality --no-dedup`), keeping `<= 512 px` tails resident and streaming higher mips from `.stream`.
3. **`reduced1024` (`1024px (Medium)`)**: `1024 px` texture cap with `256 px` resident tails for `8–12 GB VRAM` GPUs.
4. **`reduced512` (`512px (Low / Deck)`)**: `512 px` texture cap with `128 px` resident tails for `6–8 GB VRAM` GPUs and handhelds.
5. **`potato256` (`256px + Decimated Meshes (Ultra-Potato)`)**: `256 px` texture cap (`64 px` resident tails) plus topology-safe mesh decimation on non-pinned world/enemy geometry while strictly pinning node/bone/LOD layouts required by runtime leaf writers (`79` Compact LEGO garments, K-2SO `B-27`, and `9.04 m` `EXO-45/49/51/55` AT-STs), cutting active GPU VRAM up to `-80%` (measured `~ -50%` whole-process Dedicated GPU VRAM and `-94%` Shared GPU spillover in live Direct3D sessions on `D:\HellForge`) and pack disk footprint up to `-66%`.

See [STATUS.md](STATUS.md) and [v2.1.4 release notes](releases/v2.1.4.md) for current live Direct3D VRAM and `PresentMon 2.5.1` measurements.

---

## November 2026 (`v2.0.0` / `linux-v0.9.0` historical): `Johnson Ultra-Potato (512px)` (`reduced512`) Integrated in `r48` & `r20`

Starting in **Clonedivers 2.0.0** (Windows) and **Clonedivers Forge Linux 0.9.0**, both **Clonedivers (`2026.11.19-r20`)** and **EmpireDivers (`2026.11.19-r48`)** ship three verified Format-3 `textureProfiles` selectable directly in the Setup Wizard and main launcher across every enemy-skin (`All CIS Droids`, `Mixed Bots + CIS Droids`, `Native Automatons`) and `Covenant` combination:

1. **`full` (`Full`)**: Uncapped mip chains resident in `.gpu_resources` for high-VRAM desktop GPUs (`>= 12–16 GB VRAM`).
2. **`lighter` (`Lighter (Streamed)`)**: Lossless full-resolution texture streaming built with [Stingray Texture Optimizer](https://github.com/Shiroiame-Kusu/StingrayTextureOptimizer) (`--stream 512 --strategy quality --no-dedup`, no `--max-size`). Mips above `512 px` move byte-for-byte into `.stream` while `<= 512 px` tails stay resident in `.gpu_resources`.
3. **`reduced512` (`Johnson Ultra-Potato (512px)`)**: Lossy reduced-resolution streaming profile built with Stingray Texture Optimizer (`--max-size 512 --stream 128 --strategy quality --no-dedup --reprocess-streamed`). Caps modded textures at `512 px` maximum and keeps only `<= 128 px` mips resident in `.gpu_resources`, cutting both resident `.gpu_resources` and on-demand `.stream` footprints for `6–8 GB VRAM` GPUs, handhelds, and slower storage.

### Scope and Qualification Boundaries (`v2.0.0`)
- **What `reduced512` changes:** Texture mip caps (`512 px` max) and resident mip tails (`128 px`) only. Every armor/vehicle/droid unit mesh, skeleton, animation, Wwise audio bank, particle effect, and option layer (`patch_1000..1036` / `patch_1000..1032` in `r48`, `clonedivers`/`commandos` `patch_0..37` / `patch_0..33` in `r20`) is preserved intact. On `Empire r48` (`All CIS Droids`, `Covenant OFF`), `reduced512` lowers resident `.gpu_resources` from `9.00 GiB` (`Full`) and `3.34 GiB` (`Lighter`) to **`2.62 GiB` (`-70.9%`)** and cuts total disk footprint from `10.67 GiB` to **`4.98 GiB` (`-53.3%`)**; on `Clones r20` (`Delta Squad`, `All CIS Droids`, `Covenant OFF`), `reduced512` lowers resident `.gpu_resources` from `7.96 GiB` to **`3.34 GiB` (`-58.1%`)** and total disk from `8.75 GiB` to **`4.45 GiB` (`-49.1%`)**.
- **Visual trade-off:** Deliberately lossy — large hull plates (Venator, Star Destroyer, AT-ST, Bastion) and close-up armor decals appear softer than `Full` or `Lighter (Streamed)`. Mesh vertex buffers and draw calls are unchanged.
- **Backwards compatibility:** When resolving against older `r47` or `r19` two-tier manifests (`full` / `lighter`), `HellForgePack` automatically maps `reduced512` to `lighter` without failing setup or migration.
- **Verification status:** All `r48` and `r20` `reduced512` combinations pass offline Stingray bundle verification (`verify_candidate.py`: `0` missing dependencies, `0` unresolved geometry providers, `0` audio codec mismatches, `0` roundtrip errors) and launcher/bridge/Linux test suites. Live weak-PC FPS, frame-time, and VRAM measurements on build `25480438` remain open (`runtimeTested: false` pending attended `R2` desktop sessions).

---

## Historical Notes (September 2026 Steam-Era `r6`–`r10` Experiments)

September 23 naming update: the Steam-era `1.6.0` launcher called its existing Full/Lighter switch
**JohnsonPotatoMode™** (selecting full-resolution streamed textures at the time while keeping the `r6` 512px build below as a separate experiment).

Experimental performance build, keeping every mod and optional group in r6.

## Plan

1. Recover skinny history. Claude session `d7fb1a3f-bb1e-4a72-8713-f3fb98fc957a` records the user's requirement to retain the whole mod. Skinny streamed full-resolution textures; it did not cap resolution. Its historical measurements showed about 3.4 GiB of mesh payload remaining after streaming.
2. Reconstruct the canonical r6 base by manifest SHA-256 from local active/parked files. A live installation has renumbered optional groups and cannot be used as the canonical build source by filename alone.
3. Build a separate 512-pixel-cap, 128-pixel-resident-tail candidate with Stingray Texture Optimizer 0.1.4. Retain mip chains, texture formats, all geometry, animation, sound and mod identities. Reprocess already-streamed textures. Do not alias duplicate GPU payloads.
4. Verify every transformed bundle against its source, inventory hashes, compare sizes, and test reversible installation. Compare a 256-pixel candidate if the additional savings are meaningful.
5. Smoke-test on this machine and record exactly what was observed. Disk/payload reductions are not FPS measurements; Matt's hardware may have a different bottleneck.

## Build

`tools/build-johnsonmode.ps1` creates a fresh `dist/johnsonmode` containing an immutable source snapshot, optimized pack, source manifest, build settings, verifier log and file hashes. Pass `-GameDir` for a different Steam library and `-DotnetRoot` for a portable .NET 10 runtime. Use a new `-WorkDir` for each candidate.

This is deliberately lossy: distant textures and large surfaces will be softer. Mesh memory, draw calls and audio are unchanged. A lower texture cap cannot solve every performance problem. The public pack and its existing Lighter textures option are not changed by this build.

Optimizer source and license: [Stingray Texture Optimizer](https://github.com/Shiroiame-Kusu/StingrayTextureOptimizer), GPL-3.0. Tool output sometimes says streaming discards nothing; with `--max-size` enabled, higher-resolution mips **are** discarded.

## Local test status, 2026-09-07

- Both 512/128 and 256/64 candidates retain all 307 bundles. Verification found no new diagnostics relative to the source; existing source diagnostics are recorded in the verification logs.
- Original r6: 8.99 GiB on disk. 512/128: 4.64 GiB. 256/64: 4.45 GiB. These are file measurements, not process RAM or VRAM measurements.
- The production planner installed the 512 candidate (756 files for this user's options) and restored all 754 original filenames, sizes and SHA-256 hashes successfully.
- Machine: Ryzen 7 9800X3D, RTX 5090 (32 GiB), approximately 64 GiB RAM. Existing settings: 7680 x 2160, texture_quality 3, vsync off.
- Repeated skinny baseline capture was attempted with PresentMon 2.5.1. The game displayed **GameGuard: Initialize error, code 114**, before gameplay. PresentMon independently exited with code 6 because Windows denied access to its ETW trace session. The loop stopped after its first 30-second capture, with zero frame rows. Startup-error memory samples are not gameplay performance results.
- Raw capture output is under `dist/benchmarks/`; install/restore output is in `dist/johnson-roundtrip.log` and `dist/skinny-restore.log`.
- No FPS, frame-time, CPU, RAM or VRAM benefit over skinny has been established. Publishing the new option remains pending a successful in-game comparison. Resolve normal game startup first (the dialog recommends rebooting), then run PresentMon with the Windows tracing privileges it requires. Do not bypass GameGuard or disable protection to make a benchmark work.

### Repair follow-up

`tools/repair-benchmark.ps1` ran with administrator rights. Both signed INCA GameGuard tools completed with exit code 0 (uninstall, then reinstall). A 10-second elevated PresentMon permission check successfully opened and closed an ETW session, exit code 0. Future measurement runs must also run elevated; account group membership was not changed.

Steam was restarted to clear stale tracking of the browser launched by the GameGuard error helper. The subsequent normal launch still displayed error 114. An administrator launch was also attempted but did not reach a running game. A Windows reboot remains the next troubleshooting step. The original 754-file mod installation remains restored; no benchmark option has been published.
