# Star Wars Localization Bridge (`clonedivers.starwars-localization`)

Per-player (`gameplay: false`, SDK 16, build `25480438`) runtime bridge that restores EmpireDivers and Clonedivers Armory, helmet, armor, and weapon names when HellForge's auto-generated `forge-credits` patch stages last and overwrites the US English `TEXT` resource (`0x7c7587b563f10985`).

## How It Works
1. At startup, inspects `<game>\data\9ba626afa44a3aa3.patch_*` from highest patch index downward.
2. If the topmost US English `TEXT` patch is HellForge's `forge-credits` table AND an earlier modpack `TEXT` patch sits underneath it, builds a sorted in-memory diff table `(uint32_t id -> UTF-8 string)` containing every changed pack string while skipping any string whose baseline contains `"forge credit"` (case-insensitive).
3. Hooks the engine text lookup function via `hf->hook_ex` (leaving HellForge's `table + 1000` slot untouched so HellForge's own `AddText` hook chains cleanly).
4. If no `forge-credits` clobber is present (Regular mode or a future HellForge that merges `TEXT` natively), loads zero overrides and installs zero hooks.
