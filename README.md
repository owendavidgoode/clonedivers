<p align="center"><img src="docs/banner.png" width="720" alt="Clonedivers"></p>

# Clonedivers — Forge

Choose **Regular**, **Clones** or **Empire** in a separate HellForge installation.
Play the official online Helldivers 2 game directly through Steam.

[Windows launcher](https://github.com/owendavidgoode/clonedivers/releases/latest/download/Clonedivers.exe) · [Ubuntu/Debian x86-64 package](https://github.com/owendavidgoode/clonedivers/releases/download/linux-v0.10.2/Clonedivers-linux-x64.deb) · [Linux portable](https://github.com/owendavidgoode/clonedivers/releases/download/linux-v0.10.2/Clonedivers-linux-x64.tar.gz)

## Quick Start for Friends (Windows)

1. **Download and run `Clonedivers.exe`** — because the launcher is self-published, Windows Defender SmartScreen may show **"Windows protected your PC"**. Click **More info** -> **Run anyway**.
2. **Keep Helldivers 2 installed and updated in Steam** — Clonedivers uses your Steam ownership and HD2 install for Forge's game copy and mandatory account sync. Official online HD2 stays untouched in Steam.
3. **Allow ~70 GB free space** on a local folder outside OneDrive and Steam (for example `D:\HellForge`).
4. **Pick your mode & visuals first**: choose **Regular**, **Clones**, or **Empire**, plus **Texture Quality** (`Full (Ultra)`, `High (Streamed)`, `1024px (Medium)`, `512px (Low / Deck)`, or `256px + Decimated Meshes (Ultra-Potato)` — up to `-80%` VRAM and `-66%` disk). New installs default to **Full Star Wars Mix** (`Mixed Bots + CIS` and `Mixed Covenant + Squids`); enemy skins, Delta Squad, and Imperial Encrypted Death Trooper voice are under **Customize -> Advanced**.
5. **Follow the 4-step Setup Wizard**:
   - **Step 1 · Preflight Check**: confirms Steam is signed in and 70 GB is free.
   - **Step 2 · Forge & Game Copy**: sign in with the **Steam Mobile App QR scanner**, or click **Official setup (fallback)** if you don't have the phone app. Windows will ask for administrator (UAC) approval two or three times.
   - **Step 3 · Guided Steam Account Sync (Required)**: required once before your first Forge launch so your real HD2 warbonds, unlocks, and progression copy locally. Leave *Apply import* and *auto-apply* off — Clonedivers imports the sealed capture automatically when HD2 and HellForge close. *(Never share your account capture files with anyone else.)*
   - **Step 4 · Pack Download & Ready**: downloads the gameplay preset and selected Star Wars pack with byte progress, then returns to **Ready to launch**.
6. **First launch firewall prompt**: click **Allow access** if Windows asks for network approval on your first launch.

## Update or Install

The current live public releases are **Windows 2.1.2** ([v2.1.2](https://github.com/owendavidgoode/clonedivers/releases/tag/v2.1.2)) and **Linux Forge 0.10.2** ([linux-v0.10.2](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.10.2)), shipping **5 real texture tiers** across **EmpireDivers `2026.11.22-r50`** (`261` armor/helmet/K-2SO/NPC repairs including all `74` unstretched JFO helmets and `7` stray Clone helmet replacements, AAA *Jedi: Fallen Order* AT-ST with dual `2048×2048` PBR on all 4 Exosuits, closed-belly SEAF Lambda & clean Extraction Lambda + zero leftover Republic LAATs, full British Imperial voice cast + `2,680` SEAF Stormtrooper lines with Death Trooper voice opt-in OFF by default, complete SEAF/weapon blasters & Star Destroyer orbital turbolasers) and **Clonedivers `2026.11.22-r21`**, **3-way Covenant + Squids mix**, and **`build25480438-r9`** (`18` verified mods, `8,505,056` B, `0` `hf-mod.com` URLs, adding runtime **`starwars-localization 0.1.0`** Armory name restoration and D11 Mega City bot-drop-fixed **`chaos-invasions 0.3.4`** alongside O(1) `empire-camera-pullback 0.1.3`, 4-player co-op `dedicated-melee-slot 1.5.2-clonedivers.2`, host-gated `faster-corpse-cleanup 1.0.6-clonedivers.1`, `empire-atst-writer 0.2.2`, and `heavy-seaf-presence 1.2.0-clonedivers.5`; unlocked desktop stratagem call-ins, attended live `R2` multiplayer, and Linux Proton checks remain pending). Update Windows through the launcher. On Linux, install the complete `.deb` or extract the full portable archive.

New players press **Set up Clonedivers** on Windows or **Set up / update Forge**
on Linux. Windows checks for at least **70 GB** (`70 GiB`) free on the Forge
drive before installing or upgrading the verified Forge application, copies build
`25480438` from Steam or downloads it via Steam Guard QR approval, guides mandatory
one-way Steam account sync, and installs the gameplay preset and selected appearance.
Linux downloads and opens the official native installer and offers Ubuntu/Debian tool
installation (`passt` / `pasta` required). Steam sign-in and system approval remain
player actions. Existing Windows Forge `0.3.0-beta` installations offer **Update
HellForge** with protected-state checks and verified application rollback.

Read [the migration and first-setup guide](docs/FORGE-MIGRATION.md). The launcher
update preserves settings and account data and keeps a verified previous Windows
executable for rollback. It does not edit live Steam game files or close a running
game. **Set up selected mode** obtains **17 gameplay downloads** automatically in
the `build25480438-r8` catalog (**9 required** gameplay and author archives totaling
`7,991,821` bytes / `~7.62 MiB` including host-gated `Faster Corpse Cleanup` `1.0.6-clonedivers.1` and `Heavy SEAF` `1.2.0-clonedivers.5`, plus **8 optional** project-owned SDK-16 defaults totaling `502,723` bytes: `Clonedivers Clean Exit` `0.2.0`, `LEGO Compact Leaf Writer` `0.2.1`, `Empire LEGO Yoda Death` `0.2.0`, `Empire AT-ST Film-Scale Leaf Writer` `0.2.2`, `Empire per-attack TIE` `0.3.0`, O(1) `Empire Camera Pullback` `0.1.3`, co-op `Dedicated Melee Slot` `1.5.2-clonedivers.2`, and Mega City OOM-safe `Chaos Invasions` `0.3.3`), totaling **`8,494,544` bytes (`~8.10 MiB`)** before appearance assets. It verifies their
archives and content, imports them through Forge, applies preset order/options and
then prepares the appearance.

The three official bundled mods plus those 14 downloads (8 required + 6 optional
defaults enabled on fresh `r7` setup, with enabled `r4`/`r5`/`r6` defaults upgraded to `0.2.0`
/ `0.2.1` / `0.3.0` and newly added `r5` optionals offered once while preserving
disabled/removed choices) and one selected appearance pack form the complete 18-mod
Clones or Empire setup; Regular omits appearance packs. Following the **19 November
2026** `hf-mod.com` shutdown, `r7` uses **zero `hf-mod.com` URLs**: permissive `0BSD`
/ `MIT` mirrors (`Vanilla Plus Megapack 0.2.1`, `Operation Reroller 0.1.6`, `Shared
Callback Manager 0.1.0`) and owner-permitted MP-hardened SEAF packages (`Heavy SEAF
1.2.0-clonedivers.4`, `SEAF Squad 0.1.4-clonedivers.3`) target
`forge-gameplay-build25480438-r5`, while `Bingus Shared Loader v20`, `Smarter SEAF
1.0.4`, and `Smarter Guard Dogs & Sentries 5.2.0` download directly from their
authors' GitHub Releases (`All rights reserved` Th3chef archives are never mirrored).
The old mine-boundary fix (`0.1.1`), `chef.smarter-seaf` (`1.1.0`), and standalone
`Mod Options Menu v1.2.1` (now integrated in `Vanilla Plus Megapack 0.2.1`) are
disabled automatically. See [sources and permission scope](docs/SEAF-COMPATIBILITY-PERMISSION.md).
Setup turns automatic host mod sharing OFF and guides your own HD2 account import.
A fresh import of every dependency is tested separately from real mission and
multiplayer acceptance.

Every player needs their own Steam-owned HD2, official separate **HellForge
0.4.0-beta / engine 0.1.47 / SDK 16 / game 25480438**, and permitted matching mods.
Keep supported isolation and sandbox enabled. Linux requires a dedicated Proton
prefix and the official namespace setup (`pasta` from `passt` on Ubuntu 24.04+ /
Debian 13+ or equivalent). Complete Linux packages include the native UI (`0.10.1`)
and controller (`0.4.0`); no separate .NET install is needed. ARM64, Steam Deck, and
Flatpak/Snap Forge are not qualified.

## Modes, Account, and Gameplay

Regular, Clones and Empire are Forge appearance choices. In **2.1.2** / **0.10.2**,
both **Clones (`2026.11.22-r21`)** and **Empire (`2026.11.22-r50`)** ship the full
**5-tier texture matrix** (`Full (Ultra)`, `High (Streamed)`, `1024px (Medium)`,
`512px (Low / Deck)`, and `256px + Decimated Meshes (Ultra-Potato)` — cutting active
VRAM up to `-80%` and pack disk up to `-66%`), **3 Enemy Skins** (`All CIS Droids`,
`Mixed Bots + CIS Droids`, `Native Automatons`), and **3-way Covenant + Squids**
(`Mixed Covenant + Squids` default, `All Covenant`, `Native Squids`), plus **Delta Squad**
in Clones and opt-in **Encrypted Death Trooper Voice** (OFF by default) in Empire.
See [v2.1.2 release notes](docs/releases/v2.1.2.md), [linux-v0.10.2 notes](docs/releases/linux-v0.10.2.md),
and [JohnsonMode notes](docs/JOHNSONMODE.md). Every mode uses
the player's own verified **one-way HD2 → Forge account import**. Forge progression
saves locally and does not count back in official HD2. Never share captures, profiles,
receipts or keys or replay stale captures to test persistence.

The scale-up baseline combines D11, MP-hardened Heavy SEAF + Squad Stratagems, Smarter
SEAF `1.0.4`, smarter dogs/sentries, host-gated Operation Reroller `0.1.6`, Shared
Callback Manager `0.1.0`, and Vanilla Plus Megapack `0.2.1`. [Coverage, dependencies
and author sources](docs/FORGE-MOD-COVERAGE.md) distinguish installed, optional,
conflicting and gated candidates. The download contains **no third-party mod
archives**; setup obtains the complete pinned preset automatically, including
the permission-cleared Heavy/Squad repairs. Missing dependencies block launch
rather than silently changing the experience. Each peer runs setup to obtain
matching artifacts; automatic host sharing stays OFF.

## Test Status

The owner has played solo D11 with working SEAF drops on the live `r45-v4` stack,
and Clean Exit `0.2.0` produced clean quits across the reported 20-cycle D11 soak.
Wave 2 live testing passed on `r47-shuttles` (`W4` `12+12` D11 Automaton A/B gate:
`14` cycles with `0` crash events), `empire-lego-yoda` `0.1.1`, `lego-compact-leaf-writer`
`0.0.9`, and `empire-tie-switch` `0.2.1` (Sub-Run 1D Part 1). For **v2.0 (`2.0.0` /
`0.9.0`)**, offline gatekeeper verification (`WAVE2A-VERIFIED-REPORT.md` + Wave 2B)
passed across all `r48` and `r20` overlays (`verify_candidate.py` exit `0`: `0`
geometry findings, `0` missing dependencies, `0` audio codec mismatches across `437`
banks / `5,266` VO slots), all `6` SDK-16 C11 mods (`zig cc -Wall -Werror` `0`
warnings, `163,000+` C unit/integration checks + LuaJIT `lua51.dll` bytecode checks),
`mp_sim_test.py` (`65` 4-peer mixed co-op, ModSync, late-join, and host-migration
checks), all `5` offline launcher/bridge/Linux/forge-lab test suites (`0` failures,
`0` warnings), and isolated `tools/uia-wizard-smoke.ps1` (`ALL UIA SINGLE-WIZARD SMOKE
CHECKS PASSED`). Automated Wave 3 live verification additionally passed **`10/10`**
serialized `helldivers2.exe` runs (`exitCode=0`, `0` crashes/dumps/writer failures/Chaos
fallbacks, `118/118` screenshots verified across `Empire r48` Compact LEGO, K-2SO, Bad
Batch, and unfolded E-Web; `Clones r20` 4 sampled Compact LEGO kits — Cody, Coruscant
Guard, Rex, and 501st — plus `Mixed Bots + CIS` and `Covenant 1670`; `chaos-invasions
0.3.0`; `Heavy SEAF`; and live `empire-lego-yoda 0.2.0`), plus a 6-run `PresentMon
2.5.1` benchmark on a single high-VRAM test PC (`1280x720`) proving `Johnson
Ultra-Potato 512px` cuts Peak Dedicated VRAM `-40.6%` (`18,154.53 -> 10,784.25 MiB`),
Shared GPU spillover `-96.0%`, WorkingSet RAM `-44.5%`, and Boot-to-Ship `-27.7%`
(background average FPS did not improve in this test). See [Forge setup and
status](docs/FORGE.md) and [STATUS.md](docs/STATUS.md). Attended live `R2` desktop
verification (`runtimeTested: false` until attended `R2` passes) remains open for
EXO-45/49 walker pod call-in, live Eagle TIE beacon throws, speaker voice mix, Empire
Covenant (`patch_1032`) coexistence, Phase I Shiny and 212th Compact LEGO Clone kits,
weak-PC (low-VRAM) `512px` frame-time, 4-peer human co-op & host migration, clean-PC
wizard run, Linux Proton gameplay, and separate-PC squad sessions ([squad
checklist](docs/HELLFORGE-SQUAD-ACCEPTANCE.md)). Native Quit hanging/teardown crashes
(`+0x626296`) and ship distortion remain open; use **End session** for official cleanup.

## Steam Cleanup and Recovery

Updating the launcher does not remove previously enabled Clonedivers mods from
Steam. Follow the one-time cleanup section in the migration guide before treating
Steam as vanilla. Retained old launchers and explicit `--steam` mode are recovery
tools. The default application is Forge. [Legacy Linux notes](docs/LEGACY-LINUX.md)
are retained for recovery, not the current installation path.

## Development and License

Build with .NET 8. Fetch the pinned PresentMon input using `tools/fetch-presentmon.ps1`
for Windows, then run `Clonedivers.Tests`. Forge is the Linux build default;
`-p:ForgeEdition=false` is only for legacy recovery tests. The Forge package
workflow builds complete assets and checks them on Ubuntu 22.04/24.04/26.04. It
never packages accounts, game files or restricted gameplay mods.

[MIT](LICENSE) for the launcher and original mine-boundary source. Community mods
belong to their credited authors. The official HellForge installer retains its
own upstream licensing. PresentMon and native
runtime licenses accompany their distributions. Fan utility, not affiliated with
Arrowhead/Sony or Lucasfilm.
