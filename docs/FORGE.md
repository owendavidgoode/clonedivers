# Clonedivers Forge status

The replacement launcher is Windows **1.13.0**, Linux x86-64 **0.7.0**, native controller
**0.4.0**. Existing Windows update feeds point to the new default Forge executable;
Linux notices the complete 0.7 release and requires package/manual installation.
See [the migration guide](FORGE-MIGRATION.md) for rollout and recovery.

The launcher now installs the complete pinned gameplay preset through the
official CLI. Seven required archives have immutable URLs, size/SHA256 and
official content hashes; three baseline mods come from official Forge setup.
The Heavy/Squad compatibility files are distributed under the owner's confirmed
written author permission, with credits retained. Five other dependencies are
obtained directly from authors. See [permission scope](SEAF-COMPATIBILITY-PERMISSION.md)
and the embedded gameplay catalog. Files are staged and verified before imports,
with backups and durable recovery. Accounts and game files are not bundled.

The owner's Windows account level/Warbonds and solo D11 SEAF call-ins were
verified. Windows launch/End-session, isolated software fixtures and Linux package
install/help checks are distinct evidence. Actual Linux account/Proton gameplay,
extraction/reward-spend persistence and separate-PC multiplayer remain open.
Follow [squad acceptance](HELLFORGE-SQUAD-ACCEPTANCE.md), [mod coverage](FORGE-MOD-COVERAGE.md)
and author receipts. Source/package CI is not gameplay certification.

The 0.4 runtime migration updates the official Windows application and replaces
the Linux Wine bridge with native installation/control. Setup applies sharing OFF
automatically. The old standalone mine patch is disabled because the engine
includes its fix. See [upgrade evidence and remaining manual steps](FORGE-0.4-TURNKEY-2026-11-05.md).

The update preserves Steam files, settings and Forge account/profile data.
Windows retains a verified previous executable before old-file cleanup. Old
Steam pack metadata stays unchanged in compatibility feeds. Hidden --steam is
recovery only; ordinary official HD2 launches through Steam. Native Quit hanging
and ship distortion remain open; use official End session cleanup.

[The original preview](https://github.com/owendavidgoode/clonedivers/releases/tag/forge-preview-2026-11-04)
and its [artifact provenance](FORGE-REVIEW-ARTIFACTS-2026-11-04.md) remain historical
snapshots. PR8 integrated the initial source; subsequent migration work adds the
replacement update path and complete Ubuntu/Debian package. Current workflow
results and release checksums establish which exact binary was published.
