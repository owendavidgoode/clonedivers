# Linux feedback follow-up — 0.6.1

The tester's developer note describes Snap Steam, an external library move and
the 0.6.0 Forge migration. The referenced support ZIP was not supplied here;
the fixes below use reproducible source evidence, not an inspection of that ZIP.

## Changes

- Map Snap VDF `/var/lib/snapd/hostfs/...` library paths to host paths for discovery
  and owning-client matching. Retain canonical root/alias de-duplication and
  refuse ambiguous ownership.
- Open the Forge launcher's official-HD2 library button through the owning
  native/Snap/Flatpak client, never the desktop Steam URI handler. It opens the
  library page; it does not launch HD2. No package, login or URI default changes.
- Give the pinned official wrapper its supported
  `STEAM_COMPAT_CLIENT_INSTALL_PATH`, selected from HD2 ownership or an explicit
  user setting. Without HD2, one unambiguous client is sufficient. Multiple
  clients require explicit selection; the empty native root cannot silently win.
- Read trusted OS metadata sequentially without file-length/seek assumptions.
  Accept the normal os-release symlink, distinguish kernel from OS description,
  validate GPU identifiers and retain GPU IDs when a driver link is unavailable.
  Existing game/write/report-log link guards remain unchanged.
- Record successful desktop installs and desktop/terminal vanilla switches.
  Existing history stays private and bounded to the most recent 40 events.
- Link the pinned author release and Linux instructions, explain C: prefix paths
  and distinguish Snap Steam discovery from actual runtime qualification.

## Remaining qualification

No live Steam files, desktop handlers, packages, accounts or security settings
were changed to reproduce this report. Fixtures and Ubuntu package tests do not
qualify the tester's Snap namespace bridge, Proton 11, account import, combat,
extraction, progression persistence or separate-PC multiplayer.

A moved legacy installation's receipt remains tied to its prior absolute path.
The existing explicit install/repair flow can create a new receipt after checking
the selected pack. An automatic or metadata-only receipt rebind is not included;
it would claim verification of a new location without fresh content evidence.
Missing older installation events cannot be diagnosed conclusively without the
referenced report and its configuration/history source.

Retest on the supplied Ubuntu/Snap machine: `--steam status` should identify
Snap Steam and the external game folder; export a fresh local report and inspect
kernel/distribution/CPU/GPU fields and successful events; finish the pinned
official Forge setup in its dedicated prefix. Then perform actual gameplay and
two-PC multiplayer acceptance. Do not infer those results from package CI.
