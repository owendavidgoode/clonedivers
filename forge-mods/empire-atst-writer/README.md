# Empire AT-ST Film-Scale Leaf Writer (`clonedivers.empire-atst-writer` v0.1.0)

`clonedivers.empire-atst-writer` (`0.1.0`) is a visual-only HellForge SDK-16 native C leaf writer for Helldivers 2 build `25480438` (`gameplay: false`, `coop: per-player`).

## Overview

Pairs with the S2c rig-collapsed film-scale AT-ST asset delta (`UNIT 0x79E4B3D2DA5E45E3`, `106` nodes `<= 128`, `8` skin bones `<= 64`, `[8, 1, 1]` palettes `<= 64`, `9` native `PHYSICS` overrides restored, and `8` child weapon `UNIT`s aligned onto native sockets):

- **4-Walker Co-op Registry:** Tracks up to `4` concurrent Patriot (`EXO-45`) and Emancipator (`EXO-49`) walkers across `4`-player co-op sessions, retiring destroyed walkers cleanly on `RenderHandle` `helldivers2.exe+0x1E7CD0` removal with monotonic generation epochs (ABA-safe) and fail-closed passthrough for any `5`th concurrent walker.
- **Chained SDK-16 Detours:** Hooks `helldivers2.exe+0x512B80`, `+0x5B2990`, and `+0x1E7CD0` via `hf->hook_ex` chaining (`priority=0`), coexisting cleanly alongside `clonedivers.lego-compact-leaf-writer` (`0.1.0`) while preserving MXCSR rounding state and Win32 `LastError`.
- **Fail-Closed Code & Asset Pinning:** Validates build `25480438` code byte pins (`hf->read_code`) and exact S2c bind/palette metadata before substituting private joint-aware temporal graph matrices; any mismatch delegates unmodified to the native renderer.

## Build & Offline Tests

- Build & package (isolated stage): `powershell -NoProfile -ExecutionPolicy Bypass -File build-offline.ps1`
- Offline C11 test suite (`zig cc -Wall -Werror`): `powershell -NoProfile -ExecutionPolicy Bypass -File tests\run_c_tests.ps1`
