# Star Wars Localization Bridge (`clonedivers.starwars-localization` `0.1.1`)

Per-player (`gameplay: false`, `coop: per-player`, SDK 16, build `25480438`) runtime bridge (`0.1.1`) that restores EmpireDivers and Clonedivers US English `TEXT` overrides (Armory, helmets, armors, weapons, stratagems, mission objectives, and SEAF-free Imperial & Republic radio VO subtitles across all `14` US English `TEXT` tables) when HellForge's auto-generated `forge-credits` patch stages last and overwrites US English `TEXT` resources (`0x0d972bab10b40fd3`) in `9ba626afa44a3aa3`.

## How It Works
1. At startup, inspects `<game>\data\9ba626afa44a3aa3.patch_*` from highest patch index downward, scanning all `TEXT` entries and filtering for US English tables (`SW_LOC_MAX_US_TABLES = 64`, failing closed if exceeded).
2. If the topmost US English `TEXT` patch is HellForge's `forge-credits` table AND an earlier modpack `TEXT` patch sits underneath it, builds a sorted in-memory diff table `(uint32_t id -> UTF-8 string)` across all matched US English tables while protecting `Forge Credits` strings (`"forge credit"`, case-insensitive) and `WARBOND LOCKED` (`0xF8AD8A16`) globally across all baseline US tables.
3. Hooks the engine text lookup function via `hf->hook_ex` (leaving HellForge's `table + 1000` slot untouched so HellForge's own `AddText` hook chains cleanly).
4. If no `forge-credits` clobber is present (Regular mode or a future HellForge that merges `TEXT` natively), loads zero overrides and installs zero hooks.
