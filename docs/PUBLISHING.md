# Publishing a pack

Owen's notes. Friends never touch any of this; the README's Quick start is all they need.

The whole round trip, once per pack version:

1. `powershell -ExecutionPolicy Bypass -File tools\deploy-mods.ps1 -DryRun` and read the plan.
2. `powershell -ExecutionPolicy Bypass -File tools\deploy-mods.ps1`
3. `powershell -ExecutionPolicy Bypass -File tools\conflict-report.ps1` and check who overrides whom.
4. Launch once and check a bot mission.
5. `powershell -ExecutionPolicy Bypass -File tools\publish-pack.ps1 -Version <date>-rN -Notes "Built for Helldivers 2 patch <current game patch>" -Detach`
   and watch `dist\publish-pack.state.json` (or `dist\publish-pack.log`) until `phase` is `done`.
6. Open Clonedivers on a second PC and confirm it shows **Update pack** with a small download.

## How the pack is published (Clonedivers 1.3 and later)

`manifest.json` at the repo root lists every deployed `*.patch_*` file by its final name, size and SHA-256, plus the
newest app. Friends' Clonedivers reads it (through the GitHub API, so a push shows up immediately), hashes what they
already have, renames files whose bytes match, and downloads only the rest. Each file is a GitHub Release asset named by
its SHA-256 on a release tagged `pack-<version>-files`; a later pack reuses the URL of every file whose contents did
not change, so a new version only uploads what is new.

Three consequences:

- **Never delete a `pack-*-files` release by hand.** Newer manifests point into older releases. A release may only go
  once no entry in the committed `manifest.json` references any of its assets (a future `gc-pack-releases.ps1`; not
  needed for years of updates at this rate).
- **`pack.json` is frozen.** It is the zip pack the 1.2.0 app reads; it stays at r3 forever so old copies keep saying
  "Pack up to date". `build-pack.ps1` refuses to overwrite it. `publish-pack.ps1` refuses to run if it has changed.
- **Pack releases are never "latest".** The README's download button goes through `releases/latest`; only `vX.Y.Z`
  app releases may be Latest. Both scripts assert this.

### Preparing the mods

