# Performance development and gameplay trials

## Completed local geometry playtest — September 13 evening

The agreed four-capture session is complete. The user reported normal visuals and
similar combat for both candidates and confirmed the final combat sample. The
normal r8 Full Commandodivers pack is restored and remains active; recording has
stopped. No additional captures or restarts are requested for this round.

| Condition, in recording order | Average FPS | p99 frame interval | Recorder status |
| --- | ---: | ---: | --- |
| Original, initial | 81.2 | 15.92 ms | Raw-frame review only; exit status unavailable |
| Shadows | 79.5 | 17.35 ms | Clean exit, 90 seconds |
| Balanced: shadows plus distant detail | 74.0 | 20.05 ms | Clean exit, 90 seconds |
| Original, after reboot | 76.0 | 20.00 ms | Clean exit, 90 seconds |

Every sample contained zero invalid intervals and no intervals over 50 ms. Captured
graphics settings, hardware fields and PresentMon version matched. There is only
one clean sample per condition, with user-confirmed similar combat rather than a
deterministic replay. The final original sample followed a Windows reboot, and
system-wide paging activity differed. The initial original sample had a collector
exit-status defect and is excluded from automated comparisons; its raw frames and
failure artifacts remain available for context.

**Decision: retain original geometry for the team update.** Neither candidate has
shown a reliable performance benefit. Shadows were 4.5% faster than the final
original sample but slower than the initial raw sample; balanced was 2.7% slower
than the final sample. These single-run differences do not establish improvement
or regression. Sampled visuals passed, but this does not verify every affected
asset, distance or animation. No weak-PC or memory-saving claim is supported.

Evidence is in `dist/benchmarks/optimization-20260913-201320`: raw captures,
`all-captures-review.json`, two `comparison-*-vs-final-baseline.json` reports,
visual/scene reviews and `session-status.json`. The first recorder issue was fixed
by retaining the child process handle under Windows PowerShell 5.1; subsequent
captures recorded exit code 0. Eighteen measurement checks and a process-handle
smoke test passed. A separate GameGuard 110 launch failure involved a lingering
`GameMon.des`; the user rebooted. All 16 restored files matched original hashes,
and no GameGuard files or services were modified.

The reduced 1024/128 and 512/128 texture candidates remain local, uninstalled and
untested. Texture testing is deferred to a separate session. The preparation and
optional protocols below are retained for reference, not additional required tests.

## Candidate preparation — September 13

The new mesh candidates preserve each working source archive's complete layout,
header, table of contents, allocation metadata, alignment, neighboring resources
and padding. Only selected unit mesh index prefixes and their triangle-list index
counts change. Existing vertices, skinning maps, materials, bounds and the nearest
visible LOD slots remain intact. Border and skin-influence boundary locks constrain
simplification; UV attributes participate in its error calculation. Animated
silhouettes, shadows and distant transitions passed the sampled combat review;
complete asset coverage has not been established.

| Candidate | Intended effect | Local location |
| --- | --- | --- |
| Shadows | Reduce shadow geometry without changing visible meshes | `dist/geometry-candidates/layout-preserved/{full,lighter}/shadows` |
| Balanced | Add reduced distant visible detail to the same shadow changes | `dist/geometry-candidates/layout-preserved/{full,lighter}/balanced` |
| Reduced 1024 | Lower texture resolution with a 128-pixel resident mip tail | `dist/profile-candidates/reduced-1024-tail128` |
| Reduced 512 | Stronger texture reduction, also using a 128-pixel tail | `dist/profile-candidates/reduced-512` |

Both mesh variants cover HMP dropship, HMP gunship, B2 Devastators, all four AT-TE
variants and one Phase 2 helmet resource. They are deliberately limited to these
audited resources. Across modified shadow LOD slots, triangle counts fall about
61–74%, depending on the asset. These are sums across different LOD slots, not a
count of triangles simultaneously drawn or a prediction of FPS. File lengths and
vertex allocations are unchanged; this is a rendering-work experiment.

Four mesh builds are staged: shadows and balanced against each of the exact r8 Full
and Lighter baselines. Each includes 16 changed source files, hashes of the originals,
hashes of the candidates, a simplifier version/hash and per-group changes. The
archive-preserving writer has 13 passing focused checks. Independent whole-file
verification receipts are retained under
`dist/investigation-2026-09-13/geometry/layout-preserved-validation.json`.

