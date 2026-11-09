# Windows Forge setup — Clonedivers1.13.0

The primary action installs or updates official HellForge 0.4.0-beta and continues
into the pinned gameplay preset. Existing 0.3 / engine 0.1.41 / SDK 15 installations use
Update HellForge; unknown engines are not silently replaced. The new runtime is
engine 0.1.47/SDK16, still game 25480438.

The supported author `HellForge-Setup.exe --unpack <folder>` performs application
writes without an installer window. Its payload already has an install marker.
For an unmarked payload the launcher uses `create-install --root` once, before
validating the staged installation. Existing markers and profiles are retained.

Clonedivers drives `steam-download --out <root>\game --sign-in qr` in its setup
window. The pinned CLI prints ANSI half-block QR rows on stderr, without the
approval URL. WinForms reconstructs those modules at an integer pixel scale,
including the quiet zone, with no new QR dependency. The no-console CLI uses
Windows' OEM code page, which the reader explicitly matches. Challenges remain
in memory. When Steam already has build 25480438, `discover` and `inspect`
select `install-game --manifest ... --destination <root>\game` automatically,
so that copy needs no QR sign-in.
The player scans with Steam mobile > Steam Guard > QR scanner and approves.
HellForge reuses matching Steam chunks and verifies the supported build's files.

Progress and Pause download remain in setup. Pause stops and confirms the owned
CLI process. Continue invokes the same destination, so HellForge resumes its
verified files and partial chunks. Sign-in approval may be needed again. A
50 GB setup budget credits retained game bytes on resume and keeps at least
2 GB free. HellForge also checks its exact download plan. Failures preserve
saved files and offer retry plus an explicit Official setup fallback, including
SteamCMD and Steam-console choices for players without a phone or with a
borrowed licence. No download failure automatically opens that wizard.

Setup enables the official sandbox. HellForge's `play` checks ownership and
installs or verifies its network block before running the game. Its initial
Windows UAC approval remains a player action. A pre-existing WFP marker is no
longer required to reach the first launch. Unknown engines stay blocked.

The verified author installer is526467508bytes, SHA256
97dbdd07638e5198b98bf2ad74a1ba907c53f569fcdcdcff330e6c74359f04d7.
The update retains the separately verified0.3 installer and private account/profile
snapshots before application changes. Known failures restore the previous app;
unknown process outcomes keep recovery pending. Profile backups are not replayed.

Required mods, order, options, marker OFF and sharing OFF are automatic. Eight
archives are pinned in `forge-gameplay-build25480438-r3.json`, including optional
Clonedivers Clean Exit 0.2.0 enabled on first r3 setup. Its `gameplay: false`
manifest excludes it from co-op matching. Players can disable, replace or remove
it afterwards without setup re-adding it, including during required-mod repairs
or rollback. First setup also respects an already disabled copy. Completed
r1/r2 records remain readable; interrupted older setup keeps its backup for
review through recovery. The Clean Exit asset is staged and must be published
before launcher release, with the exact release pins listed in [Forge status](FORGE.md).
The older mine patch is disabled because the new engine includes it. An already
prepared same-build appearance is retained on application upgrade and its files
are checked at launch.

The main button now routes disabled Difficulty, changed preset order/options,
overlapping preset alternatives and sharing ON through the existing backed-up
gameplay transaction. Verified archives are reused. Catalog errors such as
duplicate custom IDs offer an explained review choice because the launcher
cannot choose which custom file the player wants to keep.

Account capture still needs Profile > Account sync > Sync Inventory in the
official app, Windows approval and passing HD2's title screen. After HD2 closes,
return to Clonedivers with Apply import and auto-apply off. The launcher selects
default and verifies and merges the completed sealed capture automatically.
The executable-interface investigation and exact upstream command request are
in `FORGE-ACCOUNT-CAPTURE-REQUEST.md`.

Validation on November 8 used fake CLI state/pipe fixtures with the game closed.
`dotnet run --project Clonedivers.Tests -c Release -- --turnkey-setup-only`
passed 76 checks, and `--hellforge-form-layout` passed 13. Bridge passed 212,
and Linux passed 99 in each edition. Linux builds used the process-local
`AVALONIA_TELEMETRY_OPTOUT=1` because the sandbox denies its external log folder.
NuGet vulnerability lookups were unavailable and produced NU1900 warnings.

Outside the sandbox the full Windows suite passes 775 checks, including the
long copy budget and reparse-point refusal; the two file-symlink partial cases
skip without the Windows symbolic-link privilege. Steam approval, real download
resume and first-launch UAC still need a fresh-player acceptance run. No game was launched and no existing HellForge
installation was modified for these changes.

Fresh installation, rollback/restart recovery, corrupt-cache repair and profile
preservation passed with the real 0.4 CLI. The owner's real application upgrade and
verified account preservation passed. The full11-mod Clones preset initialized
inside the Windows low-integrity sandbox. No new mission or real squad completion
is claimed. See FORGE-0.4-TURNKEY-2026-11-05.md.

Author contract: https://github.com/SkyeShade/HellForge/blob/c01f36b12a72ffb6f1af8e5096d2634bba2f2d7f/docs/external-launchers.md
Author release: https://github.com/SkyeShade/HellForge/releases/tag/v0.4.0-beta
