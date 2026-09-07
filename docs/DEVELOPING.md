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
[`build-manifest.ps1`](../tools/build-manifest.ps1) writes `manifest.json` (one entry per file, SHA-256-named assets),
[`publish-pack.ps1`](../tools/publish-pack.ps1) uploads the new files to a `pack-<version>-files` release and commits the manifest,
[`publish-app.ps1`](../tools/publish-app.ps1) releases the exe and writes the `app` block, [`gh-common.ps1`](../tools/gh-common.ps1)
is their shared GitHub plumbing, [`build-pack.ps1`](../tools/build-pack.ps1) still builds a legacy zip pack,
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
- [ ] With `manifest.json` published: **Download pack** shows `2026.09.05-r3  ·  8.8 GB` under the label, downloads with one bar
      and the percentage in the window title, verifies, installs
- [ ] During an update the switch reads **CHECKING FILES…**, then **DOWNLOADING PACK…**, then **FINISHING UPDATE…**
- [ ] Tab shows a focus ring on the buttons, mouse clicks do not
- [ ] Ghost outlines (Install pack from file…, Open, Change…) and the 6-px progress bar are visible at 100%
- [ ] Renders cleanly at 100%, 125% and 150% display scaling, and dragging the window between a 150% and a 100% monitor
      rescales corners, bar and fonts
- [ ] Per-file update against a stand-in folder and a local manifest (see "Faking states"): fresh install downloads every
      non-empty file once; a renumbering-only manifest downloads nothing and renames; **Pack up to date** re-hashes and
      reports "Verified"; an option toggle parks/renumbers (off) and downloads only the group (on)
- [ ] Interrupted update: a staged file in `data\` shows **UPDATE INTERRUPTED**, Install pack from file… and the toggle
      are inert, **Finish update** completes offline
- [ ] Warnings: `status: broken`, a `gameBuild` that differs from the acf, and a pending Steam update each turn the status
      amber with the right sentence; LAUNCH asks Yes/No/Cancel; Yes switches OFF and launches
- [ ] Self-update: a test manifest with `app.version` 9.9.9 shows the offer in the progress row; against a COPY of the
      exe the swap leaves `.old.exe` behind and the new copy starts with "Updated to 9.9.9."

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

## Pack format 2 (manifest.json) and the incremental update

`manifest.json` = `{ format: 2, app: { version, url, size, sha256 }, pack: { version, name, notes, gameBuild, gameDepots,
status, statusNotes, options: [{ id, name, description, default }], files: [{ name, size, sha256, url, option? }] } }`.
`name` is the final file name in `data\`; assets are named by sha256 and a sha keeps its URL across versions; size-0
entries have no URL and are created locally; a file with an `option` id belongs to a toggleable group; a file with
`unlessOption` is skipped while that option is on (it is the base file a same-named variant entry replaces, so a whole
alternative build of the pack is one toggle). `pack.json` (format 1, zip parts) still parses and is frozen for the 1.2.0 app.

The update is `Pack.EffectiveFiles` (drop disabled option groups and renumber per archive so the set is gap-free) →
`Pack.InventoryAsync` (hash `data\`, `mods_off\`, staged leftovers, lazily `mods_old\`; only sizes that appear in the
manifest are hashed; `%APPDATA%\Clonedivers\hashes.json` caches by `name|size|mtime`, which survives an ON/OFF toggle) →
`Pack.Plan` (pure: in-place, rename by hash as a multiset, copy a local twin, create empty, download once per sha, park
the rest under `mods_old\` with `.N` on collision; stages descend by patch index, finalizes ascend) → download into
`mods_download\<sha>` → `Pack.Apply` (writes `%APPDATA%\Clonedivers\apply-plan.json` first, then parks, stages to
`<name>.clonedivers-staged`, copies/creates, finalizes; never overwrites an occupied final name; a locked file leaves the
run interrupted). `Pack.Replay` finishes an interrupted run from the plan file without network; `ModFiles.HasStagedFiles`
is the interrupted state. `Pack.Install` (zip path) refuses while staged files exist.

Self-update: `SelfUpdate.Paths` derives `<stem>.update.exe` / `<stem>.old.exe`; the offer needs `app.url` to equal the
pinned release URL (relaxed only under `Settings.ManifestUrl`); `SelfUpdate.Swap` renames the running exe to `.old.exe`,
moves the download into place with retries (OneDrive/Defender), rolls back if the second move fails; the mutex is
released before the new exe starts with `--updated X.Y.Z`; `.old.exe` is deleted on the next start.

Warnings: `SteamAcf.Read` parses `steamapps\appmanifest_553850.acf` (buildid, TargetBuildID, StateFlags; cached by
mtime). Precedence: game running > `status: broken` > Steam update pending > build changed. LAUNCH shows Yes/No/Cancel
while a signal is active and the clones are ON.

### Faking states

- Point the app at a stand-in folder (`{ "GamePath": "...\\Helldivers 2" }` in `%APPDATA%\Clonedivers\config.json`, with
  an empty `bin\helldivers2.exe`) and at a local manifest (`"ManifestUrl": "http://127.0.0.1:8000/manifest.json"`,
  served with `python -m http.server`). The footer turns amber and names the test host.
- A fake acf lives two folders above the stand-in game folder (`steamapps\appmanifest_553850.acf`, tab-separated like
  the real one); change `buildid` / `TargetBuildID` to trigger the warnings; set `"status": "broken"` in the manifest.
- Drop any `X.clonedivers-staged` file into `data\` for the interrupted state.
- Use `"app": { "version": "9.9.9", "url": "http://127.0.0.1:8000/Clonedivers.exe", ... }` in the local manifest for the
  self-update offer; test the swap on a copy of the exe in a scratch folder, never on `dist\Clonedivers.exe`.

## Next (1.4)

- Big button as the next action: **DOWNLOAD THE PACK** / **FIND HELLDIVERS 2…** when there is nothing to toggle yet.
- A version chip in the header (the version stays in the footer until then).
- Install card around the pack row.
- An ETA on the progress line; 2-4 parallel downloads for the hundreds of small files on a first install.
- Second instance brings the running window to the front instead of exiting quietly.
- Friendly crash handler.
- `tools/gc-pack-releases.ps1` (delete a `pack-*-files` release only when no committed manifest references it).
- The quiet button style as a real API instead of the SetArmed + hover + cursor recipe.
- `tools/screenshot.ps1` to automate the screenshot plan above.
