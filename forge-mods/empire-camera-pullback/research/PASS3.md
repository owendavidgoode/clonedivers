# Camera Pullback Pass 3 — Offline Disassembly & Architecture Qualification

- **Date:** 2026-11-21
- **Target Build:** Helldivers 2 Steam build `25480438` (`helldivers2.exe` + `game.dll`), HellForge `0.4.0-beta` (`Mod SDK 16`)
- **Mod Target:** `clonedivers.empire-camera-pullback` `0.1.0` (`gameplay: false`, `coop: "per-player"`)
- **Verification Level of Pass 3 Research:** Offline DbgEng disassembly (`offline_disasm.exe`) against complete live-captured Ship (`state 3`) and Mission (`state 4`) PE memory sections + cross-reference against live-proven `CowboyBingus/FirstPersonVR` (`0BSD`, build `25480438`) and HellForge `0.4.0-beta` `terminal_camera.c` / `event_sources.c` / `event_polls.c`. No `helldivers2.exe` process was launched in this session.

---

## 1. Complete Capture Verification (`013F6000` Gap Closed)

Pass 2 (`2026-11-09`) was blocked in part because the initial 7-file observer capture omitted the unnamed read-only PE section at `helldivers2.exe+0x013F6000` (`virtual size = 4,831,658` B, flags `0x40000040`), which contains the Stingray `Unit` / `Camera` vtable at `exe+0x1676D80`.

Offline verification of `camera-capture-ship\capture.tsv` (`state 3`) and `camera-capture-mission\capture.tsv` (`state 4`, PID `34276`, `exe_base = 0x00007FF648DF0000`, `game_base = 0x00007FFBA58D0000`) confirmed **all 26/26 captured section files per state (`missing = 0`, 100% SHA-256 match)**:

| Module | Section RVA | Bytes | Missing | Verified SHA-256 (`camera-capture-mission`) | Purpose |
|---|---:|---:|---:|---|---|
| `helldivers2.exe` | `0x00001000` | `20,919,884` | `0` | `cb6f713c5e679398fe9a63e9b8bdf4b9bcb1e66557ae99f12925d66f1d67bb6e` | Engine `.text` (`CAMERA_STEP`, `SET_LOCAL_POSE`, `UPDATE_UNIT`, `RAYCAST`, `0x1E6B3E`) |
| `helldivers2.exe` | `0x013F6000` | `4,831,658` | `0` | `6681c47c5ee3ac336ec35c6169301c79c3f4c057ed71ec89a5c808a157330f6f` | Unnamed read-only data holding `Unit` vtable `exe+0x1676D80` (`+0x280D80`) |
| `helldivers2.exe` | `0x027CF000` | `1,093,476` | `0` | `c5de8da9bcedc78a43339952bbe029b7606e6c1bdad2f7d23b5dc2409fad52ca` | Engine `.rdata` constants |
| `game.dll` | `0x00001000` | `34,667,155` | `0` | `7834281031c0d1eb13f628a022f93e95dcb150d17c8774c114187e0e8d11239f` | Gameplay `.text` (camera FOV wrapper, `PlayerManager`, `HealthManager`, `ScriptApi` callers) |
| `game.dll` | `0x02111000` | `5,417,484` | `0` | `7581b1f37d00cb7a742484fdde6aedc7cce081faf64086608b62214a67b945ff` | Gameplay `.rdata` vtables & constants |
| `game.dll` | `0x037D8000` | `1,832,304` | `0` | `784a604567e09dd7cef4ad11b09f4b397ff13d7c507a2b6c3414fbaa656fc23d` | Secondary read-only tables |

*(Note: Writable `.data` sections `helldivers2.exe+0x01892000` and `game.dll+0x0263C000` are intentionally marked `excluded` in `capture.tsv`; all runtime globals in `.data` are resolved via instruction-verified RIP-relative displacements in `.text`.)*

---