The older combined archives directly under `dist/geometry-candidates/shadows` and
`balanced` are superseded and must not be installed. Review identified unvalidated
container metadata normalization in that prototype. The current builder and private
installer use only schema-2, `preserved-source-layout` candidates.

### One test session, separate conditions

1. Start with the existing Full pack. Check the affected models, then capture a
   warmed-up combat baseline with dropships/B2s and a separate AT-TE scene.
2. Close game and launcher. Enable Full/shadows, check shadows and animation, and
   repeat comparable captures. Disable to restore originals.
3. Enable Full/balanced, check close-up identity, distant transitions, all walker
   variants and animation, then repeat captures. Disable and repeat the baseline.
4. Compare Lighter and the 1024/128 and 512/128 texture profiles with original
   geometry. Record quality alongside frame times, memory and paging indicators.
5. Combine only candidates that pass their individual checks, then validate the
   combination and mode switching. Geometry files are tied to their exact texture
   baseline: do not copy Full/Lighter files over a reduced profile. A combination
   with reduced textures requires a fresh verified build against that profile.

Use the current PC and fixed graphics/FOV, scene, difficulty, host role and capture
settings. Prefer three comparable captures per condition; changing enemy counts
can overwhelm a modest improvement. Stop a condition on broken shadows, animation,
missing models or crashes and restore its baseline. No test changes GameGuard.

The private geometry harness defaults to Status:

```powershell
./tools/set-geometry-trial.ps1 -Action Status
./tools/set-geometry-trial.ps1 -Action Enable -Profile full -Variant shadows
# Launch through Steam with the launcher closed, inspect, then capture.
./tools/set-geometry-trial.ps1 -Action Disable
```

The harness passed 34 fixture checks under Windows PowerShell 5.1, including
interrupted installation/restore, changed files/backups, mode parking and conflicting
trials. Evidence: `dist/investigation-2026-09-13/geometry/geometry-trial-tests.log`.
It verifies source hashes, backs up originals and records recovery state
before replacing files. Disable restores verified originals, including after files
are parked in `mods_off`. Restore before using launcher repair, updating or changing
texture profiles. The original-dropship control and geometry trial cannot be stacked.
Reduced texture installation still needs its own verified local profile application;
the staged executable alone fetches public feeds and cannot select private assets.

All candidates remain local and unpublished. Gameplay acceptance and measured
benefit decide which changes join the single team update; none is a promised FPS fix.
The local catalog is `dist/geometry-candidates/playtest-catalog.json`. The previously
sealed 1.5.0 release stage predates these mesh candidates and does not contain them;
prepare a fresh release stage after gameplay acceptance.

## Current constraint: develop and test on this PC

The user cannot test on the weakest PC. Continue development and controlled
comparisons on the current machine; do not block work on teammate hardware details.
Use frame times and GPU busy time to compare costs even when average FPS looks
acceptable. Keep the same settings within each comparison. This machine's headroom
may hide the memory-pressure threshold of a weaker machine, so do not translate a
local percentage into promised gains for the team or treat artificial limits as
faithful weak-PC emulation.

The first targeted local control is HMP dropship versus original dropship geometry
with the rest of the mod pack present. Static analysis resolved both visible and
shadow LOD slots: HMP retains 271,205 triangle equivalents in each; original visible
geometry falls from 129,129 to 1,743, and original shadows from 19,370 to 579.
This is a measurable asset-cost lead, not a runtime FPS result.

Private tools:

- `tools/compare-dropship-lods.ps1`: resolve vanilla external geometry by bone identity
  and compare corresponding slots. External mesh order differs from unit mesh order.
- `tools/build-dropship-control.ps1`: stage one original-unit override, using the
  base game's external geometry. It does not install anything.
- `tools/set-dropship-control.ps1`: defaults to Status; Enable appends one temporary
  patch after the active pack, and Disable removes only its recorded exact hash.
  Game and launcher must be closed for changes. Do not use the launcher to repair
  or update while the control is installed.
- `tools/test-dropship-control.ps1`: disposable installation/removal/tamper fixtures.

Capture normal HMP first. With game and launcher closed, enable the private control,
then check the original dropship appears and casts shadows before capturing a
comparable encounter. Disable it, restart and repeat HMP. The test must actually
include dropships; the ship hub cannot establish this model's runtime cost.

This changes unit geometry and material references together. One original shared
material is overridden elsewhere in CIS and remains so in the control; inspect
appearance before drawing conclusions. Attribute results to the net replacement,
not automatically to polygons alone. Payload round-trip checks and nine disposable
harness checks pass. Standalone Filediver validation cannot initialize without the
game's global customization lookup; the control has not passed an in-game test.

