# Player-facing architecture and performance investigation

September 13, 2026. Baseline: published launcher 1.4.1 / pack 2026.09.12-r8.
This is an investigation and proposed scope for one coordinated update, not a
release announcement. No game files, settings, hosted assets or update feed were
changed during this investigation.

## Recommendation

Build the update around reliable mode/profile selection, easier troubleshooting,
and a measured performance option. Retain the three universe cards. Introduce a
separate texture choice that works before the first installation. Fix operation
state before adding more combinations: the current UI conflates requested mode,
installed content and active files in ways that affect repair and recovery.

Keep the proven file planner, hash-addressed downloads, reversible file moves and
transaction recovery. A small operation controller and explicit selection model
are sufficient; a wholesale launcher rewrite is unnecessary.

The Brawny explanation and removal of confirmation for cached mode switches are
already local source changes. Players do not have these changes yet.

## Confirmed findings and proposed fixes

| Priority | Finding | Player impact | Proposed change |
| --- | --- | --- | --- |
| P1 | Lighter textures can only be selected after mods are installed and ON | A new weak-PC user installs Full first, then downloads another texture variant | Select texture profile before installation; download only the chosen set |
| P1 | Repair/update always plans files into active `data` | Checking a parked pack can turn mods back on while Helldivers was selected | Preserve active/parked intent through repair, update and recovery |
| P1 | Recovery that needs a download falls back to last installed options | An interrupted switch can resume the wrong mode/profile | Carry the validated pending target through download and completion |
| P1 prerequisite | Manifest variants cannot safely represent multiple texture choices and transformed RC-only files | Naively adding Reduced can reject the manifest or enable RC content in Clonedivers | Add explicit independent mode/profile selection and compatibility handling |
| P2 | Modded mode selection depends on fetching metadata from GitHub | Offline players can turn mods off but cannot reliably turn cached modes back on | Cache validated metadata and retain the installed target receipt |
| P2 | “Pack up to date” reports version equality, not integrity | The interface can look healthy while files are missing or content is unknown | Separate installed version from Check and repair and last verification result |
| P2 | Selective inventory also prunes unvisited hash-cache entries | Returning to an inactive texture profile can rehash large parked files | Retain identities for existing inactive files; prune independently of selective hashing |
| P2 | Download cleanup retains only the currently requested profile | Switching away from an unfinished download can discard its resumable progress | Retain downloads referenced by supported profiles; clean obsolete data deliberately |
| P2 | Downloads have cancellation but no inactivity deadline | A stalled connection can appear to download forever | Bounded resume retries, inactivity timeout and a clear Retry action |
| P2 | No concise support report | Hardware, settings and install state require repeated questioning | Add Copy diagnostic report, with local preview and no automatic upload |

P1 here means high priority for this update, not that every player has encountered
the issue. The interrupted-recovery case has a specific missing/corrupt-byte trigger.

### Evidence in the implementation

- `Clonedivers/Program.cs`: options require `state == ModState.On`; `OnOption`
  enforces that condition. `RunPackAsync` composes settings/options and the planner.
- `Pack.Plan` targets `data`; `OnDownloadOrCancel` invokes repair without preserving
  the selected vanilla state.
- `Pack.Replay` returns null when `plan.Downloads.Count > 0`; `FinishUpdateAsync`
  then invokes `RunPackAsync` using the last installed settings.
- `LauncherModes.Current` combines disk ON/OFF state with saved options or manifest
  defaults. It does not prove which modded content is installed.
- `InventoryAsync` only scans old files needed for the current target, then removes
  all cache identities not visited. `CleanDownloadFolder` runs before confirmation
  and is passed only the current target's hashes.
- `DownloadPlanAsync` is sequential and resumable. The shared HTTP client has an
  infinite total timeout; no independent body-inactivity policy currently exists.

These are reasons to test complete user operations as well as individual planner
functions. Existing native tests pass, but their recovery boundary cases cover
available target bytes rather than the UI fallback that requires another download.

