# Developing Clonedivers

## Build it yourself

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). From the repo root:

```bash
dotnet publish -c Release Clonedivers
```

That drops a single self-contained `dist\Clonedivers.exe` (~63 MB, no runtime install needed on the target PC).

Native tests (plain console app, no test framework, exit code 0 = pass):

```bash
dotnet run --project Clonedivers.Tests
```

They prove the matcher moves exactly the patch files and their companions, leaves base archives and a decoy named
`game.patch` alone, overwrites a stale (even read-only) collision, round-trips cleanly, keeps going past a locked file and
names it, unescapes `libraryfolders.vdf` paths, extracts only patch files from a pack zip (flat, rejecting options packages),
parks stale files on install, and downloads with resume, rejecting hash or size mismatches and web pages served as files.

The test project compiles `Clonedivers\Program.cs` directly (`net8.0-windows`, `UseWindowsForms`), so every new class,
`using` and P/Invoke in Program.cs has to build there too.

The version lives in one place: `<Version>` in `Clonedivers\Clonedivers.csproj`. The footer and the User-Agent read it
from the assembly at run time, so the footer always matches the tag when the csproj does.

## Layout

[`Clonedivers/Program.cs`](../Clonedivers/Program.cs) is the whole app (C#, .NET 8 WinForms, one file).
[`Clonedivers.Tests/`](../Clonedivers.Tests/) is the test console app. Under `tools/`:
[`deploy-mods.ps1`](../tools/deploy-mods.ps1) assembles `data\` from the mod zips per `pack-recipe.json`,
[`conflict-report.ps1`](../tools/conflict-report.ps1) reports who overrides whom,
[`recover-sources.ps1`](../tools/recover-sources.ps1) rebuilds mod zips from a deployed `data\`,
[`build-pack.ps1`](../tools/build-pack.ps1) builds the pack zips and `pack.json`,
[`publish-pack.ps1`](../tools/publish-pack.ps1) (and `publish-resume.ps1`) upload them to a `pack-<date>` release,
[`helmet-draw.ps1`](../tools/helmet-draw.ps1) draws the helmet that
[`make-icon.ps1`](../tools/make-icon.ps1) turns into `clonedivers.ico` + `docs/icon.png` and
[`make-banner.ps1`](../tools/make-banner.ps1) turns into `docs/banner.png` + `docs/social-preview.png`.
Under `docs/`: [`MODS.md`](MODS.md) is the mod audit, [`PUBLISHING.md`](PUBLISHING.md) is the pack and release flow,
[`pack/`](pack/README.md) archives each published pack's recipe and reports, `releases/` holds the release notes,
`screenshots/` the README pictures.

## Preflight checklist

Run before shipping a new build, on a PC with the game installed. Use a dummy `test.patch_0` in `data\`
(no real mods needed) and delete it afterwards.

- [ ] `dotnet run --project Clonedivers.Tests` passes (it compiles Program.cs)
- [ ] Footer version matches the release tag; `(Get-Item dist\Clonedivers.exe).VersionInfo` shows the same with no `+sha`
- [ ] App launches, finds the game with no prompt, shows **CLONES: ON** and **Found via Steam:** at the bottom
- [ ] Title bar is dark and navy on Windows 11, plain dark on Windows 10
- [ ] Toggle → file lands in `mods_off\`, `data\` has no `*.patch_*`, shows **CLONES: OFF**; toggle back
- [ ] **Launch** hands off to Steam
- [ ] With the game running, toggle and install are refused with a clear message; LAUNCH is dim and clicking it explains
- [ ] With the game folder temporarily renamed, app shows **GAME NOT FOUND** and the picker recovers it (restore the folder)
- [ ] With no mod files and a published pack the status reads "No mod pack installed."
- [ ] **Install pack from file…** with a small test zip: files land flat in `data\`, non-pack files go to `mods_old\`, ends ON
- [ ] With `pack.json` published: **Download pack** shows `2026.09.05-r3  ·  8.8 GB` under the label, downloads with one bar
      and the percentage in the window title, verifies, installs
- [ ] During install the switch reads **INSTALLING PACK…**
- [ ] Tab shows a focus ring on the buttons, mouse clicks do not
- [ ] Ghost outlines (Install pack from file…, Open, Change…) and the 6-px progress bar are visible at 100%
- [ ] Renders cleanly at 100%, 125% and 150% display scaling, and dragging the window between a 150% and a 100% monitor
      rescales corners, bar and fonts

## Screenshots

All on the same 150% display, window never resized between shots, Win+Shift+S in Window mode (keeps the rounded DWM
frame). Files keep their numbers so README links never change. Order matters because of `%APPDATA%\Clonedivers\config.json`:

1. `6-game-not-found.png`: Steam closed, rename the real `Helldivers 2` folder briefly, launch, cancel the auto picker,
   shoot, restore.
2. `3-no-mod-files.png`: create a fake game folder (`C:\Temp\Helldivers 2\data\` plus an empty `bin\helldivers2.exe`),
   pick it via Change…; status reads "No mod pack installed." with the bright `Download pack · 2026.09.05-r3 · 8.8 GB`; shoot.
3. Delete `config.json` (so the caption reads **Found via Steam:**) and, if the real install was deployed by hand, set
   `"InstalledPackVersion": "2026.09.05-r3"` in a fresh config so the button reads **Pack up to date**.
   `1-clones-on.png`; click the toggle → `2-clones-off.png`; click back.
4. `4-game-running.png`: copy `powershell.exe` to a temp folder as `helldivers2.exe`, run it, minimise it; shoot the
   amber status + dim `HELLDIVERS 2 IS RUNNING`; close it.
5. `5-downloading.png`: click the pack button, confirm, shoot at ~30 s (`Downloading pack · … · 40.0 MB/s · 13%`, title
   `Clonedivers · 13%`, switch `INSTALLING PACK…`, `Cancel`), then Cancel and delete `Helldivers 2\mods_download\`.

Verify every shot shows the dark caption and the current version in the footer before committing. `7-folder-picker.png`
was removed in 1.2.0 (it showed the owner's whole Steam library) and is not coming back.

## Next (1.3)

Deferred from 1.2.0, in rough order:

- Big button as the next action: **DOWNLOAD THE PACK** / **FIND HELLDIVERS 2…** when there is nothing to toggle yet.
- A version chip in the header (the version stays in the footer until then).
- Install card around the pack row.
- An ETA on the progress line.
- Second instance brings the running window to the front instead of exiting quietly.
- Friendly crash handler.
- "New Clonedivers available" nudge from the app release.
- `tools/publish-app.ps1` for the release steps in PUBLISHING.md.
- The quiet button style as a real API instead of the SetArmed + hover + cursor recipe.
- `tools/screenshot.ps1` to automate the screenshot plan above.
