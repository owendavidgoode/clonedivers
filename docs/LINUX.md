# Clonedivers for Linux (preview)

A native terminal launcher for Steam/Proton players. Downloads and verifies the same
pack as the Windows launcher, switches Clonedivers/EmpireDivers/vanilla, and starts
Helldivers 2 through Steam. No separate .NET install is needed for release packages.

## Start

Download `Clonedivers-linux-x64.tar.gz` from the
[Linux preview release](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.1.0).
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

FPV/ModOptions defaults are prepared inside
`steamapps/compatdata/553850/pfx/drive_c/users/steamuser/AppData/Local`. Existing
choices are preserved. For a custom Proton prefix, set `STEAM_COMPAT_DATA_PATH` to
the directory containing `pfx`. Launch the vanilla game once through Steam before
using FPV so Proton has created its user profile.

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
recovery sources. Linux CI builds both x64 and arm64 packages and executes the x64
binary. A `linux-v*` tag publishes a prerelease without changing the Windows latest
release or the public pack feeds.
