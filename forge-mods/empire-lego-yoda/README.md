Offline native mod (v0.2.0) for HellForge 0.4.0-beta / SDK 16 / build 25480438.
Client only (`gameplay: false`, `coop: per-player`); no Lua loader or HD2Runtime dependency.

Reads applied category-0 armor, never menu selection or helmet. Built-in torso pairs cover both
Empire Stormtrooper LEGO packs (`r47-lego-compact`, `r45`/`r47-shuttles`) and Clonedivers LEGO
Clone packs (`clonedivers-r19-full`), plus optional extensible pairs from `<mod_folder>\yoda-torsos.json`.
Scans staged patch directories at startup respecting numeric patch order within a family; ambiguous
cross-family providers refuse the gate. Never changes game memory, patches, banks, statistics or network state.

Supports 1-4 player co-op by resolving the local peer across `PlayerManager` slots `0..3`, surviving
mid-mission slot compaction on peer disconnects, caching a fresh local avatar with native health `life < 2`
and the exact applied record, and sharing an 8-slot consumed `(peer, avatar, network)` deduplication ring
across `HF_ON_PLAYER_DOWN` and the 100 ms poll route. Remote player deaths remain silent.

During a mission, caches a fresh local avatar with native health life < 2 and the exact applied record.
Either HF_ON_PLAYER_DOWN or a 100 ms local poll can route death confirmation. Both require the same local
peer/player/network/avatar, lifecycle 2, native health life >= 2, and unchanged applied bytes and torso proof
within 500 ms of the last eligible observation. They share one PlaySound attempt per peer/avatar/network
tuple, including failed attempts. Mission transitions clear observations. Remote deaths, human bodies,
helmet-only LEGO, unknown state and immediately removed corpses remain silent.

Playback: Windows winmm PlaySoundW, SND_MEMORY | SND_ASYNC | SND_NODEFAULT | SND_NOSTOP, with the
hash-checked bundled WAV kept in memory. Local, non-positional, additive; does not use the game's Wwise mix
or master volume. Logs `yoda attempt ... result=started/refused route=sdk-down/local-poll`.
A successful API return still needs listening
acceptance. Another PlaySound in this process may cause a refusal; do not retry a death.

The first scan may read many archive directories. Hot pack replacement is unsupported: restart after every
pack change. The selected proof's file identity/size/write time is rechecked at death. It does not detect a newly
added higher-priority patch during a running session; never live-edit appearance packs in this prototype.

See D:/ClaudeWork/empire-forge/RUNTIME-FEATURES.md for decisions, offline verification and live-test steps.
