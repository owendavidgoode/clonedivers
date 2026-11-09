# Empire runtime features: offline scope and prototype

2026-11-08. Authority: `clonedivers-qa/docs/QA-SWEEP-2026-11-08.md`, this workspace's
`REPORT.md`, and the local HellForge source at 0.4.0-beta/d8a8b46. SDK 16, engine 0.1.47,
game build **25480438**. This is an offline prototype, not runtime acceptance.

Source-pin note: the local v0.4.0-beta tag resolves to merge commit c01f36b; installed
app-build.json names d8a8b46. A git diff from d8a8b46 to that tag is empty for all SDK
headers, readers, camera/bridge sources, mappings and build/package implementation used here.
Installed hellforge_mod.h, hellforge_globals.h and hellforge_components.h also hash-identically
to the source copies. No source-pin substitution is required.

| Feature | Simplest implementation on this pin | Disposition |
| --- | --- | --- |
| LEGO/small-body third-person pullback | Native render-step hook, using HellForge's existing terminal-camera research, plus qualified local body/ADS/vehicle guards | Mechanism exists; general gameplay scope is not qualified. No camera mod delivered. |
| Fighter for strafe, Bomber for bombing | Bridge-aware Lua: assign each distinct Eagle delivery's UnitComponentData model reference before spawning; preload both separate model closures | Delivery separation exists. Distinct resident Bomber/Fighter assets and model-reference replay remain unqualified. No TIE switching mod delivered. |
| Local LEGO Yoda death scream | Native SDK death event, read-only applied armor/health, bundled PCM through Windows PlaySoundW | **Built and packaged** as `clonedivers.empire-lego-yoda` 0.1.0. Non-positional local audio prototype. |

## Delivered files and checks

Source: `D:\ClaudeWork\forge-sdk\mods\empire-lego-yoda`.
Archive: `D:\ClaudeWork\forge-sdk\dist\empire-lego-yoda-0.1.0.zip`.
Compiled DLL also retained beside the source manifest: `empire_lego_yoda.dll` (83 KiB).
The source ZIP intentionally omits the DLL: HellForge builds source mods when installed.
The manifest explicitly sets `gameplay: false` and `coop: per-player`.

Files: `src/empire_lego_yoda.c`, `src/visual_gate.h`, `src/assets.h`, `src/passive_pins.h`,
`audio/classic-lego-yoda-death.wav`, `tests/yoda_test.c`, `hellforge-mod.json`, `README.md`,
`CREDITS.md`, `build-offline.ps1`. There is no Git repository in either assignment output
directory, so no branch/commit or push was made.

Reproduce all offline checks:

```powershell
& D:\ClaudeWork\forge-sdk\mods\empire-lego-yoda\build-offline.ps1
```

This script uses the actual `HellForge.Cli build-mod` and `package-mod --out
D:\ClaudeWork\forge-sdk\dist`, against a source copy under the mod's `build/workspace/mods`.
That isolated workspace holds copied pinned SDK headers, a read-only compiler junction and its
own `tools/zig-cache`; TEMP, TMP and Zig's local cache also stay under `build`. The ordinary
forge-sdk tools junction points into D:\HellForge, and the CLI normally writes its cache there;
the isolated workspace avoids that write. The source tree and staged sources are identical.

Results: **build-mod passed**, **100 C-host checks passed**, **package-mod passed** (10 files,
43 KiB ZIP). Logs are `build/build.log`, `build/tests.log`, `build/package.log`.
`runtime-feature-evidence.json` records inputs and output hashes. The C host never loads a
game DLL or plays audio. It uses fake game memory/audio and real archive reading/Win32 hashing.

Checks cover all four real r45 torso MAIN/GPU hashes; positive synthetic deaths in both kits
and body types; duplicate and remote events; human armor and LEGO helmet alone; stale observations;
unknown body/category; invalid map/descriptor/local peer; changed applied bytes; living/downed or
removed health records; failed playback consumed once; reinforcement; ship/mission transitions;
missing visuals; higher numeric patch overrides, cross-family ambiguity and changed proof files.
The launcher test suites were not run because launcher code did not change.

## Yoda: exact bindings and condition

SDK calls: `hf_on_game_loaded`, `hf_on_player_down` / `HF_ON_PLAYER_DOWN` (kind 5), `hf_on_state`,
`hf_on_game_thread`, `game_state`, `read`, `read_code`, `module_base`, `entity_of_network`,
`global`, `log`, `set_status`. No hooks, network sends, gameplay actions or game-memory writes.

