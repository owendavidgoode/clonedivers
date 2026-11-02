# Clonedivers for Linux (preview)

A native terminal launcher for Steam/Proton players. Downloads and verifies the same
pack as the Windows launcher, switches Clonedivers/EmpireDivers/vanilla, and starts
Helldivers 2 through Steam. No separate .NET install is needed for release packages.

## Start

Download `Clonedivers-linux-x64.tar.gz` from the
[Linux preview release](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.2.0).
Most desktop PCs and Steam Deck use x64. The arm64 package is also built, but that
does not establish that Helldivers 2 can run on an ARM machine.

```bash
mkdir -p ~/Applications/clonedivers
tar -xzf ~/Downloads/Clonedivers-linux-x64.tar.gz -C ~/Applications/clonedivers
cd ~/Applications/clonedivers
chmod +x clonedivers
./clonedivers
```

Run from a terminal. The menu offers install/update, options, vanilla, repair,
recovery and launch. Choose options before installing; option changes apply on the
next install/update. The game must be closed when changing files.

Steam discovery includes native Steam, Flatpak Steam and external libraries listed
in `libraryfolders.vdf`. If discovery misses your install:

```bash
./clonedivers --game '/mnt/Games/steamapps/common/Helldivers 2'
```

Choose the folder containing `data` and `bin/helldivers2.exe`. Use the real path if
a managed folder is a symlink; the file safety checks reject redirected writes.
Launching requires a working desktop `steam://` handler (usually provided by Steam
and `xdg-open`). Flatpak Steam must already have permission to use its game library.

## Commands

```bash
./clonedivers status
./clonedivers install clone
./clonedivers install empire
./clonedivers vanilla
./clonedivers repair
./clonedivers finish
./clonedivers launch
./clonedivers report
./clonedivers record-setup
./clonedivers record
./clonedivers record-status
./clonedivers record-cancel
```

Pass `--yes` to accept download and untested-game-build prompts in scripts. Pack
status `broken` and a queued Steam update still block installation. A mismatch
between the installed game and the pack's tested build is shown before proceeding.

Downloads resume after interruption. Ctrl+C cancels downloads and hashing; the short
file-apply step finishes without cancellation. Interrupted file updates keep a
durable target: use `finish` before launching or switching. If the saved target is
missing or corrupt, `repair` uses the installed receipt; `install clone` or
`install empire` can rebuild an unrecorded selection from staged files.

`repair` freshly hashes the recorded installed selection and preserves whether mods
were active or parked. Replaced files remain in `mods_old`. Vanilla parks all active
patch files in `mods_off`, including manually installed mods, as on Windows.

Settings live in `${XDG_CONFIG_HOME:-$HOME/.config}/clonedivers`. The launcher holds a
per-game lock against a second Linux launcher while changing files. Close any other
mod managers too. `--manifest URL` uses a separate metadata cache for testing.
Recovery targets are scoped to each game folder; saved 0.1.0 recovery targets remain
finishable after upgrading. Repair preserves the recorded active/vanilla selection
even if every mod file has been deleted. Repair and finish refuse queued Steam updates.

FPV/ModOptions defaults are prepared inside
`steamapps/compatdata/553850/pfx/drive_c/users/steamuser/AppData/Local`. Existing
choices are preserved. For a custom Proton prefix, set `STEAM_COMPAT_DATA_PATH` to
the directory containing `pfx`. Launch the vanilla game once through Steam before
using FPV so Proton has created its user profile.

## Support reports

Choose **Save support report** in the menu or run:

```bash
./clonedivers report
./clonedivers report '/path/to/support.zip'
```

The ZIP contains distro/kernel and available GPU/driver identifiers, installed pack,
game build, texture profile/options and recent launcher errors. If available, it also
includes bounded excerpts of the latest recorded Proton log and MangoHud CSVs, plus
the Proton version observed in the actual Steam command. Without a captured version,
the report says unknown. The default `~/steam-553850.log` is used when no recorded
session exists. Missing hardware information or logs remain unavailable.

Reports work while the game is running and even when game detection fails. The default
destination is the launcher's config folder under `reports`. Existing output files
are never overwritten. Raw settings, telemetry credentials and environment dumps are
excluded; text redaction filters common credential lines, home paths, Steam IDs and
URL queries on a best-effort basis. Inspect the ZIP before sharing. Nothing is uploaded.

