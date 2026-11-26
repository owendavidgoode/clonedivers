# Clonedivers Forge for Linux

The current Ubuntu/Debian x86-64 and portable Linux Forge release is **0.10.4** (`linux-v0.10.4`), paired in lockstep with Windows **2.1.4** (`v2.1.4`) and native controller **0.4.0** (`HellForge.Launcher.ClonediversBridge`).
Install the complete `.deb` or portable archive, including the adjacent `forge/`
native controller. Existing launchers show an update notice; install the package
or replace the complete portable folder. User configuration and accounts remain.

Press **Set up / update Forge** in `0.10.4`. The launcher downloads the pinned official
**HellForge 0.4.0-beta** native installer, verifies its SHA256, safely extracts it
and opens it. On Ubuntu/Debian it offers installation of the required networking
tools, including `pasta` from `passt`, through the normal system approval flow.
Ubuntu 22.04 repositories may lack `passt`: the launcher package can install,
but gameplay stays blocked until the official relay prerequisite is available.
Choose the dedicated Proton prefix in the official installer. Existing selected
roots take precedence; ambiguous installations require an explicit selection.

Finish the official game-copy/Steam ownership setup. The launcher reconnects and
continues the `18` verified `build25480438-r11` downloads (`9` required + `9` optional
SDK-16 defaults), preset options/order, automatic host sharing OFF, and your selected
5-tier Star Wars appearance (`Clonedivers 2026.11.25-r23` or `EmpireDivers 2026.11.25-r52`). It guides the official account-capture
UI and applies a verified fresh capture automatically once capture cleanup completes. Account import is one-way.
Old captures cannot replace progression earned in a native Forge profile.

The native controller calls the installed native CLI/backend directly. It does
not overlay old Wine bridge scripts or fabricate namespace/account state. The
official session runner performs isolation, relay, sandbox and ownership checks
before running the game. No game, account or third-party mod archives are bundled.

See [migration and recovery](FORGE-MIGRATION.md) and the
[0.4 integration evidence](FORGE-0.4-TURNKEY-2026-11-05.md). Native account capture,
Proton gameplay and Windows–Linux squad play require real-PC acceptance; source
and package checks do not qualify those paths.

Run `clonedivers --gui` for Regular/Clones/Empire and `clonedivers --help` for
read-only terminal diagnostics. Use Steam for official online HD2. Explicit
`--steam` opens [legacy recovery mode](LEGACY-LINUX.md). ARM64, Deck and
Flatpak/Snap Forge are not qualified.