HellForge's `native/mod-loader/event_sources.c` raises PLAYER_DOWN on a player's lifecycle
**3 -> 2**, not on an arbitrary hit or disappearance. `HfPlayerEvent.entity` is the player
entity, not the Helldiver avatar. The mod resolves those separately:

* SDK-validated `PlayerManager`: cell `game.dll+0x3326468`; count +0x84; peer per slot
  +0x2C8+0x38i; lifecycle +0x2E0+0x38i; descriptor pointer +0xE8+8i, player entity at +8;
  avatar network ID +0x3A8+0x20i, then `entity_of_network` (the loader's checked resolver,
  `game.dll+0xFD9BA0`).
* SDK-validated `LocalUser`: cell `game.dll+0x347CEF0`, local peer +0xB398.
* Applied customization follows pinned `native/mod-loader/passives.c`: manager cell
  `game.dll+0x33264F8`; record count +0x8E8 (max 4); entity map +0x930, capacity +0x938,
  empty +0x93C, multiplier +0x940; descriptor pointers +0x948, carried entity +8;
  applied record +0x96C+0x44i. Body type is +0, **body kit +0x0C**, helmet +4.
  Kit table pointer +0, count +8 (max 2048), kit ID +0, category +0x28 (must be 0).
  All 59 code pins from the pinned `passive_pins.h` are checked using pre-hook `read_code`
  before the reader acts. A mismatch logs the precise RVA and disarms the reader.
* SDK-validated `HealthManager`: cell `game.dll+0x3326688`; count +0x1020,
  descriptor pointers +0x1048 (entity +8), records pointer +0x1058, stride 0x1B8,
  life +0x19C. Life must be **>= 2 at death**, not merely downed (1). Missing corpse means skip.
  The validated descriptor index is cached; observations sample at most every 100 ms.

DP-40 / LEGO Stormtrooper kit **B513FD54** and AF-02 / LEGO Bikini Stormtrooper kit
**E9ADD047** are native kit IDs. They alone do not prove LEGO visuals. The mod reads the
current game's `bin/../data/*.patch_*` at startup and verifies the effective torso UNIT
MAIN and GPU payloads against `expected-resources.json` from r45-v3:

| Kit | Body | Torso UNIT |
| --- | --- | --- |
| B513FD54 | Brawny (0) | bb11c8041c14c747 |
| B513FD54 | Lean (1) | 003cc8a527711da5 |
| E9ADD047 | Brawny (0) | c1141b792c1b621a |
| E9ADD047 | Lean (1) | 09ad5784b873e58f |

UNIT type is e0a48d0be9a7453f. Archive reader uses the existing 72-byte header,
32-byte type rows and 80-byte resource rows. It respects numeric patch priority within a
family and refuses ambiguous cross-family providers or malformed archives. It enables each
body variant independently, and reports how many of four qualified. No appearance pack means
no eligible variant, even if the player wears vanilla DP-40/AF-02. Hashing binds torso identity,
not the full visual dependency closure or rendered appearance; those remain pack acceptance work.

During a mission it retains only a readable local living avatar with a qualifying applied body.
A death must match peer/player, a <=500-ms observation, unchanged 68-byte applied record and
record address, and the cached avatar's native dead health state. File identity, size and write
time of the selected proof are checked again. Each peer/avatar/network tuple consumes at most
one playback attempt, even on playback failure. Mission transitions clear observations.

## Audio

Bundled WAV copied byte-for-byte from the existing Empire library:
`C:\Users\goode\OneDrive\Desktop\clonedivers\dist\empire-voices-2026-11-01\library\previews\classic-lego-yoda-death.wav`.
SHA-256 `f5f49f73e435113295fef77add8c4941b2b6fd7f0c1958ba1a8b3a9d038f4cef`;
mono PCM, 11025 Hz, 16 bit, 14705 frames / 1.334 s. The legacy authored WEM is at
`dist/empire-yoda-death-2026-10-08/audio/candidate-v1/classic-yoda-death.wem`;
original and decoded WAVs remain in that area's `source-v1`. Credits are preserved in the mod.

The native mod hash-checks the WAV at startup and retains it for asynchronous
`winmm.dll!PlaySoundW(SND_MEMORY | SND_ASYNC | SND_NODEFAULT | SND_NOSTOP)`.
The sound is additive, non-positional and local to this process. It does not replace death
voices or need an authored Wwise bank to become resident. It bypasses the game's volume/mute
and spatial mix. Windows/process audio capture and listening are required; this is a prototype
tradeoff, not a port of the legacy positional Wwise adapter. An API success proves submission,
not audible output. A competing PlaySound can refuse the attempt, which remains consumed.