Native fixtures linked to the current production source reproduced three outcomes:

- A two-file parked pack changed from Helldivers to Commandodivers during repair,
  with zero downloads and both files moved into active data.
- A saved RC=true target missing one source file fell back to the previous RC=false
  selection, applied one file instead of the requested two, and cleared recovery.
- A profile round-trip discarded an inactive hash-cache entry and rehashed the full
  4,096-byte fixture file. This establishes the unnecessary read, not its timing
  cost on a player's large pack.

These fixtures exercise the production core in the UI's call sequence, not automated
UI clicks. They only write isolated fixture directories. Evidence and source:
`dist/investigation-2026-09-13/launcher/probe.log`, `fixture-run-1/results.json`,
`Probe.cs` and `LauncherProbe.csproj`.

## Texture profiles need a format change first

The builder currently expresses one texture replacement using `unlessOption` on
the base file and `option` on its alternate. It stores only one exclusion per
base name. Adding a second alternative overwrites that exclusion. It also emits
transformed variant files with the texture option alone, dropping an original
`commandos` condition.

A small fixture compiled against the actual launcher source reproduced both cases:

1. A transformed RC-only bundle remains selected with Commandodivers OFF and Lighter
   ON when emitted using the current builder predicates.
2. Two transformed texture alternatives emitted using those predicates are rejected
   by the native manifest parser as duplicate files without a valid variant pair.

This is a **future-feature blocker**, not evidence that shipped r8 leaks RC armor.
In r8 the RC bundles are shared unchanged between texture variants. The earlier
armor audit verified all 34 RC files and all 126 armor resource winners matched
with Lighter on and off. The user confirmed Brawny fixed the reported appearance.

Reproduction: `dist/investigation-2026-09-13/schema-probe/` and `schema-probe.log`.

Proposed model:

- `Universe`: Helldivers, Clonedivers, Commandodivers.
- `TextureProfile`: Full, Lighter, Reduced (Reduced becomes selectable only after
  validation). Universe and texture profile are independent, not two independent
  booleans that can accidentally enable competing variants.
- Files have explicit applicable mode/profile sets. Compile the selected pair to
  the same flat, unique, gap-free file plan the installer already handles.
- Keep selected preference, successful installed receipt, active/parked state and
  pending operation separate. Repair should not change the chosen universe.
- A pending transaction retains exact target files/URLs, profile, pack version and
  intended active state. A newer online release must not silently replace its intent.

The release builder, receipts and validation matrix must understand these choices
together. In particular, the verified optimizer wrapper currently does not forward
texture-cap/restream settings; extend it and record those parameters before a new
Reduced profile is built for release.

### Compatibility with existing launchers

Do not publish a new schema to the current feed and assume an unfamiliar field will
stop old clients. The current parser does not enforce a supported format version.
A practical migration is a new versioned feed for the new launcher, while the old
feed continues serving its compatible r8 pack and advertises the new executable.
This can be one coordinated release: update the launcher, then offer the selected
new profile. Existing users retain their universe and Full/Lighter preference.

Validate old-client behavior explicitly. Retain old hash-addressed asset releases.
Unaffected players should reuse existing bytes; only those selecting a changed
texture profile should download its changed assets.

## Performance: what the evidence can and cannot establish

Lighter textures moves texture levels into stream companions. It preserves visual
detail and can increase total disk size. A smaller pack, smaller GPU companion file
or fewer archives does not by itself establish better FPS or lower live VRAM usage.

The existing reduced-resolution experiment was built from r6, before the current RC
content. It is useful feasibility evidence, not a current release candidate. Prior
benchmark attempts produced zero valid gameplay frame rows after startup/tracing
failures. No reduced-profile gameplay speedup has been demonstrated.

### Measurements and candidates