1. Download the mod zips from Nexus (the links in the README's pack table; Arsenal's one-click download or the plain
   "Manual download" both work, the zips just need to end up in `%LOCALAPPDATA%\hd2arsenal\temp`, your Downloads folder,
   or `dist\mods`). Then let the repo do the mod manager's job:

   ```bash
   powershell -ExecutionPolicy Bypass -File tools\deploy-mods.ps1 -DryRun
   powershell -ExecutionPolicy Bypass -File tools\deploy-mods.ps1
   ```

   [`pack-recipe.json`](../pack-recipe.json) is the whole configuration: which zip, which option in every option group
   (Republic, Phase 2, 501st…), which toggles, and the load order (Galactic Map last). The script reads each zip's
   `manifest.json`, picks the folders the recipe asks for, numbers every patch set consecutively per archive in
   recipe order (preserving each mod's own base-then-textures order), and writes them into `data\`. The dry run lists
   the plan and flags any zip that is missing or still downloading. Existing mod files are moved to `mods_old\`, never
   deleted. Clonedivers should then say **CLONES: ON**; launch once and check a bot mission before publishing.

   **Optional groups.** A recipe entry with an `"option": { "id", "name", "description", "default" }` block becomes a
   toggle in friends' Clonedivers (the Republic Commando squad will be one). Keep such entries at the end of the recipe
   so their patch numbers come last; the app renumbers whatever is enabled, so a group switched off never leaves a gap.

   **Variants.** A top-level `"variants": [ { "id", "name", "description", "default", "dir" } ]` entry names a folder that
   holds the whole pack built differently; `build-manifest.ps1` hashes it, shares files identical to `data\`, and lists
   each file that differs as a pair (the base entry gets `unlessOption`, the variant entry `option`), so friends see one
   toggle and download only the files that differ. The first is `skinny` (Lighter textures): run
   `tools\optimize-pack.ps1` after `deploy-mods.ps1`, which writes `dist\pack-optimized` with every texture's mip chain
   streamed (lossless), then publish as usual. Any new pack version needs both folders rebuilt.

2. Check who overrides whom:

   ```bash
   powershell -ExecutionPolicy Bypass -File tools\conflict-report.ps1
   ```

   Every mod patches the same game archive, so a "conflict" is two mods shipping the same asset (name + type); the one
   with the higher patch number wins silently. This reads the index table at the top of every deployed patch file and
   writes `dist\conflict-report.md`: each cross-mod override with the winner, grouped by mod pair. Most are intended
   layering (Armory's accessories over its armor, Custom Projectiles over Blue Overhaul). It is how the Ranks mod was
   found to be dead weight (Galactic Map overrides both of its string tables) and the RC beeps bank was moved before
   (lower priority than) RiqCrow's ship audio so RiqCrow wins the 4 shared assets. Each published pack's deploy report,
   conflict report and recipe are archived under [`docs/pack/`](pack/README.md).

   **Arsenal deletes its download folder.** HD2 Arsenal clears `%LOCALAPPDATA%\hd2arsenal\temp` on its own schedule, and
   that is where its one-click downloads land. Copy zips you care about into `dist\mods\` (gitignored) right after
   downloading. If the originals are already gone, `tools\recover-sources.ps1` rebuilds one zip per mod from the deployed
   `data\` folder plus the deploy report; `deploy-mods.ps1` falls back to those automatically. Recovered zips carry only
   the options that were deployed, so changing an option later means re-downloading that one mod.

### Publishing

```bash
powershell -ExecutionPolicy Bypass -File tools\publish-pack.ps1 -Version 2026.09.20-r4 -Notes "Built for Helldivers 2 patch 7.1" -Detach
```

What it does, in order: `build-manifest.ps1` hashes `data\` (about 20 seconds on an SSD), refuses if Steam has a game
update queued (the recorded `gameBuild` would be wrong; `-AllowPendingGameUpdate` overrides), writes `manifest.json`
reusing URLs from the committed manifest, and lists what still needs uploading in `dist\upload-plan.json`. Then it
creates a draft release `pack-<version>-files`, uploads the new files in batches of 40 per minute through hard links
under `%LOCALAPPDATA%\Clonedivers\publish\` (no extra disk), verifies every asset by name and size, publishes the draft
with `--latest=false`, checks that the README download link still resolves to an app release, commits `manifest.json`
and pushes.

- `-Detach` runs it in a hidden background PowerShell and returns at once. Progress: `dist\publish-pack.state.json`,
  full log `dist\publish-pack.log`. A run can take an hour or more for a big change; uploads are bandwidth-bound.
- Re-running is safe and resumes: assets already on the release are skipped (the asset name is the content hash), a
  published release goes straight to the commit. `dist\publish-pack.lock` holds the PID of a live run; the script
  refuses to start a second one.
- A first-time publish of an existing zip pack can prove nothing changed for friends:
  `-VerifyZips "dist\pack\clonedivers-pack-2026.09.05-r3-part*.zip"` requires `data\` to equal the zips file for file.
- Zero-byte companion files (182 of 604 in r3) are listed with an empty URL; the app creates them locally. GitHub
  rejects empty assets.
- Limits: 2 GiB per asset (largest file today 1.2 GB), 1000 assets per release (the script chunks at 900 into
  `pack-<version>-files-2`, …).

### Marking a pack broken without publishing anything

When a Helldivers 2 patch breaks the pack, edit `manifest.json` on GitHub (web editor is fine): set
`"status": "broken"` and a `"statusNotes"` sentence. Every friend's Clonedivers shows it in amber within a minute of
opening and asks before launching whether to switch the clones OFF. `"gameBuild"` can be corrected the same way when a
game hotfix did not touch the archives and the warning is a false alarm. The next `publish-pack.ps1` run resets both
(`-Status broken -StatusNotes "..."` keeps them).

### Hosting elsewhere

The app accepts any **direct** download URL in `manifest.json`. To host the files yourself, run `build-manifest.ps1`,
upload each `dist\upload-plan.json` entry under its SHA-256 name, and replace the URLs. Cloudflare R2 (10 GB free, no
egress charges) fits one pack; the dashboard uploader stops at 300 MB, so use `rclone`. Cloudflare Pages does not work
(25 MB per file). Google Drive / OneDrive share links do not work in-app (they open a web page) but are fine for a zip
pack friends install with **Install pack from file…**. `tools\build-pack.ps1 -ManifestPath dist\pack\pack.json` still
builds such a zip pack.

## Releasing the app

The app and the pack are separate releases on the same repo. App releases are tagged `vX.Y.Z` and are the ones marked
Latest; pack releases are `pack-<version>-files` and never Latest.

1. Set `<Version>X.Y.Z</Version>` in `Clonedivers\Clonedivers.csproj`. The footer and the User-Agent read it from the
   assembly, so this is the only place the version lives.
2. Run the preflight checklist in [DEVELOPING.md](DEVELOPING.md); recapture screenshots if the window changed.
3. Write `docs\releases\vX.Y.Z.md` (friends first: how to update, what changed, which pack the build offers).
4. Commit everything (the tree must be clean apart from `manifest.json`), then:

   ```bash
   powershell -ExecutionPolicy Bypass -File tools\publish-app.ps1
   ```

   It runs the tests, publishes `dist\Clonedivers.exe`, checks its FileVersion, tags `vX.Y.Z`, pushes, creates the
   GitHub Release (Latest, asset named exactly `Clonedivers.exe`, notes from `docs\releases\vX.Y.Z.md`), writes the
   `app` block (version, pinned URL, size, SHA-256) into `manifest.json`, commits, pushes, and checks the README
   redirect. Running copies of Clonedivers 1.3+ then show "Clonedivers X.Y.Z is available · click to update".
5. Publish the pack first when both change: a new exe whose manifest has no pack block says "Pack not published yet".
6. If `tools\make-banner.ps1` was re-run, upload the new `docs\social-preview.png` by hand in the repo's Settings →
   General → Social preview (there is no API for it).

The self-updater only accepts `app.url` equal to `https://github.com/owendavidgoode/clonedivers/releases/download/vX.Y.Z/Clonedivers.exe`
for the manifest's own version, verifies the SHA-256, and swaps the exe while the app is still running (exe → `.old.exe`,
`.update.exe` → exe, restart). The trust anchor is this GitHub account plus the PC that runs the publish scripts; keep 2FA on.

## "That is a mod's options package, not a deployed pack."

The zip has the same file name under several folders, which is how Nexus mods with options are shipped. A zip pack must
be built from a deployed `data\` folder (see above).
