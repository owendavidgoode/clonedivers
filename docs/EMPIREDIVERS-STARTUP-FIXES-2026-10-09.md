# EmpireDivers startup repair loop — October 9, 2026

The owner authorized unattended actual launches, forced HD2 closure between tests,
and continued repair without questions. The owner then requested inspection of
Claude's findings before proceeding. Public releases stayed unchanged during the
private repair tests; r24 was published after runtime and release qualification.

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

## Publication and repair acceptance

Coordinated Stage passed all native launcher tests and freshly hashed both
profile exports. Independent review verified all eleven sealed files, exact
candidate semantics, four assets totaling 466,976 bytes, the unchanged launcher
1.7.3 and all three feed projections. Prepared files are retained at
`D:/clonedivers-release-r24-20261009/release-ready-v1` because C: was low on space.
The bundled SDK was NTFS-compressed with paths and contents retained; game files
were not compressed or relocated.

The coordinated publication completed. Source commit:
`491d16ba40d4c75f0193aae056337b7bc8204af0`; feed commit:
`dcff6d7a8f01022b2102a520539444a450112c9f`. The serialized public current manifest
has SHA256 `5c9e6230f3f3d36394af0ba5cbd9cd8030f62081195791e267d0a6fa580e26df`
and exactly the runtime-tested candidate's parsed contents. Fresh downloads of
all four new assets match their hashes; all three API/raw feeds match the sealed
projections. Latest launcher remains 1.7.3. Evidence:
`dist/production-r24-2026-10-09/qa/public-peer-v2/report.json`.

At 18:09, actual desktop launcher Check/Repair reported
**Verified: all 1,396 files match 2026.10.09-r24.** Selected receipt rows stayed
exact, camera settings stayed exact and no pending transaction remained.
Independent installed Eagle/Yoda context checks still pass against the published
manifest. Evidence: `qa/public-repair-after-v1` and
`qa/installed-context-after-public-repair-v1` under the r24 evidence tree.

At 18:10, Empire was launched again through the desktop launcher. The solo bridge,
Imperial character, HUD and exterior Star Destroyers were directly observed.
No new dump or DirectStorage log; receipt unchanged. A 182-second soak after
direct ship observation passed with the bridge still visible and process
responding. HD2 was force-closed as authorized, its exit confirmed, and the
launcher closed. Empire / Full r24 remains installed and selected.
Evidence: `loop-2026-10-09/attempt-006-public-r24-empire`.

Gameplay, audible Yoda playback and vehicle physics remain untested.