The current RC Full manifest totals approximately 9.38 GiB; Lighter totals 9.56 GiB.
Installing Full and then choosing Lighter requires 204 additional unique asset
hashes totaling about 7.27 GiB. Offering the desired profile before installation
avoids that detour and the superseded files it parks.

The local r8 pack metadata was compared with a fetched public manifest and matched
semantically during the preceding armor investigation. This calculation assumes
no matching Lighter assets are already cached or parked from an earlier install.

The current r8 resource-directory audit gives a more useful breakdown than download
size alone:

- Lighter shifts approximately **4.24 GiB** out of GPU companion files, while adding
  approximately **186 MiB** to total pack storage.
- Commandodivers/Lighter still contains approximately **3.43 GiB of unit GPU payload**
  and **479 MiB of texture GPU payload** across archive entries. Unit payload is
  largely geometry-related data; these totals include superseded entries and are
  not a live memory inventory.
- The RC additions include 39 unstreamed texture entries, about 225 MiB. Twelve
  older helmet texture entries are superseded by the starter override, leaving 27
  final RC texture winners totaling about **145 MiB**.
- The pinned optimizer's read-only analysis projects roughly **136.5 MiB** less
  GPU texture payload among those final winners if eligible RC textures are streamed.
  This is a modest candidate improvement; it is not measured VRAM savings or a
  demonstrated cure for the weakest PC.

The RC streaming gap is worth closing after the mode/profile ownership fix. Use
measured output inventories, not the optimizer's friendly summary: its analysis
can omit the added stream size from a displayed total and mention duplicate sharing
even when aliasing is disabled. Detailed current-pack evidence is in
`dist/investigation-2026-09-13/assets/costs.json` and the adjacent analyzer logs.

For the old r6 experiment, compare Reduced against **Lighter**, not just Full:

| r6 profile | Total files on disk | GPU companions | Stream companions |
| --- | ---: | ---: | ---: |
| Lighter | 9.1732 GiB | 3.8117 GiB | 4.7235 GiB |
| 512-pixel cap / 128 resident tail | 4.6390 GiB | 3.4487 GiB | 0.5523 GiB |
| 256-pixel cap / 64 resident tail | 4.4492 GiB | 3.4350 GiB | 0.3761 GiB |

These are file component sizes, not live memory measurements. The 512 candidate
removes about 4.17 GiB of streamed chain storage compared with Lighter, but only
about 372 MiB from GPU companions. Going from 512 to 256 saves only another roughly
14 MiB in GPU companions. This favors testing 512 before accepting the extra loss
of detail at 256. Much of the remaining geometry/audio cost is unchanged.

### Smallest useful performance experiment

1. Collect the weakest PC's CPU, GPU and dedicated VRAM, RAM, SSD/HDD, resolution,
   render scale and game graphics settings. Record whether the issue is steady low
   FPS, intermittent stutter, progressive degradation or loading time.
2. Establish a clean, working baseline on the current game build. Compare vanilla
   with the player's usual modded universe using Lighter. If needed, compare
   Clonedivers/Lighter with Commandodivers/Lighter to isolate the added RC content.
3. Build separate current-pack 1024 and 512 candidates with the **same resident
   floor**, preserving every mod and all mode gates. Compare visible quality to
   choose the least aggressive useful cap. Verify texture transformations and
   unchanged non-texture payloads before a reversible playtest. Do not install an
   old r6 candidate over r8. The historical 512/256 comparison changed the resident
   floor as well as the cap, so it did not isolate resolution alone.
4. Repeat Lighter / Reduced / Lighter on the weak PC with the same settings, scene,
   route and similar combat conditions. Separate first-load streaming from warmed
   play. Menus and the ship do not establish combat performance.
5. Record frame times and percentiles, time spent in long stalls, process memory,
   system memory pressure, dedicated/shared GPU memory and disk activity. Current
   `measure-game.ps1` needs additional telemetry and summaries for this purpose.
6. Ship Reduced only after a repeatable improvement in the reported symptom without
   visual breakage or startup/reinforcement regressions. Record variability rather
   than treating one favorable run as proof.

