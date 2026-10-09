# EmpireDivers r23 LEGO Yoda death sound

The r23 candidate adds the classic Complete Saga `YODADEATH.WAV` to the local player's LEGO Stormtrooper (DP-40, body ID `B513FD54`) and LEGO Bikini Stormtrooper (AF-02, `E9ADD047`). Both body types qualify; helmet choices are irrelevant. Ordinary body armor with a LEGO helmet does not qualify.

An isolated event in the common player voice BANK adds four HIRC objects and one embedded PCM recording. All 2,640 old HIRC objects, 604 events, 644 sound contracts and 335 resident media entries remain exact. Two independent parsers and a fresh decoder validate the new graph and unchanged 14,705-frame, mono 11,025-Hz recording. New IDs do not collide with 479 native BANKs or the 116 effective Empire BANK providers.

The trigger reads applied body armor through the current native customization record, retaining the local player, avatar and mission identity. It requires an actual native dead state, fresh armor observation, matching applied record, finite death position and resident event. A post is attempted at most once per avatar death. Avatar removal is refused; deaths that remove the avatar immediately without an observed dead state may not play. Teammate routing is excluded.

Minimal read-only native backports retain build and loaded instruction checks. The shared HD2Runtime stays at 0.28.1. The context binds the public r23 feed/version, game build 25480438, agreeing Full/Lighter profiles, active receipt, LEGO visual closure and freshly hashed audio MAIN. Later checks watch stable creation/write/size/attribute metadata; normal last-access changes are ignored. No native memory writes or global voice-event substitutions are introduced.

Final qualification uses `runtime/candidate-v4` and `inputs-v2`, superseding the earlier private candidates. The actual public launcher selects all 96 states and 16 distinct file sets. All 1,800 prior manifest rows and 64 Clone/Commando states are exact. The 32 Empire states append only nine files. Independent directory resolution confirms that only the common BANK, new Yoda LUA and version-rebound Eagle LUA winners change.

Independent checks pass 81 armor/death-policy cases and 63 real Windows/Bcrypt context cases. Separate native-reader/routing fixtures and full entrypoint smoke use the actual installed SDK API/event/scheduler sources with synthetic engine bindings. Loaded-process instruction checks, Wwise playback and actual game behavior still require playtesting.

Test one confirmed death in each LEGO body, then ordinary armor, respawn and mission exit. The effect adds its own one-shot; existing death audio remains. The previous Star Destroyer, walker, Eagle and audio playtest checklist remains applicable.

Uses code / research from **HD2Runtime by SkyeShade**: https://github.com/SkyeShade/HD2Runtime, commit `41184521ce6021bf15a294d2debdee3da30d931a`, under its copying-with-credit permission. Recording and extraction credits are in [the release notes](releases/pack-2026.10.08-r23.md).

Versioned evidence is retained under `dist/empire-yoda-death-2026-10-08` and `dist/production-r23-2026-10-08`.
