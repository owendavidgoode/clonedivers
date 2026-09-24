# AT-TE camera

## September 23: runtime capability probe

The user confirms the obstruction also happens at regular aspect ratios.
Treat the enlarged model/camera relationship as the primary investigation;
another FOV or aspect-ratio change is not an established remedy.

Reviewed Bingus Shared Loader v16 source and its declared-addon packaging.
Its Wwise bridge preserves stock callbacks and provides Lua startup discovery;
it does **not** provide a documented camera or occupied-vehicle API. The public
Controllable Hover Pack implementation uses build-specific native memory layouts
to identify its local avatar and owned equipment. Those offsets are not an
established vehicle/camera interface and have not been copied into this project.

Staged a read-only startup inventory in `tools/atte-camera-probe.lua`. It lists
exposed namespace/member names and types, without calling any engine functions,
reading native memory, installing update hooks, or changing camera/mesh state.
It bounds output and suppresses diagnostic errors. Seven LuaJIT fixture cases
pass: normal, missing loader, missing log, open/write/close failures and oversized
tables. Fixtures also check that no engine function or table metamethod is called,
no global changes, and no runtime values are logged.

The public release zip SHA-256 matches the author's SHA256SUMS; the installed
EXE and game DLL match its build 25327279 fingerprints. Its own manifest sets
`runtime_verified: false`, so this is a local experiment, not a team release.
An audit of all 393 installed patch archives found no Lua overrides and no
resource collisions. Additive slots 304/305 hold the loader and probe; no existing
pack asset is replaced. Source, packages, audit, install receipt and captured logs
are under `dist/atte-camera-runtime-2026-09-23/`.

Remove the diagnostic with game and launcher closed:
`dist/atte-camera-runtime-2026-09-23/Install-Probe.ps1 -Action Remove`.
The helper checks the six exact paths and installed hashes before removal.
Runtime log: `%LOCALAPPDATA%/CowboyBingus/Helldivers2/Logs/ClonediversCameraProbe.log`.
Function presence alone will not establish a valid active camera handle or a
reliable local occupied-walker signal. Both need verification before any mutation.

Runtime attempt: the initial launch produced no diagnostic log; the user reported
another program was open and requested a retry. The September 23 20:47 retry
reached GameGuard error 114 (Steam logged `ggerror.des 114 SONYGame 1`). No probe
log was produced, so exposed APIs remain unknown. The failed game was closed;
the six diagnostic files remain installed for a post-reboot retry. Do not infer
that either the loader or another application caused the initialization failure.
See `runtime-result.json` in the experiment directory. Nothing published.

Subsequent result: after the user's reboot, the 20:51 launch again reached 114.
Removed the six diagnostic files, verified their absence and gracefully restarted
Steam because its launch log reported `WaitingPrevProcess` after the failed run.
The baseline-only launch at 20:53:44 also reached GameGuard 114 at 20:53:52.
No new camera capability evidence was obtained. The diagnostic is **currently
removed**, superseding the installed state above. A fresh reboot with baseline
only is needed for a clean comparison; same-boot removal alone does not establish
whether the experimental loader was involved. Do not reinstall it yet.

The next fresh reboot with baseline only again produced 114 at 20:57:21. No
matching recent System/CodeIntegrity errors were returned by the bounded query.
Following Arrowhead's official Error 114 instructions, verified valid INCA Internet
signatures on the bundled `tools/gguninst.exe` and `tools/GGSetup.exe`, preserved
the existing `.erl` logs locally, and ran uninstall `/silent` then setup elevated.
Both returned exit 0; see `gameguard-repair.json`. After another graceful Steam
restart, the baseline launch still produced 114 at 21:01:11. One-time administrator
game launch requested next; Windows elevation/result pending. No permanent
compatibility flags, firewall/antivirus exclusions, or security settings changed.

The user approved the administrator prompt. The elevated game started as PID 20040
and exited; GameGuard logs updated around 21:03:44. The direct launch has no new
Steam error entry, and the recent Windows events query found no matching crash
event. This is not evidence of another 114 or a successful boot. Asked the user
what appeared; await that answer before retrying. Logs retained locally under
`gameguard-after-admin-launch/`. Diagnostic remains removed.

The user subsequently requested a Clonedivers update before manually revalidating
HD2 in Steam. The desktop shortcut pointed to launcher 1.5.0; updated it to the
verified local 1.6.0-preview6 with automatic loopback feed startup. Reapplied and
verified the current preview3 install receipt: all 1,133 Full-profile files matched
already, so no pack bytes changed. Diagnostic and cutaway remain excluded. Further
camera experiments wait until baseline startup works after Steam revalidation.

## Player screenshots, September 23 at 20:29

Inspected four Steam screenshots in
`C:/Program Files (x86)/Steam/userdata/67385638/760/remote/553850/screenshots/`:
`20260923202912_1.jpg`, `20260923202914_1.jpg`,
`20260923202926_1.jpg`, and `20260923202952_1.jpg`.
All are 7680x2160. The first two show the occupied HUD/reticle through a narrow
gap, with very close hull panels and leg/joint geometry covering much of the
screen. The latter two show on-foot/death views and are not occupied-camera
comparisons. The original meshes had been restored before this capture.

The occupied views suggest the camera sits among or beside the enlarged body and
leg geometry, rather than looking cleanly over its roof. This is an inference
from projection, not a measured camera transform. The screenshots do not prove a
separate interior mesh, camera-collision issue or a specific offset. The reticle
is already visible; replacing it alone cannot reveal the obstructed scene.
Prioritize locating the occupied camera relative to the rig and rendered geometry.
The user subsequently confirmed the issue at regular aspect ratios too.