If the bottleneck is CPU/render submission or geometry, reduced texture detail may
not help enough. Then investigate the largest mesh contributors and valid LODs,
plus resources superseded by later patches. Do not strip or merge resources merely
because IDs repeat: load order, dependencies and shared resources need verification.

The resource audit narrows that follow-up considerably:

- Keeping only final unit resource winners still leaves **3.318 GiB** of GPU
  payload. The largest raw contributors are Clone Armory (~2.06 billion bytes),
  CIS (~536 million), blasters (~247 million) and AT-TE/LAAT-c (~245 million).
  Payload size prioritizes inspection; it does not establish triangle counts,
  visible instances, draw calls or the runtime bottleneck.
- Hashing all **2,414 unit GPU slices** found only **39.9 MiB / 1.14%** of repeated
  bytes. Generic byte deduplication is therefore a small storage opportunity,
  and shared disk offsets would not prove shared runtime GPU allocations.
- The pack contains 6,167 superseded resource entries with a summed payload of
  approximately **847 MiB**. This is an upper bound before overlap/dependency
  validation, not guaranteed removable data. Repacking could reduce downloads
  and disk use, but changes many hashes and needs new mode-specific verification.
  It should not lead the gameplay-performance work.

Detailed asset methodology, per-mode totals and analyzer caveats are recorded in
`dist/investigation-2026-09-13/assets/FINDINGS.md`. The first mesh investigation
should count actual vertices/indices and inspect LOD behavior for the heavy groups;
obtain optimized author assets or preserve skeletons, attachments and silhouettes
when authoring lower-detail distant meshes.

## Proposed combined update and validation

### Include

- Existing Brawny help and direct cached mode switching.
- Independent texture profile available before install; migrate the existing skinny
  preference faithfully. Keep spoilers out of mode descriptions.
- Operation controller/receipt changes for repair, recovery and offline switching.
- Explicit Check and repair plus last-check result; distinguish unknown/manual packs
  from successfully installed content.
- Copy diagnostic report and retained recent operation/error summaries. Show what
  will be copied; omit account identifiers and personal paths; no automatic upload.
- Resume/cache preservation and bounded stalled-download recovery.
- Stream the omitted eligible RC textures in Lighter after fixing profile ownership,
  with independent payload verification and an in-game appearance/startup check.
- Reduced textures only if the current-pack weak-PC trial passes.

### Defer unless measurements justify it

Do not promise that faster launcher polling or parallel downloads fixes gameplay.
Pausing unnecessary refresh while minimized is small polish. Bounded concurrent
downloads may improve installation time, but disk/network measurements should guide
it. Avoid introducing a huge merged pack, GPU-payload aliasing, global automatic
graphics changes or automatic armor-to-voice routing into this update.

### Required acceptance checks

- Fresh install in each modded mode/profile selects only needed assets.
- Every supported mode/profile resolves to unique, gap-free files with the right
  resource winners; RC content never appears in ordinary Clonedivers.
- Repair/update while vanilla stays vanilla, including interruption and restart.
- Interrupted switches with missing/corrupt bytes retain the requested target even
  if the online pack changed. Invalid legacy plans get an explicit recovery path.
- Offline cold start can restore cached modes; missing bytes produce actionable
  connectivity information; corrupt metadata cannot masquerade as verified state.
- Cancel/profile-switch/return resumes partial downloads. Cached round-trips avoid
  unnecessary rehashing, while explicit repair still verifies file contents.
- Local HTTP fixtures cover stalled responses, range resume, disconnect, retry
  exhaustion and cancellation during backoff.
- Existing 1.4.1 clients can still read their feed and update safely. New launcher
  migration preserves options and uses existing content hashes.
- RC intro, four body/helmet appearances, selected voices heard by a second player,
  reinforcement and repeated mode changes pass gameplay checks. One confirmed
  Brawny fix is not a full multiplayer validation.