## 2. Disassembled Engine Camera & Scene-Graph Pipeline (`helldivers2.exe`)

### 2.1 `Unit` Vtable (`exe+0x1676D80`, file offset `0x280D80` in `013F6000.bin`)
- `vtable + 0x0E8 -> exe+0x2BD870`: `48 8D 41 60 C3` (`lea rax, [rcx+60h]; ret`) — returns embedded `SceneGraph*` (`unit + 0x60` for standard units):
  - `graph + 0x10`: `uint32_t node_count` (`<= 4096`)
  - `graph + 0x18`: `const float* linked_parent_world_poses` (`World.link_unit` parent matrix array)
  - `graph + 0x20`: `float* local_poses` (`16` floats = `64` bytes per node: `right[3]`, `forward[3]`, `up[3]`, `position[3]` at `[9..11]`, `scale/pad[4]` at `[12..15]`)
  - `graph + 0x28`: `float* world_poses` (`4x4` row-major `16` floats = `64` bytes per node: `right[4]`, `forward[4]`, `up[4]`, `position[4]` at `[12..14]`)
  - `graph + 0x40`: `uint32_t* node_names` (`IdString32`, high 32 bits of `murmur64a(name, seed=0)`; `"head"` = `0x8C5570C9`, index `90` on Helldiver `0x4D1C334D294DFA97`)
- `vtable + 0x160 -> exe+0x2BD950`: `8B 81 10 01 00 00 C3` (`mov eax, [rcx+110h]; ret`) — camera count on `Unit*`.
- `vtable + 0x168 -> exe+0x2BD960`: `48 8B 81 18 01 00 00 8B D2 48 8B 04 D0 C3` (`mov rax, [rcx+118h]; mov edx, edx; mov rax, [rax+rdx*8]; ret`) — returns `Camera*` at index `rdx`.
- Child attachment chain on `Unit*` (verified in `lego-compact-leaf-writer\src\coop_discovery.h:477-490`):
  - `unit + 0x1D8`: first linked child `Unit*`
  - `child + 0x1E0`: next sibling `Unit*`
  - `[[unit + 0x30]]`: `uint64_t` Stingray unit resource hash (`Unit.resource_name`, `exe+0x40BD50`).

