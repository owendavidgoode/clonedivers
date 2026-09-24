# JohnsonMode(tm)

September 23 naming update: the new launcher calls its existing Full/Lighter switch
**JohnsonPotatoMode™**. On currently means full-resolution streamed textures.
The reduced-resolution builds below remain historical experiments and have not
been rebased onto the current asset pack or silently installed by that switch.

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
