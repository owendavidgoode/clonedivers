# EmpireDivers r23 LEGO Yoda death sound

Public pack `2026.10.08-r23` adds the classic Complete Saga `YODADEATH.WAV` to the local player's LEGO Stormtrooper (DP-40, body ID `B513FD54`) and LEGO Bikini Stormtrooper (AF-02, `E9ADD047`). Both body types qualify; helmet choices are irrelevant. Ordinary body armor with a LEGO helmet does not qualify.

An isolated event in the common player voice BANK adds four HIRC objects and one embedded PCM recording. All 2,640 old HIRC objects, 604 events, 644 sound contracts and 335 resident media entries remain exact. Two independent parsers and a fresh decoder validate the new graph and unchanged 14,705-frame, mono 11,025-Hz recording. New IDs do not collide with 479 native BANKs or the 116 effective Empire BANK providers.

The trigger reads applied body armor through the current native customization record, retaining the local player, avatar and mission identity. It requires an actual native dead state, fresh armor observation, matching applied record, finite death position and resident event. A post is attempted at most once per avatar death. Avatar removal is refused; deaths that remove the avatar immediately without an observed dead state may not play. Teammate routing is excluded.

Minimal read-only native backports retain build and loaded instruction checks. The shared HD2Runtime stays at 0.28.1. The context binds the public r23 feed/version, game build 25480438, agreeing Full/Lighter profiles, active receipt, LEGO visual closure and freshly hashed audio MAIN. Later checks watch stable creation/write/size/attribute metadata; normal last-access changes are ignored. No native memory writes or global voice-event substitutions are introduced.

Final qualification uses `runtime/candidate-v4` and `inputs-v2`, superseding the earlier private candidates. The actual public launcher selects all 96 states and 16 distinct file sets. All 1,800 prior manifest rows and 64 Clone/Commando states are exact. The 32 Empire states append only nine files. Independent directory resolution confirms that only the common BANK, new Yoda LUA and version-rebound Eagle LUA winners change.

Independent checks pass 81 armor/death-policy cases and 63 real Windows/Bcrypt context cases. Separate native-reader/routing fixtures and full entrypoint smoke use the actual installed SDK API/event/scheduler sources with synthetic engine bindings. Loaded-process instruction checks, Wwise playback and actual game behavior still require playtesting.

Test one confirmed death in each LEGO body, then ordinary armor, respawn and mission exit. The effect adds its own one-shot; existing death audio remains. The previous Star Destroyer, walker, Eagle and audio playtest checklist remains applicable.

Uses code / research from **HD2Runtime by SkyeShade**: https://github.com/SkyeShade/HD2Runtime, commit `41184521ce6021bf15a294d2debdee3da30d931a`, under its copying-with-credit permission. Recording and extraction credits are in [the release notes](releases/pack-2026.10.08-r23.md).

The public release is published from source `c4e962e8aa69496bb9f307d49f5e5a22504767db` and feed commit `25b67e4284f95345fa2fbcbfaced6c5dda49c151`. The sealed manifest SHA-256 is `b08614df277dad2388a70ddeab7e230fa9990b7045e0abbe3c687e2c6966e5f7`. All three coordinated feeds match their sealed preparations. The complete public check verifies 1,106 hosted asset identities and the launcher download; an independent peer downloaded and hashed all three new assets (4,559,088 bytes). The latest launcher remains 1.7.3.

Local installation is queued behind the closed-game/closed-launcher guard. A plan attempt refused a running launcher before any changes; the installed checkpoint remains r22. This is separate from the completed public publication. The guarded local helper and its independent source review are ready. See [the verification record](releases/pack-2026.10.08-r23-verification.json) for the exact scope.

Versioned evidence is retained under `dist/empire-yoda-death-2026-10-08` and `dist/production-r23-2026-10-08`.