## Camera: available mechanism, unresolved safe scope

Pinned HellForge `native/mod-loader/terminal_camera.c` supplies more than the legacy dormant
adapter: an already researched active camera/render timing path. Exact **EXE** RVAs:
render step **0x689BC0**, main world **0x317DE0**, unit lookup **0x9D8C0**,
set_local_pose **0x1FCAD0**, update_unit **0x1B2DC0**, render_world binding **0x3F7E90**,
render-pose copy **0x3181D8**. World unit count/array is +0x2305B8/+8;
last render pose +0x230920; camera unit scene-graph node-0 pose +0x20. The terminal mod
checks instruction pins and moves the camera *before* the boot script render step.
`hook_ex` could chain a client-only mod there; these functions are not SDK-16 named mappings.

Legacy `r26-vehicle-camera-entry.lua` wraps `stingray.Application.render_world` and uses
`Camera.world_position/local_position/world_pose/set_local_position`, `Unit.camera`, and
`Matrix4x4.forward`. Its policy relies on a known false `ModOptionsMenu` first-person setting,
an exact local-avatar/head association, stance/vehicle animation layers and identity transforms.
Those are not established by the harness's title/ship/mission commands or the terminal-camera
proof. Applied LEGO armor is now readable, but that does not identify ADS, camera ownership,
collision clearance, seats or cutscenes. Moving the camera based on distance/FOV alone would
not meet the requested inert-outside-condition rule. No camera DLL was fabricated.

Next qualification: an observer at the pinned render step must record the active camera/unit,
local avatar, applied kit, ADS/first-person and vehicle transition state. Prove that association
on LEGO and human bodies, third person/ADS, ship/interior, AT-ST entry/exit and death/spectator
views. Only then enable a modest local pullback and compare same-pose screenshots; no bones/FOV
edits or broad camera hooks should substitute for those guards.

## TIE: separate deliveries exist; residency remains the blocker

The legacy gateway's generated `gateway-build-v3/profile.json` identifies distinct entities on
25480438 (historical record indices below are evidence, not hardcoded live addresses):

| Delivery | Entity resource | Unit record / Eagle record |
| --- | --- | --- |
| Strafing | 23a60681dd4383ec | 327 / 0 |
| Airstrike | 2ea01cb1676aca29 | 1154 / 1 |
| Smoke | 1b3bcadabc7ef8d6 | 1155 / 2 |
| 500kg | e44b691dc039a505 | 1156 / 3 |
| Cluster | 9d4f7cb4eb34515d | 1157 / 4 |
| Gas | dbb286ad7ed9df96 | 1158 / 5 |
| Napalm | 27bb558c893383cc | 1161 / 8 |
| 110mm | 397792815583da29 | 1162 / 9 |

UnitComponentData (88), stride 136, has the model resource at **+0x00/u64**; native Eagle
model **4e401cc877093220**. EagleComponentData (320), stride 152, owns delivery/attack
controls. `HF_ON_STRATAGEM` originates at mapped `ActivateStratagemBeacon`,
**game.dll+0x6ABDB0**; `HF_ON_SPAWN` originates after mapped `SpawnEntity`,
**game.dll+0xFDC140**. A spawn notification is too late to assume it can replace an existing
mesh. Switching one global model at beacon activation is also unsafe for overlapping sorties.

The simpler eventual solution is assign Fighter to the *strafing delivery definition* and
Bomber to each intended bombing definition before any spawn. Both models must coexist under
distinct resource IDs. `hellforge_lua_set(mod, "UnitComponentData", target, 0, HF_TYPE_U64,
0, model, flags)` is the bridge's raw aligned-field route (`lua_bridge.c` permits any aligned
record value); native SDK named Unit fields expose only scale_min +8 and radius +16. A
bridge-aware HD2Runtime can supply the ownership/replay layer; the old guarded transaction
must not write directly over HellForge. `hellforge_lua_load_package` / `hf->load_package`
and `HF_PACKAGE_RESIDENT` cover package requests, not proof of arbitrary custom model closure.

The legacy native Eagle package is **2c26bc4c6592fa14**,
`packages/generated/loadout/eagle_base`. Its private aircraft trial cloned Fighter
**8bfc3e7b58cda44b** and **Interceptor** 686e5e936d3886fe, not Bomber; its report explicitly
leaves private physics-key runtime resolution unverified. The r45 candidate replaces the
shared native Eagle with the session-wide selected Fighter. It does not establish a second
resident Bomber model. The legacy gateway itself permits only a solo Ship strafing-model
comparison and asserts all other delivery controls remain unchanged. A bridge setter alone
does not close those asset/residency gaps.