After this, compare Lighter and the reduced texture candidates locally. Keep the
weak-PC protocol below as future optional confirmation, not a prerequisite.

Historical status before the evening playtest: the September 7 capture failed to
start Windows tracing and contains zero frame rows. The new reduced-texture candidates
are built locally; they are not installed or published. Smaller files alone do not
establish higher FPS or lower live memory use.

## Optional weak-PC confirmation: use existing downloads

On the weakest PC, record CPU, GPU and VRAM, RAM, display resolution, graphics
settings, and whether the symptom is low FPS, stutter, or both. Keep the same
player hosting or joining throughout. Record whether the game is on an SSD.

1. Use the player's current modded mode with **Lighter** textures. Restart the game,
   warm up a mission for 2–3 minutes, then play a repeatable 90-second route. Record
   FPS and visible freezes; keep a separate note for a combat segment.
2. Close the game, select **Helldivers**, restart and repeat comparable conditions.
3. Close the game, restore the original modded mode with **Lighter**, restart and
   repeat. Returning to the baseline helps reveal warming or changing conditions.

Keep resolution, render scale, FOV, graphics settings, loadout, planet, difficulty,
squad and host role fixed. Avoid menus, loading and alt-tab during measurements.
Mission/enemy variation remains a limitation; repeat each condition three times
before making a release decision. A ship view is useful for a quick visual check,
but cannot establish combat performance.

| Observation | Next investigation |
| --- | --- |
| Vanilla also runs poorly | Check the base game's rendering/workload and hardware limits before attributing everything to the pack. |
| Modded runs repeatedly hitch more | Test reduced textures and correlate frame spikes with memory/paging/streaming activity. |
| Modded FPS stays lower even after texture reduction | Inspect mesh detail and LOD behavior; texture changes do not reduce geometry. |
| Results vary as much as the apparent improvement | Repeat a more controlled scene; do not call it a measured gain yet. |

## Second check: private texture trial

After local appearance/startup acceptance, compare Lighter with Reduced 1024 on
the weak PC, returning to Lighter afterward. Try Reduced 512 only if needed, and
compare its visual tradeoff. Both reduced candidates use a 128-pixel resident mip
tail, so their mutual comparison isolates the resolution cap. Lighter versus a
reduced candidate tests the whole profile, including its different streaming tail.

The candidate manifest is `dist/profile-candidates/manifest-trial-v3.json`.
The 1.5.0 executable alone does **not** install these unpublished assets. A private
installation must use the verified local candidate files, preserve the existing
installation and settings for rollback, and record the actual selected profile.
Do not distribute the old r6 experiments or the superseded 1024/256 candidate.

## Support capture and comparison

These commands are for the person assisting the test, not a normal launcher flow.
There is no automatic upload or change to game settings. Use a first short capture
to confirm that the existing PresentMon tool can trace the game. If Windows denies
ETW permission, obtain the documented tracing permission or use existing FPS
counters for preliminary observations; do not change GameGuard.

From the repository, after the scene is ready:

```powershell
./tools/measure-game.ps1 -Label lighter-a -Scene traversal -GameplayReady -Seconds 90 -ExtendedCounters
```

Run once per condition with a descriptive label. Capture tooling and extended
counters must be identical between compared runs. Check collection overhead on
the weak PC against an observation without capture.

Review raw captures for loading, menus and alt-tab before confirming comparable
scenes. Pass the resulting summary paths (arrays for repeated runs):

```powershell
./tools/compare-game-captures.ps1 -Baseline $baselineSummaries -Candidate $candidateSummaries -ComparableScenes -OutPath dist/benchmarks/comparison.json
```

The comparison recomputes results from raw frame CSVs, rejects failed or short
captures, checks captured hardware/settings/build/tool consistency, and reports
across-run medians and ranges. It normalizes long-frame counts per minute. FPS is
derived from application frame intervals, not displayed/generated frame count.
Overlapping ranges flag variability; non-overlap does not prove causation.

GPU/driver identity, actual loaded mod files, clocks, temperature and any
unavailable settings still need manual confirmation. Memory CSVs are supporting
evidence, not an automatic diagnosis. Record visual quality alongside performance.

The test itself does not publish an update. Reduced textures remain excluded from
the public candidate until gameplay evidence and appearance justify inclusion.
