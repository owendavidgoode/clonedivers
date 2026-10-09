# EmpireDivers startup repair loop — October 9, 2026

The owner authorized unattended actual launches, forced HD2 closure between tests,
and continued repair without questions. The owner then requested inspection of
Claude's findings before proceeding. Public releases remain unchanged while the
repairs are tested privately.

## Claude findings verified locally

The `claude-session-inspector` skill located **Empire Divers crashes investigation**,
session `46ebdff1-f837-445b-a937-d9a3df588453`. Its report and local review are retained
under the ignored `dist/empire-launch-crash-2026-10-08/loop-2026-10-09/claude-review-v1`.

Logical 453's public MAIN is only 204 bytes, SHA256
`7db519ad43cfd67b3d75c853afa63c82ff8c17fe82fc9b04bc0dd7479cbf4204`.
Its 12-byte Wwise wrapper starts at byte 192 and ends at EOF. Four actual
`NxStorage_*.txt` logs name its installed slots in the failed public selections,
with error `89240007`. This provides a substantially narrower lead than removing
every audio archive. The exact engine read size and its connection to the native
exception still require runtime confirmation.

The minimal candidate appends 52 zero bytes, preserving the entire original
prefix, TOC and audible stream. Its 256-byte MAIN has SHA256
`25b90ccff893f69b3d59ebc9057f040c4cccaad54ea44c1822dbaf94d2a7c11b`.
The public-r23 candidate manifest changes only that MAIN row's size/hash/URL,
diagnostic notes and derived total size; manifest SHA256
`ba0c912347883c901d1731881e82fe74b680bbdbf5dae455008a6aa82a724c46`.
The original malformed archive fails the padding guard; the correction passes.
`tools/deep-next-audio-constitution-prefix.py` now pads newly built MAINs and checks
the aligned head and 16-byte metadata slot.

Claude also identified logical 429's resource rows interleaved by name across
types, while its type table expects contiguous groups. The earlier ship-texture
diagnostic preserved this ordering defect. A minimal row-order correction and
independent checks confirm the defect. The correction changes only 132 bytes in
the existing file-row table, preserving all six resources, offsets and companions.
`tools/finish-ships-build.py` now orders rows by type then name and asserts
contiguous type ranges with dense ordinals. The combined full-r23 manifest is
`af51fa33b59141c2db938e2a67ee255c859e86e92beb444ecdb894d39176d380`.
Ship geometry/content causation is not yet isolated to that one archive.

## Actual test results

1. **17:26–17:31, Empire / Full native geometry control, 1,023 files — boot passed.**
   This is manifest `486207175b89d6ea9f5d6365b28cc5f29a52023bf15cba4faa25c53e35cd259a`.
   The title screen, native exterior and solo ship bridge were directly observed
   through Computer Use. Stormtrooper armor was visible. No new crash dump or
   DirectStorage log appeared, and the installed receipt stayed byte-identical.
   HD2 was then force-closed as authorized, with process exit confirmed before any
   subsequent installation. Evidence: `loop-2026-10-09/attempt-001-native-ship`.
   This control omits all selected audio/Lua and removes ship UNIT overrides across
   nine archives, so it narrows the remaining failure without attributing it to a
   single change. Older ship materials/textures remain. It is not the final pack.

2. **17:40–17:42, full public Clonedivers / Full, 1,199 files — boot passed.**
   Manifest `ba0c912347883c901d1731881e82fe74b680bbdbf5dae455008a6aa82a724c46`
   changes only the Constitution MAIN. Launched with the actual desktop launcher;
   directly observed the clone-armored character on the solo ship bridge with
   gameplay HUD and exterior fleets visible. No new dump or DirectStorage log;
   receipt byte-identical. Forced closure completed before the next installation.
   Evidence: `loop-2026-10-09/attempt-002-fixed-clone` and
   `apply-fixed-clone-v1/verified-install.json`. All selected audio/Lua restored.

3. **17:44–17:48, full public Empire / Full, 1,396 files — boot and soak passed.**
   Manifest `af51fa33b59141c2db938e2a67ee255c859e86e92beb444ecdb894d39176d380`
   restores all original r23 content, changing only the two malformed MAINs.
   The actual launcher reached the solo ship with Imperial armor, HUD and visible
   Star Destroyer silhouettes. No new dump or DirectStorage error and receipt
   unchanged. BingusSharedLoader reports seven scripts loaded, zero failed,
   including Eagle dispatch, LEGO Yoda death and walker observer. Their presence
   does not establish gameplay or audible acceptance. A 185.5-second soak after
   ship observation completed without a new dump or DirectStorage error.
   Evidence: `loop-2026-10-09/attempt-003-fixed-empire` and
   `apply-fixed-empire-v1/verified-install.json`.

4. **17:52–17:55, final r24 Empire / Full, 1,396 files — boot passed.**
   Candidate manifest SHA256
   `52ce94fc76141535aa094c46de69f447eb5d04623787426d3c9f06c4f874da9c`.
   The actual desktop launcher reached the solo ship with Imperial armor, HUD and
   Star Destroyers visible. No new dump or DirectStorage log; receipt unchanged.
   Seven scripts loaded with zero failures. Preserved logs show the Yoda handler
   armed and the Eagle fighter comparison applied after reaching the ship.
   Installed receipt/file context checks pass independently using Win32/BCrypt.
   Evidence: `loop-2026-10-09/attempt-004-r24-empire` and
   `dist/production-r24-2026-10-09/qa/installed-context-v2/report.json`.

5. **17:57–18:02, final r24 Clonedivers / Full, 1,199 files — boot passed.**
   The same r24 candidate reached the solo ship through the desktop launcher.
   Clone armor, gameplay HUD and exterior fleet were directly observed. No new
   dump or DirectStorage log; receipt unchanged. HD2 was force-closed and its
   process exit confirmed before restoring Empire.
   Evidence: `loop-2026-10-09/attempt-005-r24-clone`.

All 96 launcher states (16 effective file sets) pass selection qualification;
547 distinct MAIN identities pass the central structural preflight. Thirteen
regressions reject the original malformed archives and accept their corrections.
The r24 contexts pass 121 fixtures; the actual successful Empire runtime logs and
installed context checks have independent peer reviews.

Next: stage and publish r24, run actual Check/Repair against the public release,
and repeat Empire startup. The new version ensures saved receipts adopt the
corrections. Gameplay, audible Yoda playback and vehicle physics remain untested.
