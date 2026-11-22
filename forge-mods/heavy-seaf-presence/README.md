# Heavy SEAF Presence Everywhere

HellForge port for build **25480438**. Modifier **1401** is what makes Pelicans bring SEAF squads into a mission. Effect **1400** is the galactic-map name. HellForge's backend serves an empty galactic-effect catalog, so the mission never learns what 1401 does. This mod gives the game that catalog at startup, adds both ids to every planet when the war status is parsed, and writes them again if the game rebuilds the planet table.

On a mission, a wave of SEAF squads is called in once the player is on the ground, and another every 90 seconds after that: two squads per wave by default. Waves pause while 24 SEAF are alive, so the field cannot pile up past what the game carries. A fresh mission after the new session is what picks this up.

## Squad cap

The battlefield cap stays at the game's default (about 18 SEAF) until you change `EXTRA_SQUADS` at the top of `src/heavy_seaf_presence.cpp` and start a new session.

| Value | SEAF on the field at once |
| --- | --- |
| 0 | about 18 (default) |
| 1 | 24 |
| 2 | 30 |
| 3 | 36 |
| 4 | 42 |
| 5 | 48 |

5 is the highest step. The cap is written into the game's SEAF template once, and only when exactly one copy of that template is found. The session log says whether it was applied. Set it before the session starts.

## Call-in instead of constant drops

SEAF Reinforcement is a supply stratagem, code `LRLRUD`. One throw starts a Heavy SEAF Presence wave at the beacon: three squads, on a 90 second cooldown. Constant drops stay on, so the timer still sends waves as well, until **CONSTANT DROPS** is turned off on the MODS page. Off means a wave shows up only when that stratagem is called. Without the MODS page, `constant=0` in `options.ini` in this folder does the same thing.

The timer's two numbers are on the MODS page as sliders, and in `options.ini`: `interval_seconds` (10 to 900, default 90) and `squads_per_wave` (1 to 3, default 2). A new value applies to the next wave; `options.ini` needs a new session. Waves stop being added while 24 SEAF are alive and resume after casualties; the game's own drops are unaffected.

All five rows appear when a menu loads. **Universal Mod Menu** (the MODS page inside giblett's megapack v42) is a code mod, and this mod registers with its C API. The native **Vanilla Plus Megapack** replaced that menu with its own Mod Options Menu, which only Lua addons can register into: this mod ships a small Lua addon (`data/9ba626afa44a3aa3.patch_900`) that adds the same five rows there and passes values back and forth through `HeavySeafMenu.values` beside the game's logs. With neither menu, `options.ini` is the only place.

## Custom squads

**SEAF Squad Stratagems must be installed and enabled** (on every co-op player's machine, using the same package): its author allows these compositions only while that mod is present. With it loaded, the mission's SEAF squad roster gets the game's own patrol entry plus the **Stealth**, **Riot Control** and **Tank Hunter** compositions appended. The game's own SEAF drop (this mod's waves, SEAF Reinforcement, or any other SEAF call-in) draws from that roster, so those squads arrive by ordinary Pelican, and this mod equips each six-person squad as it lands. Without the source mod, the log says `POOL: SEAF Squad Stratagems is not loaded` and the roster keeps the game's own composition.

Throwing one of its four squad stratagems brings that squad down at its own beacon; this mod dispatches them because that mod's delivery code cannot run on this game build.

On the MODS page, **SEAF SQUAD STRATAGEMS IN WAVES** switches waves back to this mod's own delivery path (the 1.0 behavior; it also requires the source mod's four call-ins to be detected). The option defaults off. Without the MODS page, set `support_squads=1` in `options.ini` to turn it on. Its four-squad cap of 18 living troops applies only to that path.

## Tank Hunter targeting (not active yet)

**TANK HUNTER SQUADS GO FOR HEAVIES** is off by default on this build. The steering code cannot find the squad's AI record, so the switch currently changes nothing: Tank Hunter troops target like any other SEAF squad and the log prints `HUNTER_NO_AI` for them while it is on. Keep it off (`tank_hunters=0` in `options.ini`) until that is fixed; the code stays in the mod for the follow-up.

Squad compositions and delivery/equipment routines are bundled from SEAF Squad Stratagems 0.1.7, with permission from its author, JohnnyGrease. Install and enable the original SEAF Squad Stratagems mod: the custom squads are only added while it is loaded, and its four squad stratagems then work through this mod. [Original mod page](https://hf-mod.com/Mods/Details/59).

## Enable

The mod is on unless `mods/hellforge-mods.json` lists `local.heavy-seaf-presence` under `disabled`. HellForge compiles the C++ sources when staging a session.

Do not run this beside **SEAF Squad Pool**; the roster integration is bundled here and both would act on the same roster.

**Helldivers: Supreme Command** (`edhd2.second-galactic-war`) can be run alongside. It writes the same modifier 1401 onto the planets it is invading and takes it out again only when it was the one that added it, so it finds this mod's stamp already in place and leaves it alone; this mod re-writes anything a table rebuild removes.

## What the log should say

`mod-local.heavy-seaf-presence.log` in the session logs:

- `effect catalog: gave the game the Heavy SEAF Presence definition` when the empty catalog is replaced
- `planet table: N copy(ies)` when the galaxy table is found
- `modifier 1401 written into M planet record(s)` the first time planets are updated
- `SEAF Squad Pool ready` and `POOL: roster 1 -> 4 entries` once per mission (the roster now carries patrol, stealth, riot and tank)
- `POOL: SEAF Squad Stratagems is not loaded` instead, and no pool lines, when the source mod is missing or disabled
- `POOL_PACKAGES: 22 weapon packages resident` before the first custom squad can be equipped
- `SQUAD_EQUIPPED: SEAF … Squad; shuttle N; six passengers` for each custom squad the game drops
- `HUNTER_NO_AI: soldier N has no AI record yet` only if the Tank Hunter switch is turned on; it is off by default
- `SEAF field holds N living troops; the next wave waits for casualties` when the wave timer is paused at the 24-SEAF ceiling, and `SEAF field down to N living troops; waves resume` when it lifts
- `SEAF call-in: squad N took M ms (sent|no landing site|…)` only when a call-in held the game thread for 200 ms or more
- `MODS page rows 5 of 5 ready (Universal Mod Menu …)` when Universal Mod Menu serves the rows, or `Universal Mod Menu not found. The Lua bridge shows the rows when a menu serves ModOptionsMenu …` once when the bridge is the path
- `rows added to the MODS page (Heavy SEAF Presence Everywhere).` in `HeavySeafMenu.log`, from the Lua addon, when the native megapack's Mod Options Menu takes the rows
- a squad line only when `EXTRA_SQUADS` is not 0

If the table is not found, nothing is written.
