# Empire & Clone Camera Pullback (`0.1.6`)

Per-player third-person follow-camera framing mod for **Helldivers 2** (Steam build `25480438`, **HellForge Mod SDK 16**).

`empire-camera-pullback` solves third-person dorsal-hull camera obstruction when piloting enlarged combat walkers (**Clonedivers AT-TE** and **EmpireDivers `2.28x` film-scale AT-ST**) and provides an optional, subtle third-person framing preset when wearing **Compact LEGO** armor kits—without altering weapon ballistics, projectile origin, or co-op simulation (`"gameplay": false`, `"coop": "per-player"`).

## Key Features (`0.1.4`)

- **Linux / Proton Fallback Hook & Hot-Prefix Camera Acquisition (`0.1.3`–`0.1.4`):**
  - Automatically falls back to `helldivers2.exe+0x1c6900` with a single-apply per-frame guard when `MinHook` returns `MH_ERROR_MEMORY_ALLOC (9)` at `+0x689bc0` under Proton, probes `units[0..7]` first with a local `250 µs` discovery budget for immediate post-Hellpod acquisition, and pins all `79` Empire & Clone Compact LEGO garments.
- **O(1) Subject Resolution & `< 50 µs` Render-Thread Budget:**
  - Resolves the local player's Helldiver `Unit*` directly in **O(1)** via `LocalUser -> PlayerManager -> EntityManager` descriptor `unit_ref -> LOOKUP_UNIT` (zero `world->units` full-array scans, even in `65,672`-unit Difficulty-11 missions, and zero possibility of co-op teammate avatar hijack).
  - Tracks spawned combat walkers in **O(1)** via `HF_ON_SPAWN` + `EntityManager` descriptors, with a strictly QPC-bounded (`<= 16` units/frame, `< 35 µs`) micro-sliced fallback for live-reload scenes.
  - Emits 30-second QPC health telemetry (`tick_us_avg`, `tick_us_max`, `profile`, `cam_ok`, `avatar_ok`, `walker_ok`, `applied_frames`, `raycast_hits`, `bypass_reason`) and enforces a hard `1000 µs` safety watchdog that fails closed if frame budget is ever exceeded repeatedly.
- **Occupied Walker Dorsal-Hull Clearance (`AT-TE` & Film-Scale `AT-ST`):**
  - Detects when the local player is actively piloting an `EXO-45 Patriot`, `EXO-49 Emancipator`, `EXO-51 Lumberer`, or `EXO-55 Breakthrough` walker (`+2.80 m` pullback, `+1.65 m` Z lift over the `3.90 m` AT-TE dorsal hull).
  - Automatically detects the `2.28x` film-scale `AT-ST` (`empire-atst-writer`) via cabin/pelvis world-space elevation (`> 3.50 m` above walker root) and applies `+4.10 m` pullback and `+3.35 m` Z lift (aligned with the `0.2.7` raised `Z=6.52 m` AT-ST head cab, with cab-focus look-up scoring and double-lift protection).
  - Ignores unoccupied parked walkers and teammates' walkers in 4-player co-op.
- **Compact LEGO 3P Framing Preset:**
  - Walks the local avatar's direct child attachment chain (`avatar + 0x1D8 -> child + 0x1E0`, `<= 32` children) and matches all `61` Empire & Clone Compact LEGO garment unit resources **plus** their exact scene-graph `node_count` (`17..47`), ensuring zero false positives on vanilla armors.
- **Havok Wall, Ceiling & Bulkhead Collision Clamping:**
  - Every frame's camera offset is cast through the engine's synchronous Havok physics raycast (`helldivers2.exe+0x77DEC0`, `damage_los_query` `0x80000004`) with a configurable `wall_safety_margin` (`0.28 m` default). If the physics raycast cannot be verified at runtime, the mod fails closed (`0.0 m` offset).
- **Fail-Closed Guards, Pause Drift Guard & Instant Bypass:**
  - Active **only** in `HF_STATE_MISSION` (and optionally `HF_STATE_SHIP` if `ship_walk_enabled = true` in `options.ini`) while the HellForge terminal camera is idle (`HfBrowseState.camera <= HF_BROWSE_CAMERA_IDLE`) and the local player (`LocalUser` across co-op slots `0..3`) is spawned and alive (`lifecycle == 3`, `health_life == 0`).
  - Instant `0.0f` bypass and frame-exact pose restoration on ADS / iron sights / scopes (`dist_to_head < 0.55 m` or `fov < 0.85 rad`), FirstPersonVR, stratagem/map zoom, Hellpod drop, vehicle/Pelican passenger seats, death/spectator camera, and extraction cutscenes, with unoverwritten-pose tracking so pause menus and high-refresh sub-ticks never accumulate camera drift.
  - Atomic `InterlockedExchange` teardown disarm on `HF_EVENT_SESSION_END`, `HF_ON_MISSION_END`, and `4 -> 5` mission-exit transitions (`helldivers2.exe+0x1E6B3E` race guard), plus full `GetLastError()` and SSE `MXCSR` preservation inside `StepDetour`.

## Configuration (`options.ini`)

Edit `options.ini` in the mod folder before launching:

- `walker_pullback_distance` / `walker_lift_z` — Pullback and vertical lift (metres) for standard walkers / AT-TE.
- `atst_scaled_pullback_distance` / `atst_scaled_lift_z` — Pullback and vertical lift (metres) for `2.28x` film-scale AT-ST.
- `lego_enabled` / `lego_pullback_distance` / `lego_lift_z` — Subtle framing preset for Compact LEGO kits (`0.00` for neutral).
- `wall_safety_margin` — Clearance (metres) maintained from walls and bulkheads (`0.28` default).
- `toggle_vk` — Optional Windows virtual-key code (e.g. `0x78` for `F9`) to toggle pullback on/off in mission.

## Building & Testing Offline

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-offline.ps1
```
