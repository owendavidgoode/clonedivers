# Publishing a pack

Owen's notes. Friends never touch any of this; the README's Quick start is all they need.

The whole round trip, once per pack version:

1. `powershell -ExecutionPolicy Bypass -File tools\deploy-mods.ps1 -DryRun` and read the plan.
2. `powershell -ExecutionPolicy Bypass -File tools\deploy-mods.ps1`
3. `powershell -ExecutionPolicy Bypass -File tools\conflict-report.ps1` and check who overrides whom.
4. Launch once and check a bot mission.
5. `powershell -ExecutionPolicy Bypass -File tools\publish-pack.ps1 -Notes "Built for Helldivers 2 patch <current game patch>"`
6. Open Clonedivers on a second PC and confirm it shows **Update pack**.

## Publishing the pack

Friends never touch Nexus or a mod manager. You do it once per pack version:

1. Download the mod zips from Nexus (the links in the README's pack table; Arsenal's one-click download or the plain "Manual download" both
   work, the zips just need to end up in `%LOCALAPPDATA%\hd2arsenal\temp`, your Downloads folder, or `dist\mods`).
   Then let the repo do the mod manager's job:

   ```bash
   powershell -ExecutionPolicy Bypass -File tools\deploy-mods.ps1 -DryRun
   powershell -ExecutionPolicy Bypass -File tools\deploy-mods.ps1
   ```

   [`pack-recipe.json`](../pack-recipe.json) is the whole configuration: which zip, which option in every option group
   (Republic, Phase 2, 501st…), which toggles, and the load order (Galactic Map last). The script reads each zip's
   `manifest.json`, picks the folders the recipe asks for, numbers every patch set consecutively per archive in
   recipe order (preserving each mod's own base-then-textures order), and writes them into `data\`. The dry run lists
   the plan and flags any zip that is missing or still downloading. Existing mod files are moved to `mods_old\`, never deleted.
   Clonedivers should then say **CLONES: ON**; launch once and check a bot mission before publishing.

   Then check who overrides whom:

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
2. Run one command from the repo folder:

   ```bash
   powershell -ExecutionPolicy Bypass -File tools\publish-pack.ps1 -Notes "Built for Helldivers 2 patch 7.0.2"
   ```

   It zips `data\`'s patch files (stored, not compressed, so it runs at disk speed), splits into parts under GitHub's
   2 GB asset limit, uploads them to a release named `pack-<date>` on this repo, writes the URLs, sizes and SHA-256
   hashes into `pack.json`, commits, and pushes. It warns if any archive has a gap in its patch numbers, the classic
   "half my mods vanished" mistake. Friends' Clonedivers reads `pack.json` on startup (through the GitHub API, so a fresh push
   shows up immediately) and offers **Download pack**, or **Update pack** when the version changed. r3 shipped 2026-09-05
   this way.

   It finds the GitHub CLI on PATH or in `%LOCALAPPDATA%\Programs\gh-cli`, and uses the login Git already has for github.com.

## Hosting elsewhere

The app accepts any **direct** download URL in `pack.json`, so if you'd rather not keep the pack on the public
repo, run `tools\build-pack.ps1 -Url https://…` instead and host the zip yourself.

- **Cloudflare R2**: free tier is 10 GB with no egress charges, so the whole squad downloading 9.5 GB costs nothing.
  R2's 10 GB free tier now only just fits one pack. Make the bucket public with an unguessable object name, or put a
  Worker in front that checks a secret in the URL. Note the dashboard uploader stops at 300 MB; use `rclone` or another
  S3 client for a 9.5 GB file.
- **Cloudflare Pages does not work**: 25 MB per-file limit, and a private (Access-protected) site serves a login page the
  downloader can't get past.
- **Google Drive / OneDrive share links do not work in-app** (they open a web page). They're fine for friends to download in a
  browser and then use **Install pack from file…**.

## Updating

Deploy the new set, run `publish-pack.ps1` again. Clonedivers compares the version and shows **Update pack**. Files the
new pack no longer contains are parked in `mods_old\`; files with the same name are overwritten.

## "That is a mod's options package, not a deployed pack."

The zip has the same file name under several folders, which is how Nexus mods with options are shipped. The pack must
be built from a deployed `data\` folder (see above).

## Releasing the app

The app and the pack are separate releases on the same repo. App releases are tagged `vX.Y.Z` and are the ones marked
Latest; pack releases are tagged `pack-<date>` and are created by `publish-pack.ps1` with `--latest=false`. That flag
must stay: the README's download link goes through `releases/latest`, and a pack release marked Latest would hand
friends a zip part instead of the exe.

1. Set `<Version>X.Y.Z</Version>` in `Clonedivers\Clonedivers.csproj`. The footer and the User-Agent read it from the
   assembly, so this is the only place the version lives.
2. Build: `dotnet publish -c Release Clonedivers` writes `dist\Clonedivers.exe`.
3. Check the version: `(Get-Item dist\Clonedivers.exe).VersionInfo | Select FileVersion, ProductVersion` must show
   `X.Y.Z.0` / `X.Y.Z` with no `+sha`, and the running app's footer must read `vX.Y.Z`.
4. Run the preflight checklist in [DEVELOPING.md](DEVELOPING.md) and capture the screenshots per the plan there.
5. Write `docs\releases\vX.Y.Z.md` (friends first: how to update, what changed, which pack the build offers).
6. Commit from a clean tree, tag `vX.Y.Z`, push the branch and the tag.
7. Publish:

   ```bash
   gh release create vX.Y.Z dist\Clonedivers.exe --title "Clonedivers X.Y.Z" --notes-file docs\releases\vX.Y.Z.md
   ```

   Default `--latest`, never `--prerelease`, and the asset name stays exactly `Clonedivers.exe`: the README's download
   href points at that name. `gh` lives in `%LOCALAPPDATA%\Programs\gh-cli` if it is not on PATH; `GH_TOKEN` can be
   taken from `git credential fill` rather than a separate `gh auth login`.
8. Check the redirect:

   ```bash
   curl -sIL -o NUL -w "%{url_effective}" https://github.com/owendavidgoode/clonedivers/releases/latest/download/Clonedivers.exe
   ```

   It must end in `/releases/download/vX.Y.Z/Clonedivers.exe`.
9. If `tools\make-banner.ps1` was re-run, upload the new `docs\social-preview.png` by hand in the repo's Settings → General →
   Social preview (there is no API for it), then paste the repo link into Discord to check the card.
