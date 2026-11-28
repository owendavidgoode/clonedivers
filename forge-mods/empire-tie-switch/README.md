# Star Wars Per-Attack Air Wing — Empire TIE & Republic Air Wing (v0.4.0)

HellForge SDK 16 (compiled cleanly against SDK v20 with `-std=c11 -O2 -Wall -Wextra -Werror`), Helldivers 2 Steam build `25480438`.
Client-only (`gameplay: false`, `coop: per-player`) code driver supporting both **EmpireDivers** (`r52`) and **Clonedivers** (`r26`) across all 5 performance texture tiers (`full`, `lighter`, `reduced1024`, `reduced512`, `potato256`).

## What v0.4.0 Adds

1. **Dual-Pack 5-Tier Closure Gate (`asset_gate.h`, `assets.h`, `clone_eagle_assets.h`):**
   - Automatically detects and SHA-256-verifies either the **EmpireDivers** `135`-part TIE Fighter / TIE Bomber closure (`TIE_PACK_EMPIRE`) or the **Clonedivers `r26`** `patch_44` Republic air wing closure (`TIE_PACK_CLONES`) across `[0]=full`, `[1]=lighter`, `[2]=reduced1024`, `[3]=reduced512`, and `[4]=potato256` (`TIE_ALL_TIERS_MASK = 0x1Fu`).
   - Enforces cross-part texture-tier coherence, non-ambiguous archive ownership, and runtime `AssetsUnchanged()` file-handle verification. Fails closed with zero crashes or partial edits if any staged archive payload differs by even 1 byte.
2. **Per-Attack Aircraft Routing (`empire_tie_switch.c`):**
   - **EmpireDivers (`TIE_PACK_EMPIRE`):**
     - `strafe`, `110mm` -> `TIE_FIGHTER` (`0xf62538008a0edc18`)
     - `airstrike`, `500kg`, `cluster`, `napalm`, `smoke`, `gas` -> `TIE_BOMBER` (`0x81787742b65c43f4`)
   - **Clonedivers (`TIE_PACK_CLONES`):**
     - `strafe`, `110mm` -> `CLONE_FIGHTER` (`0xfb909f602a347374`, ARC-170 Starfighter)
     - `airstrike`, `500kg`, `cluster` -> `CLONE_BOMBER` (`0x21f2f1745df2d0b9`, BTL-B Y-Wing Bomber)
     - `smoke`, `gas`, `napalm` -> `CLONE_GUNSHIP` (`0xfe2802e03035e982`, LAAT/i Republic Gunship)
3. **Direct `eagle_base` (`0x2C26BC4C6592FA14`) Residency Fallback (`package_pin.h`):**
   - Calls `hf->load_package(mod, 0x2C26BC4C6592FA14ull)` first.
   - When `kRefPackages` omits `0x2C26BC4C6592FA14` and returns `0`, verifies all four build `25480438` 32-byte code pins (`game.dll` `0x12FE350` `RefcountedPackageRequest`, `0x12FE680` `ArePackagesIdle`; `helldivers2.exe` `0x321330` `has_loaded`, `0x60DA80` `find`), checks `< 75%` refmap headroom at `[game.dll + 0x347CEB0]`, acquires/holds `0x2C26BC4C6592FA14` on the game thread, and confirms residency in `helldivers2.exe` (`0x1A10208 -> +0x400 -> +0x208`).
4. **4-Player Concurrent Eagle Sortie Tracking (`TIE_MAX_SORTIES = 16`):**
   - Correlates `HF_ON_STRATAGEM` throwers against `PlayerManager` slots `0..3` with `HF_ON_SPAWN` delivery spawns, prunes expired sorties on TTL / mission exit, and clears bridge edits cleanly in `hellforge_mod_unload`.

## Offline Build & Verification

`build-offline.ps1` compiles `empire_tie_switch.dll` and `tie_test.exe` using the offline Zig toolchain and SDK v20 headers with `-std=c11 -O2 -Wall -Wextra -Werror` (touching zero live `HellForge` directories), runs the full C11 regression suite across both Empire and Clonedivers `r26` 5-tier closures, and packages the deterministic `ModPackage.Create`-compatible `.zip`.