## Cutaway rejected after gameplay test

The user reports no useful cutaway while occupying the vehicle and unacceptable
loss of the exterior hull. The first cutaway is rejected. A crash also occurred
before the successful retry; its cause was not established. Structural validation
did not establish a useful camera fix, and the triangle-removal percentage was
not a measure of how much of the exterior would visibly disappear.

Restore the original `2026.09.23-player-preview3` pack; rollback verification is
recorded in `dist/walker-cutaway-2026-09-23/rollback/verified-install.json`.
Keep launcher `1.6.0-preview6` and its regular-Helldivers option hiding.

A bounded search of the available asset-name catalog and extracted walker bones
did not identify a separate named interior/pilot mesh or a follow-camera control.
The test does not prove whether occupied rendering, remaining geometry, or camera
collision/framing caused the failure. Do not infer a separate cockpit model from
this symptom alone or enlarge the permanent cut without new evidence. Further
camera work needs evidence from the occupied view and its actual render/camera
path, while retaining the intact exterior.

## September 23: first cutaway candidate

`2026.09.23-atte-cutaway1` opens the center of the rear/upper hull in all four
AT-TE variants. The exported bind-pose geometry shows forward along +Y and
up along +Z. The selected triangle-centroid region is X between -3.7 and 0.2,
Y below 1.8, and Z between 1.4 and 3.9. This removes 10,212 of 61,052 triangles
at each visible detail level (16.7%). The front, outer hull sides, legs and
upper cannon remain; the open hull is intentionally visible from outside too.
Original shadows remain for this first visibility experiment.

`tools/build-walker-cutaway.mts` verifies donor hashes, retains the archive layout,
and modifies only visible-mesh index prefixes and their draw counts. Every
vertex, skin weight, bone, animation, physics payload, bounds and unrelated
resource is byte-identical. No camera/FOV or protected executable changes.
Full and Lighter variants are built from their respective verified donors.
Unrelated assets and all file sizes are unchanged. Rendered orthographic reviews
and validation receipts are in `dist/walker-cutaway-2026-09-23/`.

The production installer planned eight replacement files (187.7 MiB) for the
current Full profile, retaining the originals in `mods_old`. Local install
results are in that directory's `local-install/`; gameplay acceptance is pending.
This is not a team release or an automatic pilot-only effect.

Test the pilot view looking level, up/down, while aiming and turning. Exit and
walk around the hull; check entry, movement and weapons. Then inspect a teammate's
walker. The intended result is a clearer view, with normal controls and collision.
The outside model will have the same cutaway on this client.

Rollback: close game and launcher, then use `tools/PlayerUpdate.Apply` with
`apply dist/vehicle-update-2026-09-23/feed`, the existing game path, a fresh audit
directory, and `http://127.0.0.1:8767/manifest.json`. The baseline server remains
available on 8767; the cutaway feed uses 8768. Both servers are loopback-only.
Run the installer's plan first; originals must be recovered by their hashes.

## September 23: invisible body / cockpit exploration

Read-only inspection verified all four current Lighter AT-TE donors against the
preview3 manifest SHA-256 values. Each has eight meshes: four visible detail
levels and four shadow detail levels. Every mesh has exactly one populated
material group and 61,052 triangles. Lumberer declares more material slots, but
only one group actually draws. There is no separate roof/hull draw group to turn
off. Evidence: `dist/launcher-cleanup-2026-09-23/walker-inspection.json`; the
adjacent inspector reuses the previously verified unit parser. No game files changed.

Options and limits:

- **Whole-body hide:** a visual-only candidate could suppress the visible groups
  at every detail level while retaining skeleton, animations and physics. This
  has not been built or validated in game. A static asset replacement applies to
  every matching walker rendered by that client, including parked and teammate
  walkers. It is not a pilot-only switch. Separate weapon units may remain visible;
  driver and shadow handling would also need a gameplay check.
- **Cutaway:** export the mesh and identify the rear/upper triangles obstructing
  the camera, then remove only those surfaces consistently across detail levels.
  This keeps more of the walker visible, but remains a permanent visual change
  on that client. Geometry inspection is necessary before choosing triangles;
  a material toggle cannot isolate the hull in these donors.
- **Pilot overlay:** cockpit framing could be cosmetic, but cannot reveal terrain
  already covered by the body. It needs either a cleared view or an actual camera
  relocation, plus a reliable enter/exit signal. No pilot-only visibility signal
  or follow-camera distance field has been identified in the inspected assets.

The next useful experiment is a separately staged visual cutaway, with a baseline
and rollback receipt, tested from the pilot seat, on foot, and beside another
player's walker. Inspect the exported geometry first. Do not change collision,
driver attachment or unnamed numeric engine fields to approximate a camera fix.

Current primary-source checks:

- [AT-TE author's page](https://www.nexusmods.com/helldivers2/mods/6396?tab=description)
  still describes a model/audio replacement, not a pilot camera control.
- [KoabraFang's Camera Mod](https://www.nexusmods.com/helldivers2/mods/9071)
  makes particular equipment invisible. Its equipment condition is not evidence
  of a local-pilot visibility mechanism.
- [Toggle HUD](https://www.nexusmods.com/helldivers2/mods/1) uses ShaderToggler,
  requires a separate manual bin installation, and documents unrelated models
  disappearing together. It is not an established isolated AT-TE solution.

The [historical investigation](archive/2026-09-12-session/AT-TE-CAMERA.md) records
the earlier FOV, camera-field and driver-attachment findings.
