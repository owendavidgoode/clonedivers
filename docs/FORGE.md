# Clonedivers Forge status

The replacement launcher is Windows **1.12.0**, Linux x86-64 **0.6.2**, bridge
**0.3.9**. Existing Windows update feeds point to the new default Forge executable;
Linux0.5 notices the complete0.6 release and requires package/manual installation.
See [the migration guide](FORGE-MIGRATION.md) for rollout and recovery.

The launcher now installs the complete pinned gameplay preset through the
official CLI. Eight required archives have immutable URLs, size/SHA256 and
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
