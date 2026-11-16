<p align="center"><img src="docs/banner.png" width="720" alt="Clonedivers"></p>

# Clonedivers — Forge

Choose **Regular**, **Clones** or **Empire** in a separate HellForge installation.
Play the official online Helldivers 2 game directly through Steam.

[Windows launcher](https://github.com/owendavidgoode/clonedivers/releases/latest/download/Clonedivers.exe) · [Ubuntu/Debian x86-64 package](https://github.com/owendavidgoode/clonedivers/releases/download/linux-v0.8.1/Clonedivers-linux-x64.deb) · [Linux portable](https://github.com/owendavidgoode/clonedivers/releases/download/linux-v0.8.1/Clonedivers-linux-x64.tar.gz)

## Update or Install

The current public versions are Windows **1.14.1** and Linux Forge **0.8.1**.
Update Windows through the launcher. On Linux, install the complete `.deb` or
extract the full portable archive.

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
game. **Set up selected mode** obtains **8 additional gameplay downloads**
automatically in the live `build25480438-r3` catalog (**7 required** gameplay
archives totaling `7,780,423` bytes / `~7.42 MiB` plus **1 optional** client-only
`Clonedivers Clean Exit` `0.2.0` archive `clean-exit-0.2.0.zip` at `8,192` bytes,
`gameplay: false`), totaling **`7,788,615` bytes (`~7.43 MiB`)** before appearance
assets. It verifies their archives and content, imports them through Forge, applies
preset order/options and then prepares the appearance.

The three official bundled mods plus those 8 downloads (7 required + optional Clean
Exit enabled by default on first setup) and one selected appearance pack form the
complete 12-mod Clones or Empire baseline; Regular omits appearance packs. (A
staged `build25480438-r4` experimental catalog on branch `claude/exp-yoda-011-20261115`
is not live and would add optional `empire-lego-yoda` `0.1.1` at `45,502` bytes for
9 catalog downloads / `7,834,117` bytes if selected; Wave 2 live death verification
passed in Sub-Run 1A, and publication awaits Wave 3.) Five required packages come
directly from authors; the permission-cleared Heavy/Squad fixes use pinned project
assets (`forge-gameplay-build25480438-r1`), and Clean Exit `0.2.0` uses a pinned
project asset (`forge-mods-2026-11-08`). The old mine-boundary fix (`0.1.1`) is
disabled because engine `0.1.47` includes its bounds fix. See [sources and
permission scope](docs/SEAF-COMPATIBILITY-PERMISSION.md). Setup turns automatic
host mod sharing OFF and guides your own HD2 account import. A fresh import of
every dependency is tested separately from real mission and multiplayer acceptance.

Every player needs their own Steam-owned HD2, official separate **HellForge
0.4.0-beta / engine 0.1.47 / SDK 16 / game 25480438**, and permitted matching mods.
Keep supported isolation and sandbox enabled. Linux requires a dedicated Proton
prefix and the official namespace setup (`pasta` from `passt` on Ubuntu 24.04+ /
Debian 13+ or equivalent). Complete Linux packages include the native UI and
controller (`0.4.0`); no separate .NET install is needed. ARM64, Steam Deck, and
Flatpak/Snap Forge are not qualified.

## Modes, Account, and Gameplay

Regular, Clones and Empire are Forge appearance choices. Clones retains Delta armor,
CIS droids, Covenant squids and texture options (`2026.09.27-r19`). The live
`1.14.1` / `0.8.1` release uses the published **Empire `2026.11.08-r45-v4` Full**
appearance with fixed CIS enemy models (`8,435,474,224` bytes / `~7.86 GiB`).
Experimental Empire Lighter, CIS-off variants (`0ed1ac8`) and compact LEGO
proportion changes (`lego-compact-leaf-writer` `0.0.9` on `claude/exp-lego-20261115`)
are not included in the default `1.14.1` / `r47` Full baseline. Saved unavailable
options are retained until you explicitly choose **Use published Empire appearance**
or return to Clones. Candidate `1.14.2` / `0.8.2` launcher fixes and the `r47-shuttles`
Full appearance candidate (which passed Wave 2's `W4` A/B gate and awaits Wave 3 asset
upload plus attended `R2` verification, requiring `~25 GB` free and `~7.71 GiB` download
when updating from `r45-v4`) are staged in [release notes](docs/releases/v1.14.2.md)
and [r47 Full notes](docs/releases/pack-2026.11.09-r47-full.md). Every mode uses
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
`tie-held-root` (Sub-Run 1D Part 1). See [Forge setup and status](docs/FORGE.md).
Attended `R2` landed Lambda clearance, Linux Proton gameplay, account capture,
mission reward persistence, and multiplayer remain open. Use the
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