### 2.2 `Camera` Object & `CameraApi` (`exe+0x1CD09A..0x1CD2AE` -> `exe+0x27C9800..0x27C9928`)
Disassembling `exe+0x30D940..0x30DF10` proves the `Camera` object layout:
- `camera + 0x18`: `SceneGraph*` (`exe+0x30DE70`: `mov rax, [rcx+18h]`)
- `camera + 0x20`: `int32_t node_index` (`0` for the main world's camera unit; `movsxd rdx, dword ptr [rcx+20h]; shl rdx, 6`)
- `camera + 0x28`: `float near_range` (`0.05f`, getter `exe+0x30D940`: `movss xmm0, [rcx+28h]; ret`)
- `camera + 0x2C`: `float far_range` (`5000.0f`, getter `exe+0x30D950`: `movss xmm0, [rcx+2Ch]; ret`)
- `camera + 0x30`: `uint32_t mode` (getter `exe+0x30D9B0`: `mov eax, [rcx+30h]; ret`)
- `camera + 0x34`: `float vertical_fov` in radians (unzoomed 3P gameplay = `0.9599f` rad / `55°`; getter `exe+0x30D980`: `movss xmm0, [rcx+34h]; ret`; setter `exe+0x30D990`: `movss [rcx+34h], xmm1; mov dword ptr [rcx+50h], 0; jmp exe+0x4CFAB0`).

### 2.3 Render-Step Hook & Pose Commit (`helldivers2.exe`)
All five engine entry points match both `terminal_camera.c` and `FirstPersonVR/src/common/fpv_camera.c` byte-for-byte on build `25480438`:
1. `CAMERA_STEP` (`exe+0x689BC0`):
   - Pin (`32` B): `48 83 EC 58 33 C0 4C 8D 05 EB 71 00 01 48 89 44 24 30 48 8D 54 24 40 89 44 24 38 48 81 C1 90 01`
   - Dispatches boot script `render()` (`exe+0x1C6900`), which invokes `Application.render_world` (`exe+0x3F7E90 -> exe+0x318150`) and copies the camera unit's world pose (`graph+0x28 + node*64`) into `world + 0x230920..0x230950` (`WORLD_RENDER_POSE`).
2. `SET_LOCAL_POSE` (`exe+0x1FCAD0`, `UnitApi + 0x60` at `exe+0x27C8F70`):
   - Pin (`32` B): `48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 49 8B D8 48 63 FA E8 D6 0D EA FF 48 8B C8 48 8B F0`
   - Signature: `void (*)(uint32_t unit_ref, int32_t node, const float local_pose[16])`
3. `UPDATE_UNIT` (`exe+0x1B2DC0`):
   - Pin (`32` B): `48 89 5C 24 08 48 89 74 24 10 48 89 7C 24 18 55 41 54 41 55 41 56 41 57 48 8D AC 24 30 F8 FF FF`
   - Signature: `void (*)(uintptr_t world, uintptr_t unit)` — propagates `graph+0x20` local poses into `graph+0x28` world poses for this frame's render.
4. `LOOKUP_UNIT` (`exe+0x9D8C0`):
   - Pin (`32` B): `48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 48 8B 35 1A 28 97 01 8B D9 48 8D 8E D0 00 00 00 FF`
   - Signature: `uintptr_t (*)(uint32_t unit_ref)`
5. `MAIN_WORLD` (`exe+0x317DE0`):
   - Pin (`32` B): `48 89 5C 24 08 48 89 74 24 10 48 89 7C 24 18 48 8B 05 12 84 6F 01 33 C9 0F 57 C0 8B D1 8B D9 44`
   - Signature: `uintptr_t (*)(void)` (`world + 0x2305B8` = `uint32_t unit_count`, `world + 0x2305C0` = `Unit** units`, `world + 0x230920` = `float render_pose[16]`).

---

## 3. How `game.dll` Drives the 3P Camera & Why Post-Solve + Raycast Beats Raw `.data` Mutation

Scanning all `18,724` `ScriptApi` (`*(game.dll+0x3326308)`) and `2,135` secondary plugin table (`*(game.dll+0x3326318)`) references in `capture-game.dll-00001000.bin` established:
1. `game.dll+0x11ECF20..0x11ED29B` wraps vertical FOV updates (`*(game.dll+0x3326318) + 0xA0 -> exe+0x30D990`, multiplying degrees by `*(game.dll+0x23C77F0)` = `pi/180`), while **`CameraApi->set_local_pose` (`exe+0x30DEB0`) has `0` call sites anywhere in `game.dll`**.
2. Instead, `game.dll` maintains its private third-person camera rig inside writable `.data` (`0x0263C000`, `18.46 MB`) and writes the solved camera transform to **node `0` of the main world's camera `Unit*`** via `UnitApi->set_local_pose` (`[ScriptApi + 0x18] + 0x60 -> exe+0x1FCAD0`) every frame **before** `CAMERA_STEP` (`exe+0x689BC0`).
3. Crucially, as confirmed live by `FirstPersonVR` (`docs/findings.md` L55–58), **`game.dll` computes weapon aim and projectile trajectories from its own internal third-person camera state and never reads back the engine camera's `Unit*` pose**.
   - **Consequence 1 (Aim & Reticle Integrity):** If a mod mutated `game.dll`'s internal spring-arm distance in `.data`, it would move the ballistic ray origin backward and shift where shots and lasers land! Filtering the camera unit's node-0 pose at `CAMERA_STEP` (`exe+0x689BC0`) moves **only** the rendered viewport while keeping `game.dll`'s ballistic aim, reticle projection, and audio listener untouched.
   - **Consequence 2 (Automatic Frame-Exact Restoration):** Because `game.dll` overwrites node `0` of the camera unit from its clean internal state on every frame before `CAMERA_STEP`, returning early (`changed == 0`) from `StepDetour` immediately restores the engine's exact unmodified pose on the very next frame with zero residual drift.
   - **Consequence 3 (Fail-Closed Gate on Experimental Spring-Arm):** Because `game.dll` `.data` (`0x0263C000`) is uncaptured in offline PE dumps, no raw `game.dll` spring-arm offset is touched; `options.ini` exposes `allow_experimental_spring_arm = false` (fail-closed no-op even if set true in `0.1.0`, reserved strictly for future live-probed experiments).

---

## 4. Synchronous Havok Collision Raycast (`helldivers2.exe+0x77DEC0`)

Disassembling `helldivers2.exe+0x77DEC0..0x77DF68` (`*(game.dll+0x3326328) + 0x68`) and query allocator `exe+0x77DE60..0x77DEB6` confirms the exact synchronous physics raycast contract:
```asm
; exe+0x77DEC0 (pin: 48 8B C4 48 89 58 10 48 89 70 18 57 48 83 EC 70 8B D9)
mov     rax, rsp
mov     [rax+10h], rbx
mov     [rax+18h], rsi
push    rdi
sub     rsp, 70h
mov     ebx, ecx                     ; ebx = query_id (0x80000000 | row)
...
lea     r9, [exe+0x27B9440]          ; 20-byte query descriptor table
mov     eax, ebx
btr     eax, 1Fh                     ; clear bit 31 -> row index
lea     rcx, [rax+rax*4]             ; rcx = row * 5 dwords (20 bytes)
...                                  ; loads 5 filter dwords [r9+rcx*4 + 0x00..0x10]
call    exe+0x7F4590                 ; synchronous Havok world raycast
```
- **Signature:** `int (*RaycastFn)(uint32_t query, const float from[3], const float dir[3], float length, int zero, float result[16], int closest)`
- **Verified Query Row:** Row `4` (`query = 0x80000004u`), verified at runtime before first use by reading `*(uint32_t*)(exe_base + 0x27B9440 + 4 * 20 + 12) == 0x2315515Du` (`damage_los_query`, mode `1` = closest hit; `result[0..2]` = world hit XYZ).
- **Wall / Bulkhead / Ceiling Clamp Geometry:**
  - `game_pos = (game[9], game[10], game[11])` has **already** been solved by `game.dll`'s native camera collision this frame to lie outside walls, ceilings, and the subject's Havok collision hull.
  - Given desired offset $\Delta \mathbf{p} = -\mathbf{f}_{\text{cam}} \cdot d_{\text{pullback}} + (0, 0, z_{\text{lift}})$ with $L = \|\Delta \mathbf{p}\| > 0$ and unit $\hat{\mathbf{u}} = \Delta \mathbf{p} / L$:
    - Cast `RaycastFn(0x80000004u, game_pos, u, L + wall_safety_margin, 0, result, 1)`.
    - On hit (`hits > 0`), compute $d_{\text{hit}} = (\mathbf{p}_{\text{hit}} - \mathbf{p}_{\text{game}}) \cdot \hat{\mathbf{u}}$ and clamp travel to $\max(0.0, \min(L, d_{\text{hit}} - \text{wall\_safety\_margin}))$.
    - If `RaycastFn` cannot be verified (`CheckRaycast() != NULL`), `empire-camera-pullback` **fails closed** (`0.0f` pullback) so it can never push the camera through geometry.

---

## 5. Subject Profiles & Teardown Race Guard (`helldivers2.exe+0x1E6B3E`)

### 5.1 Walker Dorsal-Hull Profile (`AT-TE` & Film-Scale `AT-ST`)
- **Target Walker Unit Resources (`4` Exosuit / Combat Walker variants):**
  - `0x79E4B3D2DA5E45E3` (`EXO-45 Patriot` — Empire `AT-ST` & Clones `AT-TE`)
  - `0xC2D449ECF7FACAB1` (`EXO-49 Emancipator` — Clones `AT-TE`)
  - `0x7B2326F6FD9C8069` (`EXO-51 Lumberer` — Clones `AT-TE`)
  - `0x35DBF54F016F3624` (`EXO-55 Breakthrough` — Clones `AT-TE`)
- **Metric Geometry (`AT-TE-CAMERA.md` L121–125 & `atst_asset_pins.h` L244, L444):**
  - Vanilla `EXO-45` rig top sits at `Z ≈ 2.55 m`.
  - Clonedivers `AT-TE` dorsal hull rises to `Z ≈ 3.90 m` (`X ∈ [-3.7, +0.2]`), placing the vanilla occupied camera inside/behind the upper hull. Default `walker_pullback_distance = 2.80 m`, `walker_lift_z = 1.65 m` lifts the camera cleanly over the `3.9 m` dorsal roof.
  - EmpireDivers film-scale `AT-ST` (`empire-atst-writer`, `2.28x` stride) elevates node `13` (`pelvis`/cabin) from `2.06 m` to `4.70 m` (`top ≈ 5.83 m`). When `world_pose(13).z - walker_root.z > 3.60 m`, `empire-camera-pullback` automatically applies `atst_scaled_pullback_distance = 3.80 m`, `atst_scaled_lift_z = 2.65 m`!
- **Strict Local-Pilot Occupancy Proof (Rejects Parked & Teammate Walkers):**
  - Walker `root` must be in front of the camera (`dot(walker_center - cam_pos, cam_fwd) > 0.45f`), camera-to-walker distance in `[1.2 m, 14.0 m]`, **AND** the local player (`lifecycle == 3`, `health_life == 0`) must be seated inside/piloting **this** walker (`|helldiver_root - walker_root| <= 3.6 m` or `helldiver` linked to `walker`, while no closer on-foot local Helldiver stands separate from the walker).

### 5.2 Compact LEGO Framing Profile (`61` Garments)
- Matches the exact `61` Compact LEGO 3P garments (`7` Empire baked IDs in `pinned_data.c` + `54` Clone IDs in `lego-garments.json`) attached to the local Helldiver's child unit list (`avatar_unit + 0x1D8 -> +0x1E0`), verifying **both** `uint64_t resource_id` **and** `graph->node_count == expected_node_count` (`17..47` nodes) so vanilla armors sharing base resource IDs never false-trigger when LEGO `.patch_*` archives are not installed.

### 5.3 Mission-Exit Teardown Race (`helldivers2.exe+0x1E6B3E`)
Disassembling `helldivers2.exe+0x1E6B2D..0x1E6B48` shows:
```asm
00007ff6`48fd6b3a add    rdx, qword ptr [rax+18h]   ; graph->linked_parent_world_poses
00007ff6`48fd6b3e movups xmm6, xmmword ptr [rdx]    ; AV when parent world poses free during 4 -> 5
```
To eliminate any extra `UPDATE_UNIT` (`exe+0x1B2DC0`) call or stale `Unit*` read during mission-exit world teardown (`HF_STATE_MISSION [4] -> HF_STATE_PREPARE_SHIP [5]`), `empire-camera-pullback` subscribes at high priority to `HF_EVENT_SESSION_END`, `HF_ON_MISSION_END`, and `HF_ON_STATE`, atomically setting `InterlockedExchange(&g_disarmed, 1)` and purging cached world/unit handles the instant `from == HF_STATE_MISSION` or `SESSION_END` fires, re-checking `hf->game_state(mod)` inside `StepDetour`, and preserving `GetLastError()` and SSE `MXCSR` (`_mm_getcsr` / `_mm_setcsr`) across every detour invocation.
