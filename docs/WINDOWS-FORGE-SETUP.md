# Guided Windows setup — Clonedivers 1.12.0

The normal first-run action is **Set up Clonedivers**, not a release-page link or
manual directory selection. The wizard presents Steam, Forge and game-copy
readiness with one primary action. Once official game setup passes, the same
flow obtains and installs all required gameplay mods and the selected appearance.
Existing players use **Install required mods** / **Set up selected mode**. Controls
stay visible outside scrolling text.

## Supported boundary

The pinned author `HellForge-Setup.exe --unpack <folder>` installs the application
without its installer UI. The player's official `HellForge.exe` still performs
the initial game copy, ownership and network isolation. The supported external
launcher API explicitly delegates that first setup and repairs to its manager.
Clonedivers opens it, provides the necessary next action and detects completion;
it never changes an engine, creates its own network rules, automates UAC/Steam
authentication, repairs an active staged gameCopy=false or resets accounts.

Author release:
https://github.com/SkyeShade/HellForge/releases/tag/v0.3.0-beta

Author integration contract:
https://github.com/SkyeShade/HellForge/blob/b1aef771474710eab5461d360444cd80a809c70c/docs/external-launchers.md

Installer entry point:
https://github.com/SkyeShade/HellForge/blob/b1aef771474710eab5461d360444cd80a809c70c/src/HellForge.Setup/Program.cs

Pinned artifact: 315829864 bytes,
SHA256 b815655998d6b4b31cd12e821460cd0bbde5e360c2886381ca59e051a106a7e4.
The release API digest and existing author-provided binary agree. Downloads come
directly from the author; the Clonedivers EXE does not bundle Forge/game/private mods.

## Recovery behavior

- Safe default outside Steam/OneDrive, optional drive selection, free-space check.
- Resumable verified download, exclusive cache lock, installer pinned through exit.
- Active-game checks before download and again before unpack; no game shutdown.
- Fresh staging beside the destination; reject a changed/nonempty target and
  nested existing installations. Only a verified new app tree is promoted.
- Failed staging is retained; no recursive cleanup or restoration over profiles.
- Connected roots are saved immediately and remain available after stopping setup.
- Read-only official status polling; actual active-session state takes precedence.

Source fixtures exercise interrupted download, hash failure, preserved owner data,
target races, wrong-build payload, a game starting during download, path boundaries
and the first-run/active-session UI states. A real pinned installer unpack and CLI
status query passed in an isolated Windows folder without running game setup,
ownership, network changes or account import. Real new-player game-copy/UAC,
account onboarding and separate-PC multiplayer acceptance are distinct remaining
tests. Windows 1.12.0 and Linux 0.6.2 share the complete pinned gameplay catalog;
Linux downloads outside the namespace and imports through bridge 0.3.9. Clean
real-CLI mod installation, failed imports, lost acknowledgements, restart recovery,
no-op setup, corrupt-cache repair and profile-file preservation have passed in
an isolated installation. These tests do not authenticate an account, prepare a
game copy or run a mission. See COMPLETE-PRESET-INSTALLER-2026-11-05.md.
