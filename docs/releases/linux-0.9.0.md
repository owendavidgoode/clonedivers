# Clonedivers Forge Linux 0.9.0

> **Candidate release notes (unpublished — feeds-last staging):** `linux-v0.9.0` (`feat/v2.0-turnkey`) is staged alongside Windows `v2.0.0`, `pack-2026.11.19-r48-files`, `pack-2026.11.19-r20-files`, `forge-mods-2026-11-19`, and `forge-gameplay-build25480438-r5`. Root public feeds (`manifest*.json`) stay at live `1.14.2` / `r45-v4` until `R1` asset upload and attended `R2` acceptance complete.

## Linux Launcher & Forge Bridge Updates (`0.9.0` / Bridge `0.4.0`)

- **3×3×2 Appearance Controls Across Clones (`r20`) & Empire (`r48`):**
  - `ForgeWindow`, `LinuxForgePreferences`, and `HellForge.Launcher.ClonediversBridge` support all **3 Enemy Skins** (`All CIS Droids`, `Mixed Bots + CIS Droids`, `Native Automatons`), **3 Texture Tiers** (`Full`, `Lighter (Streamed)`, `Johnson Ultra-Potato (512px)` `reduced512`), and opt-in **Covenant** (`1670`) across both **Clones (`2026.11.19-r20`)** and **Empire (`2026.11.19-r48`)**, plus **Delta Squad** in Clones.
  - `PlayerCommands` (`select-mode`) accepts `--enemy-skin` and `reduced512` while preserving 5-argument backwards compatibility (`--droids no -> native-automatons`).
- **Gameplay Catalog `build25480438-r5` (`14` Downloads, `8,293,752` Bytes):**
  - Eliminates all `hf-mod.com` URLs ahead of the `2026-11-19` shutdown via permissive `0BSD`/`MIT` mirrors (`Vanilla Plus Megapack 0.2.1`, `Operation Reroller 0.1.6`, `Shared Callback Manager 0.1.0`), owner-permitted MP-hardened SEAF packages (`Heavy SEAF 1.2.0-clonedivers.4`, `SEAF Squad 0.1.4-clonedivers.3`), direct author GitHub releases (`Smarter SEAF 1.0.4`, `Smarter Guard Dogs & Sentries 5.2.0`, `Bingus Shared Loader v20`), and **6 project-owned SDK-16 optional defaults** (`clean-exit 0.2.0`, `lego-compact-leaf-writer 0.1.0`, `empire-lego-yoda 0.2.0`, `empire-atst-writer 0.1.0`, `empire-tie-switch 0.3.0`, `chaos-invasions 0.3.0`).
  - Upgrades enabled `r4` optional defaults automatically while preserving disabled/removed choices and disabling superseded `chef.smarter-seaf` and standalone `Mod Options Menu`.
- **Package & Upgrade Verification:**
  - `.github/workflows/forge-packages.yml` and `tools/qa-forge-package.sh` verify `0.8.0 -> 0.9.0` portable `.tar.gz` overlay and `.deb` `apt` upgrades, preserved profile/account/config fixtures, exact launcher/bridge pairing (`clonedivers-forge-channel.json`), and headless `xvfb` GUI smoke rendering across Ubuntu `22.04`, `24.04`, and `26.04`.
  - Keep `passt` (`pasta` on Ubuntu `24.04+` / Debian `13+` or equivalent) installed for supported namespace isolation. Live Linux Proton gameplay, account capture, extraction, and Windows/Linux co-op remain open for attended `R2` qualification. See [v2.0.0](v2.0.0.md).
