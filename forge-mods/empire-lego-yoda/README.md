# Empire & Clone LEGO Yoda Death & Stormtrooper Salute (`clonedivers.empire-lego-yoda` `0.2.3`)

Offline native mod (`v0.2.3`) for HellForge `0.4.0-beta` / `0.5.0-beta` / SDK 16 / build `25480438`.
Client only (`gameplay: false`, `coop: per-player`); no Lua loader, engine audio hooks, or memory writes.

## Features

1. **LEGO Yoda Death (`audio/classic-lego-yoda-death.wav`):**
   - Reads applied category-0 armor, never menu selection or helmet. Built-in and `yoda-torsos.json` pairs cover
     all `30` Empire & Clone Compact LEGO torso visuals (including the `12` Clone Compact LEGO torsos and `10`
     converted legacy Empire LEGO body variants in `r51`/`r52`).
   - Supports 1-4 player co-op by resolving the local peer across `PlayerManager` slots `0..3`, surviving
     mid-mission slot compaction on peer disconnects, caching a fresh local avatar with native health `life < 2`
     and the exact applied record, and sharing an 8-slot consumed `(peer, avatar, network)` deduplication ring
     across `HF_ON_PLAYER_DOWN` and the 100 ms poll route. Remote player deaths remain silent.

2. **Stormtrooper Salute Trigger (`audio/stormtrooper-salute.wav`, Bundled Out of the Box & Overridable):**
   - Vanilla Helldivers 2 Salute emotes (*Super Earth Salute* and *Casual Salute*) have no dedicated Wwise VO event.
   - Ships out of the box with a 100% original synthesized Stormtrooper helmet-comm WAV (`audio/stormtrooper-salute.wav`,
     24 kHz 16-bit mono PCM, `<= 80 KB`, deadpan `"Stormtrooper."` nod with light helmet click; zero copyrighted TV audio).
     Any valid replacement RIFF/WAVE file (`<= 4 MB`, 1-2 ch PCM or IEEE float, 8-96 kHz) placed at
     `<mod_folder>\audio\stormtrooper-salute.wav` automatically overrides the default clip, and removing the file
     gracefully disables Salute polling with zero per-tick overhead.
   - Monitors the **local** player's 107-node Helldiver avatar scene graph (`0x4D1C334D294DFA97`) read-only
     in both `HF_STATE_SHIP` (Star Destroyer bridge) and `HF_STATE_MISSION` (compatible with both 2,048-slot and
     relocated 4,096-slot `EntityManager` tables via `clonedivers_em_layout.h`), firing `PlaySoundW`
     (`SND_MEMORY | SND_ASYNC | SND_NODEFAULT | SND_NOSTOP`) once per rising edge after 3 consecutive stationary
     Salute samples (~200-300 ms) with hold-latch and 1.5 s re-trigger cooldown while rejecting two-handed emotes,
     weapon aiming/reloading, sprinting, grenade/stratagem throws, and remote co-op players.

## Playback & Operational Notes

Playback uses Windows `winmm` `PlaySoundW` with in-memory WAV buffers. Local, non-positional, additive; does not
modify Wwise soundbanks or game state. Logs `yoda attempt ...` and `salute attempt ... result=started/refused`.
Restart the game after changing staged packs or drop-in `audio/` files.
