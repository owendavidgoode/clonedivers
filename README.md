<p align="center"><img src="docs/banner.png" width="720" alt="Clonedivers"></p>

# Clonedivers — Forge

Choose **Regular**, **Clones** or **Empire** in a separate HellForge installation.
Play the official online Helldivers 2 game directly through Steam.

[Windows launcher](https://github.com/owendavidgoode/clonedivers/releases/latest/download/Clonedivers.exe) · [Ubuntu/Debian x86-64 package](https://github.com/owendavidgoode/clonedivers/releases/download/linux-v0.8.2/Clonedivers-linux-x64.deb) · [Linux portable](https://github.com/owendavidgoode/clonedivers/releases/download/linux-v0.8.2/Clonedivers-linux-x64.tar.gz)

## Quick Start for Friends (Windows)

1. **Download and run `Clonedivers.exe`** — because the launcher is self-published, Windows Defender SmartScreen may show **"Windows protected your PC"**. Click **More info** -> **Run anyway**.
2. **Keep Helldivers 2 installed and updated in Steam** — Clonedivers uses your Steam ownership and HD2 install for Forge's game copy and mandatory account sync. Official online HD2 stays untouched in Steam.
3. **Allow ~70 GB free space** on a local folder outside OneDrive and Steam (for example `D:\HellForge`).
4. **Pick your mode & visuals first**: choose **Regular**, **Clones**, or **Empire**, your **Enemy skin** (`All CIS Droids`, `Mixed Bots + CIS Droids`, or `Native Automatons`), and **Textures** (`Full`, `Lighter (Streamed)`, or `Johnson Ultra-Potato (512px)` if your GPU has limited VRAM or slow asset streaming).
5. **Follow the 4-step Setup Wizard**:
   - **Step 1 · Preflight Check**: confirms Steam is signed in and 70 GB is free.
   - **Step 2 · Forge & Game Copy**: sign in with the **Steam Mobile App QR scanner**, or click **Official setup (fallback)** if you don't have the phone app. Windows will ask for administrator (UAC) approval two or three times.
   - **Step 3 · Guided Steam Account Sync (Required)**: required once before your first Forge launch so your real HD2 warbonds, unlocks, and progression copy locally. Leave *Apply import* and *auto-apply* off — Clonedivers imports the sealed capture automatically when HD2 and HellForge close. *(Never share your account capture files with anyone else.)*
   - **Step 4 · Pack Download & Ready**: downloads the gameplay preset and selected Star Wars pack with byte progress, then returns to **Ready to launch**.
6. **First launch firewall prompt**: click **Allow access** if Windows asks for network approval on your first launch.

## Update or Install

The current public versions are Windows **1.14.2** and Linux Forge **0.8.2**
(`pack-2026.11.09-r47-files` and `forge-mods-2026-11-15` published prerelease
stores; attended `R2` acceptance checks remain pending). Update Windows through
the launcher. On Linux, install the complete `.deb` or extract the full portable
archive.

New players press **Set up Clonedivers** on Windows or **Set up / update Forge**
on Linux. Windows checks for at least **70 GB** (`70 GiB`) free on the Forge
drive before installing or upgrading the verified Forge application, copies build
`25480438` from Steam or downloads it via Steam Guard QR approval, and installs
the gameplay preset and selected appearance. Linux downloads and opens the
official native installer and offers Ubuntu/Debian tool installation (`passt` /
`pasta` required). Steam sign-in and system approval remain player actions.
Existing Windows Forge `0.3.0-beta` installations offer **Update HellForge** with
protected-state checks and verified application rollback.

Read [the migration and first-setup guide](docs/FORGE-MIGRATION.md). The launcher
update preserves settings and account data and keeps a verified previous Windows
executable for rollback. It does not edit live Steam game files or close a running
game. **Set up selected mode** obtains **11 gameplay downloads**
automatically in the `build25480438-r4` catalog (**7 required** gameplay
archives totaling `7,780,423` bytes / `~7.42 MiB` plus **4 optional**
project-owned defaults: client-only `Clonedivers Clean Exit` `0.2.0` at `8,192`
bytes `gameplay: false`, client-only `LEGO Compact Leaf Writer (CO-OP v0.0.9)`
at `199,207` bytes `gameplay: false` `coop: per-player`, client-only
`Empire LEGO Yoda Death` `0.1.2` at `45,907` bytes `gameplay: false`
`coop: per-player`, and host-driven `Chaos Invasions` `0.2.0` at `29,526` bytes
`gameplay: true`), totaling **`8,063,255` bytes (`~7.69 MiB`)** before appearance
assets. It verifies their archives and content, imports them through Forge, applies
preset order/options and then prepares the appearance.

The three official bundled mods plus those 11 downloads (7 required + 4 optional
defaults enabled on first `r4` setup, and new `r4` optionals offered once on
`r3` -> `r4` upgrades while preserving prior opt-outs) and one selected appearance
pack form the complete 15-mod Clones or Empire setup; Regular omits appearance
packs. Five required packages come directly from authors; the permission-cleared
Heavy/Squad fixes use pinned project assets (`forge-gameplay-build25480438-r1`),
Clean Exit `0.2.0` uses `forge-mods-2026-11-08`, and `lego-compact-leaf-writer`
`0.0.9`, `empire-lego-yoda` `0.1.2`, and `chaos-invasions` `0.2.0` target
`forge-mods-2026-11-15` (pending `R1` asset upload). The old mine-boundary fix
(`0.1.1`) is disabled because engine `0.1.47` includes its bounds fix. See
[sources and permission scope](docs/SEAF-COMPATIBILITY-PERMISSION.md). Setup
turns automatic host mod sharing OFF and guides your own HD2 account import.
A fresh import of every dependency is tested separately from real mission and
multiplayer acceptance.

Every player needs their own Steam-owned HD2, official separate **HellForge
0.4.0-beta / engine 0.1.47 / SDK 16 / game 25480438**, and permitted matching mods.
Keep supported isolation and sandbox enabled. Linux requires a dedicated Proton
prefix and the official namespace setup (`pasta` from `passt` on Ubuntu 24.04+ /
Debian 13+ or equivalent). Complete Linux packages include the native UI and
controller (`0.4.0`); no separate .NET install is needed. ARM64, Steam Deck, and
Flatpak/Snap Forge are not qualified.

## Modes, Account, and Gameplay

Regular, Clones and Empire are Forge appearance choices. Clones retains Delta armor,
CIS droids, Covenant squids and texture options (`2026.09.27-r19`). In **1.14.2** /
**0.8.2**, **Empire** ships the four-way Compact-LEGO-baked **`2026.11.09-r47`**
catalogs (`Full` or `Lighter` streamed textures × `CIS droids` ON or OFF, with
SEAF Lambda `+0.20 m` ground clearance `lambda-fix`): `8,738,707,008` bytes
(`Full + Droids`), `6,013,620,208` bytes (`Full + Native`), `8,961,480,892` bytes
(`Lighter + Droids`, lowering resident `.gpu_resources` from `7,934,299,136` to
`2,542,440,456` bytes), and `6,172,775,164` bytes (`Lighter + Native`). Updating
an existing `r45-v4` Full Empire installation to `r47` Full reuses `34` files
(`455,980,288` bytes) and requires `~25 GB` free. Non-shipping experimental/shelved
items (`at-st` Hulk rig exceeding 64 palettes/128 nodes, `empire-weapons`,
per-instance `mixed-appearances`, and `empire-tie-switch` `0.2.1` pending
HellForge `>=0.4.1-beta` `eagle_base` support) remain outside `1.14.2` / `0.8.2`.
See [release notes](docs/releases/v1.14.2.md) and
[r47 Full notes](docs/releases/pack-2026.11.09-r47-full.md). Every mode uses
the player's own verified **one-way HD2 → Forge account import**. Forge progression
saves locally and does not count back in official HD2. Never share captures, profiles,
receipts or keys or replay stale captures to test persistence.

The scale-up baseline combines D11, Heavy SEAF + Squad Stratagems, native Smarter
SEAF, smarter dogs/sentries, reroller and supporting mods. [Coverage, dependencies
and author sources](docs/FORGE-MOD-COVERAGE.md) distinguish installed, optional,
conflicting and gated candidates. The download contains **no third-party mod
archives**; setup obtains the complete pinned preset automatically, including
the permission-cleared Heavy/Squad repairs. Missing dependencies block launch
rather than silently changing the experience. Each peer runs setup to obtain
matching artifacts; automatic host sharing stays OFF.

## Test Status

The owner has played solo D11 with working SEAF drops on the live `r45-v4` stack,
and Clean Exit `0.2.0` produced clean quits across the reported 20-cycle D11 soak.
On PR `#25` (`c90673f`), Windows `run-suites.ps1` (`0` failures) and `11/11` CI
jobs pass (including `xvfb` Linux package/GUI smoke across Ubuntu `22.04`, `24.04`,
and `26.04`). Wave 2 live testing also passed on `r47-shuttles` (`W4` `12+12` D11
Automaton A/B gate: `14` cycles with `0` crash events vs `r45-v4` `12` cycles /
`1` `+0x1e6b3e` event), `empire-lego-yoda` `0.1.1` + `empire-lego-camera` `0.0.2`
(Sub-Run 1A), `lego-compact-leaf-writer` `0.0.9` on `r47-lego-compact` (Sub-Run 1E),
and `empire-tie-switch` `0.2.1` co-loaded with `clean-exit` `0.2.0` on patched
`tie-held-root` (Sub-Run 1D Part 1). Offline integration on `1.14.2` / `0.8.2` also
verified all four Compact-LEGO-baked `r47` variants (`verify_candidate.py` exit `0`),
`empire-lego-yoda` `0.1.2` (`139` C checks, `ScanVisuals == 4` on both `r47-lego-compact`
and `r47-shuttles`), `chaos-invasions` `0.2.0` (`195` C checks, `52` LuaJIT checks,
`15` delivery checks), and `run-suites.ps1` (`0` failures / `0` warnings). See
[Forge setup and status](docs/FORGE.md).
Attended `R2` landed Lambda clearance, Compact LEGO multi-actor co-op, `chaos-invasions`
live 3/4/5-way combat/co-op, `empire-lego-yoda` `0.1.2` live Compact LEGO audio, Linux
Proton gameplay, account capture, mission reward persistence, and multiplayer remain open. Use the
[separate-PC squad checklist](docs/HELLFORGE-SQUAD-ACCEPTANCE.md) for joins, drops,
extraction and reconnect tests. Native Quit hanging/teardown crashes (`+0x626296`)
and ship distortion remain open; use **End session** for official cleanup.

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
