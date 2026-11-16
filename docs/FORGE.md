# Clonedivers Forge status

Public downloads are Windows **1.14.0** and Linux x86-64 **0.8.0**. This
integration stages Windows **1.14.1** and Linux Forge **0.8.1** with native
controller **0.4.0**. Linux updates require installing the complete package.
See [the migration guide](FORGE-MIGRATION.md) for rollout and recovery.

The launcher now installs the complete pinned gameplay preset through the
official CLI. The staged r4 catalog pins seven required archives and two
optional client-only mods: Clonedivers Clean Exit 0.2.0 and Empire LEGO Yoda
Death 0.1.1 (prototype), with immutable URLs, size/SHA256 and official content
hashes. Three baseline mods come from official Forge setup.
The Heavy/Squad compatibility files are distributed under the owner's confirmed
written author permission, with credits retained. Five other dependencies are
obtained directly from authors. See [permission scope](SEAF-COMPATIBILITY-PERMISSION.md)
and the embedded gameplay catalog. Files are staged and verified before imports,
with backups and durable recovery. Accounts and game files are not bundled.

Clean Exit and Empire LEGO Yoda Death are enabled once on first setup (`r3`
upgrades keep prior Clean Exit choices while offering Yoda 0.1.1 once). Their
`gameplay: false` manifests keep them outside HellForge's co-op modlist
matching, so friends can leave them out. After setup, players can disable,
replace or remove either in HellForge. The completed setup record preserves
those choices across restarts, required-mod repairs and repair rollback.
Already disabled copies are also respected on first setup. Required artifacts
continue to be verified at launch; absent optional mods permit launch before
first r4 setup. Completed r1/r2/r3 records remain readable. Pending older-catalog
records retain their backup through the existing recovery-review path.

This catalog is staged for release. Publish the exact 8192-byte
`D:\ClaudeWork\forge-sdk\dist\clean-exit-0.2.0.zip` as `clean-exit-0.2.0.zip`
on release `forge-mods-2026-11-08` (SHA-256
`6835354fff8264c6f29c33dfac71a0b6e4c4da7a401920bb76e1b604d2435932`, content hash
`0006a02101583cf05e3bd9fa4e431409c18ff4ac72f59ae1ece8c7d9831a888a`, planned URL
[the Clean Exit release asset](https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-08/clean-exit-0.2.0.zip))
and the exact 45502-byte
`D:\ClaudeWork\forge-sdk\dist\empire-lego-yoda-0.1.1.zip` as
`empire-lego-yoda-0.1.1.zip` on release `forge-mods-2026-11-15` (SHA-256
`2a5b439a0ca9ab486ffa99b1b7fd650fd12f98b578c7acdbe903864336c7b03f`, content hash
`95cc05bd87855c6d06251fdaf3b7687a0ad9494dee4922d818b14a0feb697706`, planned URL
[the Empire LEGO Yoda release asset](https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-15/empire-lego-yoda-0.1.1.zip))
before shipping the launcher. Yoda 0.1.1 passed 122 offline checks; live death
routing and audibility remain pending.

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
recovery only; ordinary official HD2 launches through Steam. Clean Exit 0.2.0
produced clean quits in the reported 20-cycle D11 soak. Ship distortion remains
open; official End session cleanup remains available.

[The original preview](https://github.com/owendavidgoode/clonedivers/releases/tag/forge-preview-2026-11-04)
and its [artifact provenance](FORGE-REVIEW-ARTIFACTS-2026-11-04.md) remain historical
snapshots. PR8 integrated the initial source; subsequent migration work adds the
replacement update path and complete Ubuntu/Debian package. Current workflow
results and release checksums establish which exact binary was published.
