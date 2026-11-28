# AT-TE Titanfall Tactical Cockpit HUD (`clonedivers.titanfall-walker-hud` `0.1.0`)

Client-side Titanfall-style tactical walker cockpit visor HUD and optional `F7` first-person forward-canopy / third-person tactical dual-view toggle for Clonedivers AT-TE combat walkers (`EXO-45` `0x79E4B3D2DA5E45E3`, `EXO-49` `0xC2D449ECF7FACAB1`, `EXO-51` `0x7B2326F6FD9C8069`, `EXO-55` `0x35DBF54F016F3624`, plus modded walker copies).

- **Purely visual & co-op safe:** `"gameplay": false`, `"coop": "per-player"`, `"sdk": 17` (compiled against HellForge SDK v20 for Helldivers 2 build `25480438`). Zero archive asset overrides and zero projectile/aim/network mutations.
- **AT-TE vs AT-ST smart gating:** Automatically activates on `198`-node Clonedivers AT-TE walkers while leaving `106`-node Empire `2.28x` AT-ST walkers uncluttered by default (`enable_on_atst = false` in `options.ini`).
- **Titanfall Tactical Visor (`HfScreen` / `HfDraw`, D3D11/D3D12 SDR & HDR):**
  - `0.55 s` optical scanline boot-up sequence (`ROTHANA HEAVY ENG. // AT-TE TACTICAL OS v2.3`) and `0.20 s` visor power-down on disembark.
  - Aspect-adaptive (`16:9`, `21:9`, `32:9`) armored canopy bezels & angled Titanfall A-pillar struts.
  - Clipped `000..359` compass tape with `N/NE/E/SE/S/SW/W/NW` markers and chassis-nose vs camera-aim azimuth triangle.
  - Live hull integrity bar & `< 30%` critical hull warning (`HfHealthManager`).
  - Center pitch ladder (`-30..+30 deg`) with a `96`-unit center reticle deadzone so native weapon convergence rings stay unobstructed.
  - Lower-left 6-leg AT-TE hexapod schematic, ground velocity (`M/S`, `HfMotionManager`), and optics mode readout.
  - Lower-right live weapon bay round counters & bars (`HfMountManager` + `MagazineRounds` / `MagazineCapacity`).
- **Optional `F7` Dual-View (`1P CANOPY` / `3P TACTICAL`):**
  - Toggles between standard 3P tactical walker follow camera (compatible with `empire-camera-pullback`) and a Havok-raycast-clamped 1P forward-canopy viewport, restoring unmodified base poses immediately on toggle, disembark, or mission exit.