Next work: qualify a separate Bomber clone with unchanged compatible physics/material closure,
include both in an engine-loadable package, prove package residency and each delivery record's
ownership on the pinned build, then assign definitions through the bridge. Future live test:
alternate strafe/airstrike, overlap both in flight, repeat after return/rearm and world reload,
and observe smoke/napalm/cluster/500kg/110mm independently. Log delivery entity, chosen model
and residency; timed screenshots/video must show the correct silhouettes simultaneously.
No current TIE ZIP claims to implement this.

## Orchestrator live-test handoff (not executed here)

Use only **clonedivers-armory**. One session at a time. Before staging any ZIP, use the shared
closed-session checks (`HellForge.Cli status`, no helldivers2.exe or HellForge.exe), timestamped
backup, atomic install/settings changes and post-write verification. Install only one copy of
the mod ID, either ZIP or extracted source. Enable r45-v3 as the sole appearance pack, plus the
usual gameplay preset. Preserve settings for restore. This worker made no D:\HellForge changes.

```powershell
$py = 'C:\Users\goode\AppData\Local\Programs\Python\Python313\python.exe'
$harness = 'D:\ClaudeWork\bingus-harness\harness.py'
$env:HD2H_HOME = 'D:\ClaudeWork\harness-home'
$env:HD2H_LOADER_ZIP = 'D:\ClaudeWork\releases\Bingus-Shared-Loader-v20.zip'
$env:HD2H_HELLFORGE_PROFILE = 'clonedivers-armory'
$hfArgs = @('--target','hellforge','--hellforge-root','D:\HellForge','--profile','clonedivers-armory')
& $py $harness @hfArgs preflight
& $py $harness @hfArgs start --preset private
& $py $harness @hfArgs ship
& $py $harness @hfArgs mission --difficulty 3 --faction terminid --shots
& $py $harness @hfArgs shot lego-before-death
# Attended confirmed local death; capture process audio/video and a death screenshot.
& $py $harness @hfArgs shot lego-after-death
& $py $harness @hfArgs leave
& $py $harness @hfArgs stop
```

Check each command's exit before advancing. Use `private`, not the muted `light` preset.
Preselect kit/body in the lab profile before each drop. The generic `drop_loop.py` proves
survival and transitions, not a particular equipped kit or controlled death. The agent
supports Lua commands, but no new equip/kill binding was qualified here; do not invent one.

Acceptance in `mod-clonedivers.empire-lego-yoda.log`:

1. `armed: r45 torso variants=4/4` (or explicitly account for missing variants).
2. `eligible local avatar=... network=... kit=B513FD54|E9ADD047 body=0|1` while alive.
3. On death, exactly one `yoda attempt ... result=started` and one audible scream. No repeats
   while waiting to reinforce. `result=refused` is a playback failure, not acceptance.
4. Reinforce and die again: a new avatar/network tuple, one new attempt.
5. Repeat both LEGO kits and both body types. Confirm armor in screenshots, not just its name.
6. Human body with LEGO helmet: zero attempts. Human body/helmet: zero. Remote player's
   LEGO death: `skip remote death`, zero local sound. Remote acceptance requires a second player.
7. Ship, return, abandon, missing corpse and spectator transitions: no new attempts. A death
   with a removed corpse may be logged as a skip; that false negative is intentional.
8. Restart without r45 (r19/control or no Empire): `off: ... torso variants=0/4`; vanilla
   DP-40/AF-02 deaths must remain silent.

Retain run JSON/screenshots, the mod log and modloader.log, and unmuted process audio/video.
The camera/TIE sections above describe qualification scenes only; neither feature is enabled
by the delivered Yoda ZIP. Record them as pending, not passing because the drop loop survives.

## Remaining risks and work boundary

No game launches, HellForge installation writes, old-checkout writes/builds, Steam game-folder
changes, system settings, public feeds, releases, messages or pushes were performed. Only
workspace source, build caches, fixtures, report/evidence and the dist ZIP were written.

Actual death-event ordering, corpse retention and audible Windows playback require live tests.
The 500-ms guard can intentionally miss extremely short-lived/unreadable avatars. Existing
other Yoda death addons must be off to avoid two independent sounds. The legacy positional
event is not posted by this mod. The startup torso scan is not a general runtime asset loader:
restart for every appearance change; newly inserted patches/live pack replacement are
unsupported. File metadata checks are ordinary change detection, not an adversarial tamper
proof. The r45 pack's own unresolved helpers and visual acceptance remain separate blockers.
