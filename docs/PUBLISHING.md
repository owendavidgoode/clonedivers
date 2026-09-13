# Release workflow

Current player release: [STATUS.md](STATUS.md). Maintenance tools do not update players.
The older procedural notes are retained in [the archive](archive/2026-09-12-session/PUBLISHING.md).

## Rebuild and validate locally

Run `deploy-mods.ps1` into a separate staging game/data directory for a new pack.
It writes the deployment report and a receipt covering the recipe, report and every
file hash. A manifest build rejects missing/stale receipts, incorrect file membership,
changed content, and a texture variant from a different base pack.

Build new texture variants into a **fresh output directory** using:

```powershell
./tools/optimize-verified-pack.ps1 -Source <staged-data> -OutDir <new-variant-folder>
```

Set the recipe variant directory before deploying. The wrapper invokes the existing
optimizer, verifies the source remained unchanged, and records output provenance.
Do not hand-seal an old variant to silence a mismatch. r8's existing verified variant
is `dist/pack-optimized-current`.

Build a candidate without touching the shared manifest:

```powershell
./tools/build-manifest.ps1 -Version <pack-version> -GameDir <staged-game> -ManifestPath dist/candidate/manifest.json -UploadPlanPath dist/candidate/upload-plan.json
```

The recipe/report default to this workspace. Pass `-RecipePath` / `-DeployReport` when
building elsewhere. The report must describe the entire base pack, including RC files;
a clones-only installation is not a complete release source.

Alternatively `prepare-tested-pack.ps1` promotes a saved, fully tested manifest and
hash-named assets. `Release.Check` verifies every mode/texture combination against the
tested manifest and the expected Clonedivers baseline, plus the deployed default mode.
For intentional changes to ordinary Clonedivers, supply the explicitly tested new baseline.

## One release command, two explicit actions

Commit the intended source changes before preparing a future release. Build the
versioned executable to a separate folder. The existing public binary is never a
scratch build target. Put new content-addressed files in an asset directory.

```powershell
./tools/release.ps1 -Directory dist/release-ready -CandidateManifest dist/candidate/manifest.json -TestedManifest <tested-manifest> -BaselineManifest <expected-clone-baseline> -GameDir <tested-game> -AppExe <built-exe> -NotesPath <release-notes> -AssetDirectory <hash-named-assets>
```

The default **Stage** action is offline. It runs native tests and mode validation,
checks executable metadata and asset identities, copies the candidate into a sealed
staging directory, and writes `release.json`. It does not edit the shared manifest,
install anything, authenticate to GitHub, or publish. Use a fresh staging directory
after input changes. The current r8 dry run is `dist/maintenance-review/staged-r8`.

Only when a new player release is authorized:

```powershell
./tools/release.ps1 -Action Publish -Directory dist/release-ready
```

Publish requires committed changes and the prepared source commit. It pushes that
commit, creates/resumes draft releases, uploads missing assets, verifies every hosted
size and SHA256 digest, publishes verified assets, and downloads/checks the executable.
**Only then** does it commit and push the candidate manifest. A changed remote main
branch causes a stop, never a force push. Existing asset content is never overwritten.

Re-running the same Publish command resumes by rechecking hosted evidence. The state
file records a completed feed commit so a failed push can be retried. No failure before
artifact verification may activate the update feed. Already completed runs are no-ops.
The publication state machine has offline failure/resume tests; its new real GitHub
path has deliberately not been exercised during this no-download maintenance pass.

Old `publish-app.ps1` / `publish-pack.ps1` remain for historical compatibility; use the
new staged workflow for coordinated releases. `pack.json` stays frozen for 1.2 clients.
Never delete old `pack-*-files` releases: current manifests reuse their assets.

## Local maintenance checks

```powershell
./tools/test-deployment-contract.ps1
./tools/test-release-workflow.ps1
./tools/rebuild-rc-voices.ps1 -Template <verified-template> -OutDir <fresh-rebuild-folder>
```

Do not use test counts as a substitute for [gameplay evidence](PLAYTEST.md). Routine
experiments begin with a known-good baseline, change one variable, and use a small
sample before a full build. Record findings in STATUS rather than appending competing
“current state” paragraphs to old investigation logs.
