# Credits & Attribution

- **Original Author:** [`vibeslacker/melee-weapon-slot-test`](https://github.com/vibeslacker/melee-weapon-slot-test) (`v1.5.2`, `MIT` License) — Dedicated Melee Slot & Melee Picker (`mods/jarvis/melee_picker`), custom `input_settings.config` keybind reader (`v1.6.3`), and package-set discovery.
- **Clonedivers Co-op & SDK-16 Maintenance (`1.5.2-clonedivers.1`, `MIT` License):**
  - Multi-slot (`slots 0..3`) `LocalUser` + `PlayerManager` + `EntityManager` Helldiver avatar resolution (replacing `HfPlayerEvent.entity` player-entity assignment).
  - Preservation of all 4 player slots' `+0x2E8` renderer words across `ATTACH_RVA` (`game.dll+0x8BF870`).
  - Deterministic local-authority (`spawnData[0] != 0`) + secondary-weapon (`armory type 3` + `HfContent` custom weapons) spawn matching, fixing `seq == 2` / `< 2000 ms` hot-join and 4-player co-op races, with `network_id = 0x7FFF` sentinel preventing `EVP_NETWORK_MAP` pistol slot clobbering.
  - Backported `input_settings.config` custom keybind support (`LoadSlotKeys`), foreground-window focus gating on `GetAsyncKeyState` / `SendInput`, reusable spawn-context scratch buffer, `GetLastError()` / SSE `MXCSR` detour preservation, and clean thread shutdown on `HF_EVENT_SESSION_END` / `hellforge_mod_unload`.
  - Unified HellForge `Mod SDK 16` packaging with non-colliding `data/9ba626afa44a3aa3.patch_920` Lua addon archive and nil/teardown guards in `melee_picker.lua`.
