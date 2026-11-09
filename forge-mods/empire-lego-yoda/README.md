Offline native prototype for HellForge 0.4.0-beta / SDK 16 / build 25480438.
Client only (`gameplay: false`, `coop: per-player`); no Lua loader or HD2Runtime dependency.

Reads applied category-0 armor, never menu selection or helmet. DP-40 B513FD54 and AF-02 E9ADD047
qualify only for the corresponding Brawny/Lean torso whose staged MAIN and GPU payloads match r45-v3.
Scans staged patch directories at startup, respecting numeric patch order within a family; ambiguous
cross-family providers refuse the gate. Never changes game memory, patches, banks, statistics or network state.
Use one appearance pack; this gate is a version-specific torso proof, not a general pack conflict resolver.

During a mission, caches a fresh alive local avatar and applied record. HF_ON_PLAYER_DOWN must agree on
local peer/player, applied bytes and native health life >= 2 within 500 ms. At most one PlaySound attempt per
peer/avatar/network tuple, including failed attempts. Mission transitions clear observations. Remote deaths,
human bodies, helmet-only LEGO, unknown state and immediately removed corpses remain silent.

Playback: Windows winmm PlaySoundW, SND_MEMORY | SND_ASYNC | SND_NODEFAULT | SND_NOSTOP, with the
hash-checked bundled WAV kept in memory. Local, non-positional, additive; does not use the game's Wwise mix
or master volume. Logs `yoda attempt ... result=started/refused`. A successful API return still needs listening
acceptance. Another PlaySound in this process may cause a refusal; do not retry a death.

The first scan may read many archive directories. Hot pack replacement is unsupported: restart after every
pack change. The selected proof's file identity/size/write time is rechecked at death. It does not detect a newly
added higher-priority patch during a running session; never live-edit appearance packs in this prototype.

See D:/ClaudeWork/empire-forge/RUNTIME-FEATURES.md for decisions, offline verification and live-test steps.
