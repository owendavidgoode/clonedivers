Chaos Invasions 0.2.0 (`clonedivers.chaos-invasions`) is playable cross-faction
Chaos Mode for HellForge SDK 16 / Helldivers 2 build 25480438.

Features:
- Fail-closed 32-byte code pins (`hf->read_code`) verifying 10 native game.dll
  functions (`0x953150`, `0x953580`, `0x952EB0`, `0x8B61F0`, `0x8B5C80`,
  `0x13C6D30`, `0xBE1340`, `0x4F8AF0`, `0x12FE350`, `0x12FE680`) before calls/writes.
- All-peer curated foreign package preloading in `HF_STATE_MISSION` (game thread):
  detects mission faction from `[director+0x518BC]`, preloads base faction packages,
  transport units (`0xDB90077E76FAA025` Dropship / `0x74E2285C01DA4F71` Warp Ship),
  and packages of up to 6 curated non-boss infantry/medium units per foreign faction
  via `0x4F8AF0` and `0x12FE350` while keeping the ref map `<75%` full and gating
  readiness on `ArePackagesIdle(NULL)` (`0x12FE680`) + engine part `state==4`
  (`parts <= 1024`).
- Host/Solo bounded wave spawner respecting `0x952EB0` headroom, `total_cap`,
  `faction_cap`, `mission_budget`, and `0x8B5C80` ground anchor projection (`38..62m`
  from anchor player, `>=28m` from all living players, `|dz| <= 25m`):
  * Automatons (`CHAOS_BOTS`): `0x8B61F0` Dropship (`0xDB90077E76FAA025`) + smoke flare FX,
    with 12s fallback to staggered `SpawnRosterRow(..., flags=0x3C)` if transport
    slots are busy or yield 0 units.
  * Illuminate (`CHAOS_SQUIDS`): scoped `[director+0x518BC]=4` across synchronous
    `0x8B61F0(..., 0x74E2285C01DA4F71)` (immediately restored), with 12s fallback to
    staggered `SpawnRosterRow(..., flags=0x3C)`.
  * Terminids (`CHAOS_BUGS`): `TriggerParticleFx(0x7B)` + `TriggerBreachBurst(0x7B)` +
    staggered `SpawnRosterRow(..., flags=0x24, kind=3)` over ~3.5s.
- Host/Solo 250ms mutual-hostility perception narrowing on `BHV_PERCEPTION` (`+0x13E8`,
  stride `0x13F8`) leaving `BHV_FACTIONS` (`0x3326CB8`) untouched so players, SEAF,
  Sentries, and up to three enemy factions fight in 3/4/5-way battles.

Kill switch: `chaos.stop` in the staged mod folder (`hf->mod_folder`), latched until
session restart. Menu bridge: `%LOCALAPPDATA%/Clonedivers/ChaosInvasions.values`.
Build & test: `powershell -NoProfile -ExecutionPolicy Bypass -File build-offline.ps1`.
