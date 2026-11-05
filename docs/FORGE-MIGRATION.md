# Updating Clonedivers to Forge

Clonedivers is now the Forge launcher: Regular, Clones and Empire run in a
separate HellForge installation. Launch the official online HD2 game from Steam.
Windows version 1.10.0 replaces the old launcher through its existing update
button. Linux x86-64 version 0.6.0 replaces the old package/portable launcher.

This changes the launcher. It does not install HellForge, game files, restricted
gameplay mods or another player's account. Linux gameplay and Windows–Linux
multiplayer still require real-machine testing. A successful update is not a
multiplayer certification.

## Windows

Close HD2, open the old launcher and accept its launcher update. All three old
manifest routes advertise the same SHA256-verified Forge executable; their game
pack data is preserved. If the launcher cannot replace itself, download
Clonedivers.exe from the latest GitHub release into a writable folder.

The new launcher opens Forge by default. An adjacent .old.exe left by the updater
is copied to `%LOCALAPPDATA%\Clonedivers\launcher-backups\<SHA256>.exe` before
cleanup. If backup fails, the adjacent old file stays in place. Existing settings,
Steam files, Forge account/profile and mod options are not reset. A manual
download should go in a new folder; keep the prior executable yourself.

Use Check updates inside the Forge launcher for later releases. It accepts only
the explicitly marked Forge channel and the repository's pinned versioned EXE,
with exact size/SHA256 and idle checks before replacement. Game/account-sync
sessions must finish first. The original updater already asks before downloading
and restarting; this rollout does not force-close anyone's game or launcher.

## Ubuntu/Debian x86-64

The old Linux launcher checks for versioned Linux releases and will show the new
0.6.0 release. It does not replace installed software by itself. Download the
complete `Clonedivers-linux-x64.deb` from `linux-v0.6.0`, close the launcher, then
install it with your package manager or:

```sh
sudo apt install ./Clonedivers-linux-x64.deb
```

The existing Clonedivers application/menu entry now opens Forge. Its native
binary and adjacent forge/ companion are installed together under
`/usr/lib/clonedivers`; `/usr/bin/clonedivers` forwards arguments to that binary.
User configuration and account files are outside the package and are preserved.

Portable users should extract the **entire** new TAR.GZ into a fresh directory,
keep `clonedivers` beside `forge/`, and run `./clonedivers --gui`. Replacing only
the executable is insufficient. Keep the old directory for rollback. Later
Forge update notices use a separate cache and require a Forge channel marker,
so they cannot accidentally offer a legacy Steam-only package.

Only x86-64 Forge packages are supported. ARM64/Deck/Flatpak/Snap and actual
Linux account capture are not qualified. No old ARM64 package is relabeled as
working Forge support.

## First Forge setup

1. Obtain official HellForge **0.3.0-beta**, engine **0.1.41**, SDK **15**, game
   **25480438**. Keep supported isolation and sandbox enabled. Use a separate
   installation outside Steam. Linux needs the official namespace setup in a
   dedicated Proton prefix with Forge under its `pfx/drive_c`.
2. In Clonedivers, use Setup & mods and Locate/Connect. The pinned official CLI
   performs setup/session/mod operations. Updating the launcher never repairs
   an active game reporting staged gameCopy=false.
3. Import only your own verified HD2 account through the official backend.
   Progress imports HD2 → Forge; Forge progress does not count back in official
   HD2. Do not share captures, profiles, receipts or keys or reimport stale data.
4. Obtain permitted matching gameplay mods and prepare the chosen appearance.
   The full Heavy/Squad baseline needs author-compatible artifacts/permissions;
   private repairs are not redistributed. See FORGE-MOD-COVERAGE.md and
   preset.json. A fresh installation remains blocked until its preset is complete.
5. Manually turn OFF official host gameplay-mod sharing before using private
   adaptations. Each peer needs permitted matching mods. Check account, options,
   startup and mission behavior before a controlled two-PC test.

## One-time Steam cleanup and recovery

The update does **not** remove old Clonedivers files from Steam's live game.
If that game still has the old pack enabled, close the game and use the retained
legacy launcher (or the new launcher's explicit `--steam` recovery mode) to select
Helldivers and park those old mods yourself before treating Steam as vanilla.
Review any other installed mods before changing them. No automatic live Steam
file edit or forced game shutdown is part of this migration.

The main launcher stays Forge. Hidden `--steam` is a recovery tool, not the
normal player workflow. For regular official HD2, use Steam directly. Roll back
launcher software with the retained executable/package, never by restoring an
old account profile. Native in-game Quit hanging remains open; use End session
for official Forge cleanup. See docs/FORGE.md and the squad acceptance checklist
in the repository for qualification details.
