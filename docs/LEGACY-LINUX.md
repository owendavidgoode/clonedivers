# Clonedivers for Linux (preview)

A native desktop and terminal launcher for Steam/Proton players. Downloads and verifies the same
pack as the Windows launcher, switches Clonedivers/EmpireDivers/vanilla, and starts
Helldivers 2 through Steam. No separate .NET install is needed for release packages.

## Start

**Ubuntu / Debian:** download
[Clonedivers-linux-x64.deb](https://github.com/owendavidgoode/clonedivers/releases/download/linux-v0.5.0/Clonedivers-linux-x64.deb),
open it with your software installer, and install. Start **Clonedivers** from the
Applications menu. It opens a desktop window; no terminal is needed for normal use.
If your desktop does not offer a local package installer, install once with
`sudo apt install ~/Downloads/Clonedivers-linux-x64.deb`.

**Other distributions / portable install:** download `Clonedivers-linux-x64.tar.gz`
from the [Linux release](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.5.0)
and extract it with your file manager. Open the `clonedivers` executable. In the
window, **Add app shortcut** adds it to your Applications menu; keep the executable
in its current folder afterward. If your file manager requires it, enable execution
in the file's Properties/Permissions. A terminal alternative is:

```bash
mkdir -p ~/Applications/clonedivers
cd ~/Applications/clonedivers
tar -xzf ~/Downloads/Clonedivers-linux-x64.tar.gz
chmod +x clonedivers
./clonedivers
```

The window offers Helldivers/Clonedivers/EmpireDivers cards, pack options,
install/update with progress and cancellation, repair, recovery and a Steam Launch
button. Steam native/Flatpak/Snap libraries are detected automatically. Use **Browse**
to choose the game folder, or paste a path and click **Use folder**. Choose a universe
and options, then **Install / update pack** or **Launch**. Downloads show their size
before proceeding. The game must be closed when changing files.

Most PCs and Steam Deck use x64. Use Steam Deck Desktop Mode for this preview.
ARM64 packages are also built; that does not establish ARM game compatibility.
The tar build needs the desktop's X11/XWayland and fontconfig libraries. The Ubuntu
package declares its runtime dependencies. Both include their own .NET runtime.

Run each complete line separately; if terminal paste markup such as `^[[200~` appears,
press Ctrl+C and enter the command again. The extraction command runs inside the
destination folder so there is no separate `tar -C` argument to miss.

Steam discovery includes native Steam, Flatpak Steam, Ubuntu Snap Steam and external libraries listed
in `libraryfolders.vdf`. If discovery misses your install:

```bash
./clonedivers --game '/mnt/Games/steamapps/common/Helldivers 2'
```

Choose the folder containing `data` and `bin/helldivers2.exe`. Use the real path if
a managed folder is a symlink; the file safety checks reject redirected writes.
Launch uses the Steam installation that owns the selected game library. Native Steam
runs its existing `steam.sh`; Snap and Flatpak use their respective app launchers.
This includes external libraries registered in that client's `libraryfolders.vdf`.
The launcher bypasses the default `steam://` handler so it cannot select a different
Steam client just because that client owns the desktop association. No Steam package
is installed and no global association is changed. Steam can still perform its own
normal client updates when opened.

If the folder is unregistered or more than one Steam installation lists it, Launch
explains the ambiguity. Open your usual Steam and start Helldivers 2 there; installed
mods still apply. `status` and support reports include Steam routing information.
Flatpak Steam must already have permission to use its game library.
Snap Steam is checked under `~/snap/steam/common/.local/share/Steam` and
`~/snap/steam/common/.steam/steam`, including their Steam library lists. You can also
use Steam's **Manage → Browse local files** to find the game's exact install folder.

## Terminal dashboard

Both interfaces are included in the same package. Opening **Clonedivers** from
Applications, or running `clonedivers` on a graphical desktop, opens the window.
Use `clonedivers --tui` for the terminal dashboard (`./clonedivers --tui` with the
portable build). `--terminal` is an alias. Without a graphical display, an interactive
session opens the terminal dashboard automatically.

- **↑/↓** moves, **Enter** selects, and a number or letter jumps to an action.
- **Space** or **Enter** toggles an option. Choose **Save options** to keep edits;
  **Esc** or **Back** discards them. Saved options apply on the next install/update.
- **Esc** goes back or quits from the dashboard. **Home/End** jumps to the first/last action.
- **Change game folder** works even when discovery fails. Paste the folder from
  Steam's **Manage → Browse local files**; quoted paths, `~` and `$HOME` are accepted.
- The dashboard shows the installed universe, game state, pending recovery and next
  options. Downloads show a progress bar, transferred bytes, speed and estimated time.
- **Ctrl+C** cancels preparation/downloads and returns to the dashboard. Partial
  downloads remain for resuming. The brief file-finalization step completes safely.
- Support and recording results wrap long paths and provide **Next/Previous page**
  when needed. Reports remain local ZIPs.

Set `NO_COLOR=1` to disable colors. `--plain`, `TERM=dumb`, or redirected output uses
basic numbered menus. Direct commands below keep their ordinary text output.

## Launcher updates

The desktop window and terminal dashboard check for newer **Linux launcher** releases
in the background. A newer version shows an update notice; **View release** opens its
GitHub download page. In the terminal, choose **Launcher updates** (`u`) to view the
version and link, open the release page, or check again. The desktop also has a
**Check for updates** button. These notices are separate from mod-pack updates.

Install the newer `.deb`, or close the portable launcher and replace its executable.
Installed pack files, cached downloads and settings are preserved. The launcher does
not download or install its own replacement automatically. Version 0.5.0 must be
installed once to receive future update notices.

Checks include Linux preview releases and ignore Windows releases. They only offer
releases with an uploaded package for the launcher's architecture. Successful checks
are cached for six hours; failures retry after fifteen minutes. Manual checks bypass
the cache. Offline operation keeps a previously known update, and update-check failure
does not block playing, pack installs or launcher startup. The check requests public
GitHub release metadata without credentials; it does not upload game or system data.

## Commands

Optional terminal commands remain available for troubleshooting and automation.
Running `./clonedivers` with no command opens the GUI on a desktop; `--tui` opens the
keyboard dashboard.

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

Click **Save report** in the window to choose a ZIP destination, or run:

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
The Snap Steam home under `~/snap/steam/common` is also checked for its default Proton log.

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

Snap Steam bundles MangoHud, but [Canonical documents its support as experimental,
with Vulkan limitations](https://ubuntu.com/docs/steam/howto/configure-mangohud/).
Installing a host MangoHud package does not make it available inside the Snap. The
launcher warns when Snap Steam is detected; use `record-status` to verify a CSV rather
than assuming performance capture worked. Support reports and available Proton logs
remain useful even when MangoHud cannot capture frames.

1. Click **Recording…** in the window and **Copy setup**, or run `./clonedivers record-setup`.
2. In Steam, open **Helldivers 2 → Properties → Launch Options** and paste the printed
   wrapper line. For an empty field it looks like:

   ```text
   '/path/to/Helldivers 2/mods_diagnostics/steam-capture.sh' %command%
   ```

   If you already have custom launch options, put the printed wrapper path before
   their existing command, preserving the rest. Setup prints instructions; it does
   not edit Steam's configuration.
3. Click **Record next session**, or run `./clonedivers record`. Start through the launcher's
   **Launch** command or directly in Steam. Keep the launcher's terminal closed or
   open as you prefer; the wrapper handles the capture.
4. After exiting the game, click **Show result** in the recording dialog, or run `./clonedivers record-status`, to check the command exit
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

This release has its own Linux desktop interface. Windows performance sharing,
PresentMon recording and launcher self-update are unavailable. Update by installing
a newer `.deb`, or replace the portable executable while it is closed.

Automated checks cover Linux Steam discovery, process detection, downloads, SHA-256
verification, recovery, repairs and cached mode changes. Successful gameplay has also
been reported by a player on Ubuntu 26.04. Other mode, GPU and Proton combinations
remain subject to playtesting. First confirm vanilla Helldivers 2 works with that
machine's Proton setup.

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
x64 executable through installation/launch/repair/recovery scenarios on Ubuntu 22.04,
24.04 and 26.04. A `linux-v*` tag publishes a prerelease and downloads it again on
Ubuntu 24.04 and 26.04 for hosted
package verification, without changing the Windows latest release or public pack feeds.
CI also exercises the recording wrapper and support ZIPs, and records an actual
MangoHud CSV from a Vulkan demo using software rendering on a virtual X display.
Native GUI checks click the real controls to install/switch/repair/launch, preserve
vanilla and base files, restore missing mods, create an Applications shortcut, and
render a Linux screenshot. Package QA installs the `.deb` and repeats those checks.
Real pseudo-terminal checks cover arrow keys, checkbox save/discard, folder changes,
headless routing, plain menus, NO_COLOR, Ctrl+C during an actual HTTP download and
resuming its retained bytes. The workflow saves a terminal transcript for inspection.
Launch checks cover native, Snap and Flatpak libraries with a deliberately wrong
desktop URI handler, plus symlink aliases and ambiguous/unregistered ownership.
Update checks cover Linux/Windows channel separation, numeric version ordering,
architecture filtering, caching, offline failures, and actual desktop/terminal
notices. The desktop's release button is exercised through a local browser stub.
