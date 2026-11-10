<p align="center"><img src="docs/banner.png" width="720" alt="Clonedivers"></p>

# Clonedivers — Forge

Choose **Regular**, **Clones** or **Empire** in a separate HellForge installation.
Play the official online Helldivers 2 game directly through Steam.

[Windows launcher](https://github.com/owendavidgoode/clonedivers/releases/latest/download/Clonedivers.exe) · [Ubuntu/Debian x86-64 package](https://github.com/owendavidgoode/clonedivers/releases/download/linux-v0.8.0/Clonedivers-linux-x64.deb) · [Linux portable](https://github.com/owendavidgoode/clonedivers/releases/download/linux-v0.8.0/Clonedivers-linux-x64.tar.gz)

## Update or install

The current public versions are Windows **1.14.0** and Linux Forge **0.8.0**.
Update Windows through the launcher. On Linux, install the complete .deb or
extract the full portable archive. This branch stages Windows **1.14.1** and
Linux Forge **0.8.1** pending pack acceptance and release verification.

New players press **Set up Clonedivers** on Windows or **Set up / update Forge**
on Linux. Windows installs or upgrades the verified Forge application; Linux
downloads and opens the official native installer and offers Ubuntu/Debian tool
installation. After official game setup, the launcher installs the gameplay
preset and selected appearance. Steam sign-in and system approval remain player
actions. Existing Windows Forge 0.3 installations have an **Update HellForge**
button with protected-state checks and verified application rollback.

Read [the migration and first-setup guide](docs/FORGE-MIGRATION.md). The launcher
update preserves settings and account data and keeps a verified previous Windows
executable for rollback. It does not edit live Steam game files or close a running
game. **Set up selected mode** obtains all seven additional required gameplay
packages automatically, verifies their archives/content, imports them through
Forge, applies preset order/options and then prepares the appearance.

The three official bundled mods plus those seven downloads form the complete
gameplay preset; Clones/Empire add their selected appearance. Downloads total
about 7.4 MB before appearance assets. Five packages come directly from authors;
the permission-cleared Heavy/Squad fixes use pinned project assets. The old mine
fix is disabled because the new engine includes its fix. See [sources and
permission scope](docs/SEAF-COMPATIBILITY-PERMISSION.md). Setup turns automatic
host mod sharing OFF and guides your own HD2 account import. A fresh import of
every dependency is tested separately
from real mission and multiplayer acceptance.

Every player needs their own Steam-owned HD2, official separate **HellForge
0.4.0-beta / engine 0.1.47 / SDK 16 / game 25480438**, and permitted matching mods.
Keep supported isolation and sandbox enabled. Linux requires a dedicated Proton
prefix and the official namespace setup. Complete Linux packages include the
native UI and controller; no separate .NET install
is needed. ARM64, Deck and Flatpak/Snap Forge are not qualified.

## Modes, account and gameplay

Regular, Clones and Empire are Forge appearance choices. Clones retains Delta armor,
CIS droids, Covenant squids and texture options. Empire retains the published r45-v4
Full appearance with fixed enemy models; its experimental texture and enemy switches
are not included. Saved unavailable options are retained until you explicitly choose
the published Empire appearance or return to Clones. This launcher update does not
change LEGO proportions or include the experimental r47 pack. Every mode uses the player's
own verified **one-way HD2 → Forge account import**. Forge progression saves
locally and does not count back in official HD2. Never share captures, profiles,
receipts or keys or replay stale captures to test persistence.

The scale-up baseline combines D11, Heavy SEAF + Squad Stratagems, native Smarter
SEAF, smarter dogs/sentries, reroller and supporting mods. [Coverage, dependencies
and author sources](docs/FORGE-MOD-COVERAGE.md) distinguish installed, optional,
conflicting and gated candidates. The download contains **no third-party mod
archives**; setup obtains the complete pinned preset automatically, including
the permission-cleared Heavy/Squad repairs. Missing dependencies block launch
rather than silently changing the experience. Each peer runs setup to obtain
matching artifacts; automatic host sharing stays OFF.

## Test status

The owner has played solo D11 with working SEAF drops. Windows source/session
checks, native/bridge fixtures and Linux package installation/help checks are
recorded in the [Forge setup/status](docs/FORGE.md) and CI. These do not certify
Linux Proton gameplay, account capture, mission reward persistence or multiplayer.
Use the [separate-PC squad checklist](docs/HELLFORGE-SQUAD-ACCEPTANCE.md) for joins,
drops, extraction and reconnect tests. Native Quit hanging and ship distortion
remain open; use **End session** for official cleanup.

## Steam cleanup and recovery

Updating the launcher does not remove previously enabled Clonedivers mods from
Steam. Follow the one-time cleanup section in the migration guide before treating
Steam as vanilla. Retained old launchers and explicit `--steam` mode are recovery
tools. The default application is Forge. [Legacy Linux notes](docs/LEGACY-LINUX.md)
are retained for recovery, not the current installation path.

## Development and license

Build with .NET8. Fetch the pinned PresentMon input using tools/fetch-presentmon.ps1
for Windows, then run Clonedivers.Tests. Forge is the Linux build default;
`-p:ForgeEdition=false` is only for legacy recovery tests. The Forge package
workflow builds complete assets and checks them on Ubuntu22.04/24.04/26.04. It
never packages accounts, game files or restricted gameplay mods.

[MIT](LICENSE) for the launcher and original mine-boundary source. Community mods
belong to their credited authors. The official HellForge installer retains its
own upstream licensing. PresentMon and native
runtime licenses accompany their distributions. Fan utility, not affiliated with
Arrowhead/Sony or Lucasfilm.
