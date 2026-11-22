# Dedicated Melee Slot (`1.5.2-clonedivers.2`)

Carry a secondary firearm **and** an extra melee weapon at the same time on **Helldivers 2** build `25480438` (**HellForge Mod SDK 16**).

This `1.5.2-clonedivers.2` release is a Clonedivers 4-player co-op hardened build of [`vibeslacker/melee-weapon-slot-test`](https://github.com/vibeslacker/melee-weapon-slot-test) `v1.5.2` under the **MIT License**.

## Features & Co-op Hardening (`1.5.2-clonedivers.2`)

- **Unified HellForge Package (`patch_920`):** Ships both the native SDK-16 C11 DLL (`melee_weapon_slot.dll`) and the loadout-card Melee Picker Lua addon (`data/9ba626afa44a3aa3.patch_920`) in one mod archive, avoiding `.patch_*` collisions with Bingus Shared Loader (`patch_0`), Clonedivers (`patch_0..37`), Heavy SEAF (`patch_900`), Chaos Invasions (`patch_910`), and EmpireDivers (`patch_1000..1036`).
- **4-Player Co-op & Mid-Mission Hot-Join Safe (`slots 0..3`, Host & Joiner):**
  - Resolves the local Helldiver avatar across player slots `0..3` via `LocalUser` (`+0xB398`) + `PlayerManager` (`lifecycle == 3`) + `EntityManager`.
  - Matches local-authority (`spawnData[0] != 0`) secondary weapon spawns across all `25` build-`25480438` secondary firearms/melees and `HfContent` modded secondaries—removing the `seq == 2` / `< 2000 ms` mission-start race so late co-op hot-joins and reinforcements work reliably.
  - Marks the local extra melee entity with non-networked sentinel `network_id = 0x7FFF` (`game.dll+0xFDC263`), keeping your real secondary pistol's `network_id` intact in `EntityManager`'s network map.
  - Preserves all 4 player slots' `+0x2E8` renderer words across `game.dll+0x8BF870` (`AttachCopy`).
- **Custom Keybind & Foreground Window Safety:**
  - Reads rebound primary/secondary keys from `input_settings.config` (falling back to `1` and `2`) and gates key polling and `SendInput` on `helldivers2.exe` owning the foreground window.

## Usage

1. Install **Dedicated Melee Slot (`1.5.2-clonedivers.2`)** in HellForge (and **Bingus Shared Loader v15+** if you want the clickable `MELEE:` picker box on the pre-mission player card; otherwise write `machete`, `stun-lance`, `saber`, `stun-baton`, `hatchet`, `shovel`, or `none` to `Documents\HellForge\melee-slot-weapon.txt`).
2. In a mission, press your secondary weapon key (`2` by default) to draw your secondary, then **tap `2` again while drawn** to toggle between your pistol and your extra melee weapon.

## Building & Testing Offline

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-offline.ps1
```
