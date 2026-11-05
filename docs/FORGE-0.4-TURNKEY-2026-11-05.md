# Forge 0.4 integration and setup

Target: Windows 1.13.0, native Linux 0.7.0, controller 0.4.0. Runtime: author
HellForge 0.4.0-beta (payload d8a8b46), engine 0.1.47, SDK 16, game 25480438.
Release source c01f36b12a72ffb6f1af8e5096d2634bba2f2d7f. Nightlies are not used.

The release adds Linux relay support/native setup, the sandboxed 3/4-player squad
fix, D11 loading fixes and improved session shutdown/account capture. This is an
explicit runtime migration, not a compatibility bypass for 0.3 scripts.

Windows setup now handles the author application update, private state snapshots,
verified previous-installer rollback and pending-outcome recovery. Existing
same-build appearances are kept and verified at launch. Linux setup handles author
installer acquisition/extraction, package-tool approval and default-root discovery;
its native controller calls the installed native CLI/backend. The official runner
owns every actual namespace, relay, sandbox, ownership and game staging check.

Seven pinned additional archives plus three bundled mods and one appearance make
the 11-mod Clones preset. Heavy/Squad artifacts remain the exact permission-cleared
repairs. Five other dependencies come directly from authors. The standalone mine
patch is disabled because the engine integrated the fix; it is not deleted.
Sharing OFF uses the official preferences-file contract with atomic replacement,
unknown-field preservation and transaction recovery. No manual checkbox is needed.

Account imports remain one-way, sealed for the current account/PC and backed up by
the official backend. Startup never applies a stale capture. When linking a
character imported through native Forge, an older capture is rejected and a fresh
capture uses forward merge without replacing ship/onboarding settings. The owner
account was not reimported, reset or given rewards during the engine migration.

Local evidence: Windows source/update and interrupted-process recovery fixtures;
real 0.4 CLI clean seven-archive installation and content verification; real failure,
lost-acknowledgement/fresh-client recovery, no-op, corrupt-cache repair and profile
preservation; owner 0.3→0.4 application upgrade with game/account intact; all eleven
mods initialized inside the Windows sandbox. Input automation did not pass the
local title screen, so this is initialized/title-screen evidence, not a ship,
mission or multiplayer result. Official End session completed cleanup, using its
30-second forced fallback when the title screen did not close on request. Native
Quit is not certified fixed by that test.

Native Linux source/protocol/archive/package checks do not qualify actual account
capture, Proton gameplay or cross-PC co-op. Test every squad size separately. The
Linux tester reports the newer Forge works, but a full current-preset Windows/Linux
squad acceptance record is still required.

The standard flow no longer requires manual downloads, mod imports, host-sharing
settings or namespace commands. Players still handle Steam/system approval and
the official first game-setup/account-capture UI; no unsupported headless wizard
or capture flags, security disabling, auth bypass or live Steam-file patching is
part of this integration.