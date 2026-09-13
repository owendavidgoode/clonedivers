# Historical record — superseded

See [current status](../../STATUS.md). This file preserves the investigation as it happened; installation claims and pending actions below may be obsolete.

# Three launcher modes

The launcher presents three selectable helmet cards and a separate launch button:

| Choice | Installed state |
| --- | --- |
| Helldivers | Parks every active mod patch using the existing vanilla switch. |
| Clonedivers | Applies the clone pack with `commandos = false`. |
| Commandodivers | Applies the clone pack with `commandos = true`. |

The mode cards replace the old ON/OFF button and the separate RC toggle. Skinny and optics choices retain their saved values. Extras are unavailable while vanilla is selected, so changing an extra cannot silently enable the mods.

Both modded choices use the existing verified inventory, download plan, confirmation and recoverable apply flow. This also applies when the pack is parked: choosing clonedivers cannot accidentally restore a parked RC pack. Mode changes do not update saved options until application succeeds; interrupted updates continue through **Finish update**. The cards are unavailable while the game runs or an operation/recovery is pending.

The three helmet marks are original vector drawings in `Clonedivers/LauncherModes.cs`. They use native drawing and remain sharp at different DPI scales. Each card supports keyboard focus and exposes its selected state to accessibility tools.

## Validation and builds

Run the native suite with the portable SDK:

```powershell
& ./dist/dotnet-sdk/dotnet.exe run --project Clonedivers.Tests/Clonedivers.Tests.csproj
```

The mode tests cover both parked RC → clones and parked clones → RC, exact resulting files, reusing local bytes, vanilla parking, preserved extras and interrupted settings updates. Existing file safety, downloads and recovery tests remain in the same suite.

The separate preview harness exercises the actual launcher controls with mode changes simulated in memory. It does not install anything, launch the game, save settings or load a manifest from the network:

```powershell
& ./dist/dotnet-sdk/dotnet.exe run --project tools/Launcher.Preview/Launcher.Preview.csproj
& ./dist/dotnet-sdk/dotnet.exe run --project tools/Launcher.Preview/Launcher.Preview.csproj -- --render dist/rc-upgrade/launcher-preview.png
```

The render uses the same controls without displaying a window. Its disabled lower controls distinguish it from the operational launcher. A reviewed render is staged at `dist/rc-upgrade/launcher-preview.png`.

The operational executable is staged separately at `dist/launcher-three-modes/Clonedivers.exe`. Building it does not replace the installed `dist/Clonedivers.exe`:

```powershell
& ./dist/dotnet-sdk/dotnet.exe publish Clonedivers/Clonedivers.csproj -c Release -p:PublishDir="C:/Users/goode/OneDrive/Desktop/clonedivers/dist/launcher-three-modes/"
```

This source change does not publish a new release, change the game pack or replace the public manifest. Deployment and an actual game launch remain separate checks.

## Local deployment — 2026-09-12

The three-mode executable is now installed at `dist/Clonedivers.exe`, SHA256
`08D2F04DF7C9DD2C278842DDF189A74DF68FA58A7BAA6D00C4C5F01DBF82DFDA`.
The display name was subsequently corrected from lowercase `clonedivers` to **Clonedivers**;
this label-only build was installed and its running UI checked.
CommandoDivers help text now reads **Delta Squad is elite.** The user requested keeping the
film and armor reveal out of help text; local manifest notes and option descriptions were
also updated so switching modes does not expose those details in its confirmation.
The user subsequently requested **Commandodivers**, with a lowercase internal d; the displayed
name and unsupported-pack tooltip were updated accordingly. Internal enum/option identifiers
remain unchanged.
The prior executable is preserved at
`dist/rc-upgrade/launcher-three-modes-backup/Clonedivers.before.exe`.

The native suite passed303 checks. The operational UI was visually checked, and a live mode
round trip passed: Helldivers parked all788 files and left zero active patches; clonedivers
restored754 expected files; CommandoDivers restored788 expected files. Every file in each
modded mode matched the expected name, size and SHA256. Options remained optics ON and
skinny OFF. Reports are in `dist/rc-upgrade/mode-audit/`.

The user authorized FOV75 in the native user settings. Exactly one byte changed from55 to75,
with original settings backed up under `dist/rc-upgrade/fov75-test/`. This is a global user
camera setting, independent of the three mod modes; no executable or anti-cheat files changed.
The actual AT-TE framing still needs in-game checking.

The user confirmed75 is better and requested a wider view. Native FOV is now **85**, changed
while the game was closed with exactly one byte edited. The75-degree settings backup and
receipt are in `dist/rc-upgrade/fov85-test/`;85-degree gameplay feedback is still pending.
