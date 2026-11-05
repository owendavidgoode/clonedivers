# Windows Forge setup — Clonedivers1.13.0

The primary action installs or updates official HellForge 0.4.0-beta and continues
into the pinned gameplay preset. Existing 0.3 / engine 0.1.41 / SDK 15 installations use
Update HellForge; unknown engines are not silently replaced. The new runtime is
engine 0.1.47/SDK16, still game 25480438.

The supported author `HellForge-Setup.exe --unpack <folder>` performs application
writes without an installer window. Initial game-copy/ownership/network setup
still uses the official wizard; Clonedivers opens it and automatically continues
once ready. Steam/QR authentication and Windows approval remain player actions.

The verified author installer is526467508bytes, SHA256
97dbdd07638e5198b98bf2ad74a1ba907c53f569fcdcdcff330e6c74359f04d7.
The update retains the separately verified0.3 installer and private account/profile
snapshots before application changes. Known failures restore the previous app;
unknown process outcomes keep recovery pending. Profile backups are not replayed.

Required mods, order, options, marker OFF and sharing OFF are automatic. Seven
archives are pinned in forge-gameplay-build25480438-r2.json; the older mine patch
is disabled because the new engine includes it. An already prepared same-build
appearance is retained on application upgrade and its files are checked at launch.

Fresh installation, rollback/restart recovery, corrupt-cache repair and profile
preservation passed with the real 0.4 CLI. The owner's real application upgrade and
verified account preservation passed. The full11-mod Clones preset initialized
inside the Windows low-integrity sandbox. No new mission or real squad completion
is claimed. See FORGE-0.4-TURNKEY-2026-11-05.md.

Author contract: https://github.com/SkyeShade/HellForge/blob/c01f36b12a72ffb6f1af8e5096d2634bba2f2d7f/docs/external-launchers.md
Author release: https://github.com/SkyeShade/HellForge/releases/tag/v0.4.0-beta