Implementation order: lock down the operation/schema regression fixtures; implement
selection and transaction state; add diagnostics and UX; run the texture experiment;
then stage one coordinated executable/feed/optional-profile release. Do not publish
intermediate builds to the team while these pieces are being combined.

## Follow-up: geometry and usable comparisons

A bounded read-only follow-up parsed fifteen effective unit resources using the
local Filediver metadata definitions (commit
`42ee19a7d8a743a77b7db996ce8bc64f47105f71`). Several declared LOD entries reference
equally detailed geometry. HMP dropship mesh records 6–14 have matching corresponding
vertex and index payload hashes across both LOD groups. A sampled Phase 2 helmet's
three LOD levels also reference matching payloads. All sampled AT-TE levels retain
61,052 index-count/3 triangle-list equivalents; the Breakthrough variant has repeated
vertex payloads and two index-hash classes with repeated adjacent levels.

This establishes repeated geometry in these modded LOD slots, not the game's actual
draw counts or an FPS benefit. Groups can represent damage, shadow or other variants.
The vanilla dropship uses external geometry group `417da1b8e06cb5b9`. The initial
follow-up had not parsed it; the subsequent local-development pass below completes
that comparison.
Simplified distant meshes are now a concrete authoring candidate; changing thresholds
alone cannot reduce polygon count when each level references equally detailed meshes.

Full methodology, warnings, source hashes and reproducible scripts:
`dist/investigation-2026-09-13/geometry/FINDINGS.md`.

The [performance trial guide](PERFORMANCE-TRIAL.md) starts with existing-download
Lighter → Helldivers → Lighter runs on the weakest PC. Support tooling now captures
the actual HD2 frame-cap, upscaling and LOD settings. An offline comparison recomputes
raw frames, rejects failed captures and mismatched recorded settings, and reports
per-run medians/ranges and normalized hitch rates. Eighteen fixture checks pass.
There are still no valid gameplay measurements, no private texture install, and no
publication from this follow-up.

## Local development: completed vanilla dropship comparison

The user cannot provide weak-PC tests; proceed on the current PC. External geometry
was extracted read-only from the current public game build 24826606. Unit LOD entries
must resolve through unit mesh bone identity into the external geometry table;
those two tables have different order. Comparing array indices directly is wrong.
The comparison script verifies matching vanilla/mod unit bone identities and LOD
thresholds before reporting counts.

| Visible slot | Vanilla triangle equivalents | HMP replacement |
| --- | ---: | ---: |
| g_body | 129,129 | 271,205 |
| g_body_LOD0 | 64,570 | 271,205 |
| g_body_LOD1 | 19,371 | 271,205 |
| g_body_LOD2 | 5,810 | 271,205 |
| g_body_LOD3 | 1,743 | 271,205 |

Original shadow slots fall from 19,370 to 5,811, 1,161 and 579; the replacement stays
at 271,205 for all four. This establishes increased static geometry relative to
vanilla for corresponding slots. It does not establish simultaneous draws, selected
runtime distances, frame-time cost or a guaranteed optimization gain.

A private 78,864-byte original-unit control is staged for HMP → original geometry →
HMP gameplay comparisons while keeping the rest of the pack. The build round-trips
the original unit payload exactly; Enable/Disable owns one extra patch using a
hash-checked receipt. Nine disposable fixture checks pass. One shared vanilla
material remains overridden by CIS, so a visual check is mandatory before treating
the control as valid. It measures the net model/material-reference change, not a
pure polygon-only experiment. Standalone Filediver initialization requires global
game customization metadata; that attempted validation supplies no runtime proof.

Evidence: `dist/investigation-2026-09-13/geometry/dropship-vanilla-comparison.json`,
`dropship-control/build-report.json`, and the developer procedure in
[PERFORMANCE-TRIAL.md](PERFORMANCE-TRIAL.md). No live patch, installed setting or
published feed was changed. The control is not installed or gameplay-tested.

