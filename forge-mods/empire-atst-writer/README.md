# Empire AT-ST Film-Scale Leaf Writer (`clonedivers.empire-atst-writer` v0.2.2)

`clonedivers.empire-atst-writer` (`0.2.2`) is a visual-only HellForge SDK-16 native C leaf writer for Helldivers 2 build `25480438` (`gameplay: false`, `coop: per-player`).

## Overview

Pairs with the S2c rig-collapsed `9.04 m` film-scale AT-ST chassis `UNIT`s across all 4 Combat Walker variants:
- `0x79E4B3D2DA5E45E3` (`EXO-45` Patriot)
- `0xC2D449ECF7FACAB1` (`EXO-49` Emancipator)
- `0x7B2326F6FD9C8069` (`EXO-51` Lumberer)
- `0x35DBF54F016F3624` (`EXO-55` Breakthrough)

(`106` nodes `<= 128`, `8` skin bones `<= 64`, `[8, 1, 1]` palettes `<= 64`, stock `25480438` `BONES`/`STATE_MACHINE`/`PHYSICS` pinned, and `8` child weapon `UNIT`s aligned or hidden onto S2c sockets):

- **4-Walker Co-op Registry:** Tracks up to `4` concurrent walkers of any `EXO-45` / `EXO-49` / `EXO-51` / `EXO-55` mix across `4`-player co-op sessions, retiring destroyed walkers cleanly on `RenderHandle` `helldivers2.exe+0x1E7CD0` removal with monotonic generation epochs + `resource_id` ABA checks and fail-closed passthrough for any `5`th concurrent walker.
- **Chained SDK-16 Detours:** Hooks `helldivers2.exe+0x512B80`, `+0x5B2990`, and `+0x1E7CD0` via `hf->hook_ex` chaining (`priority=-10`), coexisting cleanly alongside `clonedivers.lego-compact-leaf-writer` (`priority=0`) while preserving MXCSR rounding state and Win32 `LastError`.
- **Fail-Closed Code & Asset Pinning:** Validates build `25480438` code byte pins (`hf->read_code`) and exact S2c bind/palette metadata before substituting private joint-aware temporal graph matrices; any mismatch (such as Clonedivers AT-TE `198`-node / `88`-bone walkers) delegates unmodified to the native renderer.

## Build & Offline Tests

- Build & package (isolated stage): `powershell -NoProfile -ExecutionPolicy Bypass -File build-offline.ps1`
- Offline C11 test suite (`zig cc -Wall -Werror`): `powershell -NoProfile -ExecutionPolicy Bypass -File tests\run_c_tests.ps1`