## Optional session recording

MangoHud is an optional dependency. Install its Vulkan layer in the environment used
by Steam. For native Debian/Ubuntu Steam, the upstream documented package is
`sudo apt install mangohud`; for Flatpak Steam, install the matching
`org.freedesktop.Platform.VulkanLayer.MangoHud` extension. See
[MangoHud installation](https://github.com/flightlessmango/MangoHud#installation---pre-packaged-binaries)
for your distribution and runtime. The launcher never installs packages or changes
global MangoHud settings.

1. Run `./clonedivers record-setup`, or use **Session recording → One-time Steam setup**.
2. In Steam, open **Helldivers 2 → Properties → Launch Options** and paste the printed
   wrapper line. For an empty field it looks like:

   ```text
   '/path/to/Helldivers 2/mods_diagnostics/steam-capture.sh' %command%
   ```

   If you already have custom launch options, put the printed wrapper path before
   their existing command, preserving the rest. Setup prints instructions; it does
   not edit Steam's configuration.
3. Run `./clonedivers record` to arm the next launch. Start through the launcher's
   **Launch** command or directly in Steam. Keep the launcher's terminal closed or
   open as you prefer; the wrapper handles the capture.
4. After exiting the game, run `./clonedivers record-status` to check the command exit
   code and whether a MangoHud CSV was produced. Use `./clonedivers report` to collect
   a support ZIP. Complete raw files remain under the game's
   `mods_diagnostics/sessions/<session-id>` folder.

One request records one Steam launch. Ordinary launches forward the original command
and environment unchanged. A recorded launch enables a hidden MangoHud log after one
second, records frame data for up to 10 minutes, and enables Proton logging for that
launch. It applies no FPS cap, texture or rendering overrides. Pack selection is copied
at launch from the launcher's latest metadata; manual changes outside the launcher
may not be reflected. Existing session files are retained until you remove them.

`record-cancel` cancels a pending request and keeps existing recordings. No CSV means
performance capture did not succeed; a launch request alone does not prove that the
MangoHud layer loaded. `record-status` explains this rather than claiming success.

This integration targets **desktop sessions**. Gamescope and Steam Deck Gaming Mode
require a separate `mangoapp` integration, which this preview does not configure.
Flatpak Steam needs its matching MangoHud extension and access to the selected game
library. The wrapper lives in that library so it does not need to run the host's .NET
launcher from inside Flatpak. Actual game/driver/Flatpak behavior still needs playtesting.

To remove the integration, remove the wrapper prefix from Steam's Launch Options;
keep any pre-existing options. The launcher does not need the wrapper for normal use.

## Preview limits and validation

This release has a terminal menu, not the Windows graphical interface. Windows
performance sharing, PresentMon recording and launcher self-update are unavailable.
To update the launcher, extract a newer Linux package over it while it is closed.

Automated checks cover Linux Steam discovery, process detection, downloads, SHA-256
verification, recovery, repairs and cached mode changes. The actual mod pack and
Steam/Proton gameplay still need testing on a player's Linux machine. First confirm
vanilla Helldivers 2 works with that machine's Proton setup.

For a squad playtest, check download/update, Clonedivers launch, EmpireDivers switch,
vanilla restoration, repair and a cancelled/resumed download. Report distribution,
Steam native/Flatpak, Proton version, game build and the terminal error text.

## Build

With the .NET 8 SDK:

```bash
dotnet run --project Clonedivers.Linux.Tests -c Release
dotnet publish Clonedivers.Linux -c Release -r linux-x64 --self-contained true -o dist/linux-x64
./dist/linux-x64/clonedivers
```

The Linux and Windows launchers compile the same pack, validation, download and
recovery sources. Linux CI builds both x64 and arm64 packages, verifies archive
contents, executable permissions, architecture and checksums, then runs the extracted
x64 executable through installation/launch/repair/recovery scenarios on Ubuntu 22.04
and 24.04. A `linux-v*` tag publishes a prerelease and downloads it again for hosted
package verification, without changing the Windows latest release or public pack feeds.
CI also exercises the recording wrapper and support ZIPs, and records an actual
MangoHud CSV from a Vulkan demo using software rendering on a virtual X display.