## Arrowhead's own slim build: relevance to this pack

Reviewed September 13, 2026. Arrowhead's October 3, 2025
[Tech Blog #1](https://www.arrowheadgamestudios.com/2025/10/helldivers-2-tech-blog-1-install-size/)
explained deliberate asset duplication to reduce mechanical-drive seeks. Its proposed
shared bundles could load unnecessary common resources and increase RAM use; engine
work to avoid that was a plan, not confirmation of the eventual implementation.

The December 2, 2025
[Tech Blog #2](https://steamcommunity.com/games/553850/announcements/detail/491583942944621372)
credits Nixxes and complete data deduplication for reducing installation size from
about 154 GB to 23 GB. Game-specific tests found level generation dominated loading,
with asset reads running in parallel; HDD load penalties were only seconds. The
announcement describes functional parity, not a measured combat-FPS improvement.
The Steam page's body was unavailable to the web reader; the announcement text was
read from its [SteamDB reproduction](https://steamdb.info/patchnotes/20965246/).
No detailed shared-resource format or loader implementation is disclosed there.

Implications for our investigation:

- Keep packaging savings separate from texture residency and rendered geometry.
  Our Lighter profile changes texture streaming; reduced profiles also cap texture
  resolution. Arrowhead's result does not measure either mod technique's benefit.
- Do not assume fewer copies on disk mean fewer runtime allocations or fewer
  polygons drawn. Simplified distant meshes remain a separate optimization target.
- The earlier 1.14% duplicate figure covers entire unit GPU slices only. New matching
  mesh subranges inside units mean that figure does not bound all possible geometry
  storage savings. A subresource audit could quantify additional packaging potential,
  but any repack still needs loader/dependency validation and measured runtime impact.
- Avoid converting everything into always-loaded common bundles without testing RAM.
  Follow Arrowhead's useful methodological lesson: measure this game's actual limiting
  stage before relying on assumptions about storage, textures or loading time.

No game files, candidate assets or published feeds changed for this research.

## Primary references and measurement notes

September 13 evening follow-up: the Full shadow-only and distant-detail candidates
completed one local combat capture each, with normal visuals reported. Average FPS
was 79.5 (shadows), 74.0 (balanced) and 76.0 for the clean final original baseline.
The earlier original raw frames averaged 81.2 but had an unavailable collector exit
status. Single samples, changing combat and a reboot before the final baseline
prevent a reliable performance conclusion. Retain original geometry for the team
update. Originals are restored, captures are stopped, and no candidates were
published. Reduced texture testing remains deferred. See
[the playtest results and evidence](PERFORMANCE-TRIAL.md) for details; earlier
no-gameplay statements in this investigation describe the preceding research stage.

The detailed performance research is saved in
`dist/investigation-2026-09-13/performance/research.md`.

- [PresentMon 2.5.1 console documentation](https://github.com/GameTechDev/PresentMon/blob/v2.5.1/README-ConsoleApplication.md)
  defines the pinned capture tool's fields and options. Parse its actual schema;
  correlate counters and gameplay markers rather than accepting any nonempty CSV.
- [Microsoft GPU memory explanation](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/)
  explains dedicated/shared memory and cross-process double counting. Adapter
  totals and correlated frame-time stalls are more useful than a lone allocation.
- [Microsoft hard-fault documentation](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/xperf/hard-faults)
  distinguishes disk-backed page retrieval from ordinary faults. Pair memory
  pressure with paging and disk latency rather than treating working set as proof.
- [DXGI adapter description](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc)
  provides appropriately sized dedicated-memory fields for hardware reporting.
- [Stingray Texture Optimizer](https://github.com/Shiroiame-Kusu/StingrayTextureOptimizer)
  documents texture streaming, cap behavior and unchanged non-texture payloads.
  Upstream capabilities may differ from the pinned tool. Single-mip textures need
  an explicit separate investigation; do not assume they can be streamed unchanged.
