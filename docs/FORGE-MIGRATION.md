# Updating Clonedivers to Forge

Clonedivers is now the Forge launcher: Regular, Clones and Empire run in a
separate HellForge installation. Launch the official online HD2 game from Steam.
Windows version 1.11.0 replaces the old launcher through its existing update
button. Linux x86-64 version 0.6.1 replaces the old package/portable launcher.

Windows now includes guided installation of the pinned official Forge application;
its official wizard still obtains the player's separate game copy. Restricted
gameplay mods and another player's account are not included. Linux gameplay and Windows–Linux
multiplayer still require real-machine testing. A successful update is not a
multiplayer certification.

## Windows

For a new player, open Clonedivers and press **Set up Clonedivers**. No GitHub
download, terminal command or manual Locate step is required for the normal path.
The guided window checks Steam, downloads the pinned author's installer with
size/SHA256 verification, installs Forge into a separate folder and remembers it.
The default is `%USERPROFILE%\HellForge`, outside OneDrive; Change folder lets
the player choose another drive. Allow at least 50 GB plus appearance-pack space.
Interrupted downloads resume. Existing installations are reused, never silently
updated or replaced. A new installation is staged and validated before promotion.

Clonedivers then opens the official Forge game-setup window. Click **Start setup**
there and approve its Windows prompt yourself. Steam login, ownership verification
and security approval remain player actions. If the current Steam build cannot
be converted, follow the supported-build download instructions in that window.
The pinned official CLI does not expose this first game-copy/isolation wizard as
an unattended setup command; Clonedivers does not bypass it.

The guided window detects completion without a Locate or Refresh step. Return
to Clonedivers (or close the official manager for automatic return), **Sync HD2
account**, then **Prepare Star Wars pack**. Matching permitted gameplay mods are
still required; this installer does not redistribute private Heavy/Squad repairs.
Closing an unfinished setup remembers the connected installation; **Finish
setup** resumes the guided flow. Active game sessions are left running and block
installation. A failed new unpack is retained separately for diagnosis and never
replaces an existing installation or account.

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
0.6.1 release. It does not replace installed software by itself. Download the
complete `Clonedivers-linux-x64.deb` from `linux-v0.6.1`, close the launcher, then
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

Only x86-64 Forge packages are supported. Snap/Flatpak **Steam** roots are
discovered; Clonedivers and Forge still run outside those application sandboxes,
with Forge in its own Proton prefix. This is not a Snap/Flatpak package of Forge.
The pinned upstream scripts describe Snap/Flatpak Steam as untested. Discovery
fixtures do not qualify their Steam bridge, Proton gameplay or multiplayer.
ARM64/Deck and actual Linux account capture are also unqualified.

## First Forge setup

1. Download `HellForge-Setup.exe` from the author's
   [official 0.3.0-beta release](https://github.com/SkyeShade/HellForge/releases/tag/v0.3.0-beta), engine **0.1.41**, SDK **15**, game
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

### Linux setup before Connect

Follow the [pinned author's Linux setup guide](https://github.com/SkyeShade/HellForge/blob/b1aef771474710eab5461d360444cd80a809c70c/docs/linux.md).
The Clonedivers package does not contain the Windows Forge installer or game.
Keep your usual owning Steam client running and signed in. Do not install a
second Steam client or change your desktop URI handler to set up Forge.

1. Install the guide's dependencies (`socat`, `iproute2`, `util-linux`, `procps`
   on Ubuntu/Debian) and install Proton in your existing Steam client.
2. Choose the **Steam client root**, not `/mnt/extrassd/SteamLibrary`. For Snap,
   this is normally `$HOME/snap/steam/common/.local/share/Steam`. For native Steam,
   normally `$HOME/.local/share/Steam` or `$HOME/.steam/debian-installation`.
   Find your installed Proton's `proton` script in that client's libraries.
   Adjust these example paths before running:

   ```sh
   steam_root="$HOME/snap/steam/common/.local/share/Steam"
   proton="$steam_root/steamapps/common/Proton 11.0/proton"
   prefix="$HOME/.local/share/hellforge-prefix"
   mkdir -p "$prefix"
   STEAM_COMPAT_CLIENT_INSTALL_PATH="$steam_root" \
   STEAM_COMPAT_DATA_PATH="$prefix" \
   "$proton" run "$HOME/Downloads/HellForge-Setup.exe"
   ```

3. Keep the suggested install inside the prefix's C: drive, usually
   `pfx/drive_c/users/steamuser/Documents/HellForge`. Close the manager that setup
   opens outside the namespace. Continue with the author's namespace/depot steps;
   review any requested administrator action yourself. Do not disable AppArmor
   or bypass a failed isolation check. The official setup must obtain the pinned
   game build; your current Steam game is not that copy.
4. Start the installed official wrapper with the same Steam root:

   ```sh
   STEAM_COMPAT_CLIENT_INSTALL_PATH="$steam_root" \
   bash "$prefix/pfx/drive_c/users/steamuser/Documents/HellForge/app/linux/hellforge-linux.sh"
   ```

   Complete official setup, close its manager/session, then use Clonedivers
   Locate to select that `HellForge` folder. Terminal diagnostics accept:

   ```sh
   STEAM_COMPAT_CLIENT_INSTALL_PATH="$steam_root" clonedivers --forge \
     --forge-root "$prefix/pfx/drive_c/users/steamuser/Documents/HellForge" status
   ```

Clonedivers reads its saved selection from
`${XDG_CONFIG_HOME:-$HOME/.config}/clonedivers/forge.json`; it does not scan Steam
for a Forge installation or create one automatically. Steam ownership discovery
does scan native, Flatpak and Snap client roots and their `libraryfolders.vdf`.
Snap `/var/lib/snapd/hostfs/mnt/...` library entries map to host `/mnt/...` paths.
With multiple clients and no reliable HD2 owner, set the supported
`STEAM_COMPAT_CLIENT_INSTALL_PATH` explicitly rather than letting another client
be selected. This variable selects Steam; `STEAM_COMPAT_DATA_PATH` is Forge's
separate prefix and is not a Steam client root.

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
