# Complete preset installer work

The first Windows setup release omitted required gameplay acquisition. This is a
blocking onboarding defect: a working existing owner installation is not evidence
that a new player can install the full preset.

The complete preset has three official bundled mods, eight pinned downloads
(7,784,074 bytes total), and the selected appearance. Five downloads come directly
from authors. The Heavy/Squad compatibility packages use the owner's newly
confirmed written permission; the mine fix is original MIT source.

Implementation: common immutable artifact catalog, staged archive verification,
official CLI import/content hashes, order/options and conflict handling, durable
recovery, Windows first-run integration, native Linux downloads outside the
namespace, and bounded bridge import inside the prefix. Host sharing stays a
player-confirmed setting through the official manager. Account import remains
one-way and cannot be skipped or fabricated.

Windows setup now leads into the selected preset automatically. An unapplied
mode and an unfinished account import offer enabled primary actions rather than
a grey Launch button. Sync reads current verified state instead of relying on
the last UI poll. After a player requests capture, the open launcher completes
the verified import after the normal game and capture cleanup finish; startup
does not replay an old capture. Missing-capture diagnostics identify the selected
installation/default profile and missing files without displaying account data.

The pinned official external-launcher contract still reserves initial game-copy,
ownership/network setup and inventory capture for the native manager. No
unsupported headless wizard flags or direct security-settings writes are used.
Gameplay acquisition, CLI imports, options, load order and mode/co-op settings
are automatic. Official host-sharing OFF remains one explicit manager checkbox
because the pinned CLI does not expose it.

Linux Forge now has **Save support report** and `clonedivers --forge report`.
The ZIP labels the Forge workflow and includes last observed readiness/mods,
including connection errors, instead of reporting the Steam fallback as the
active installation. It excludes unrelated Steam recordings and raw account
data. A game crash still needs the separate official HellForge session export.

Evidence so far: a clean isolated official Forge installation started with only
the three bundled mods. Setup fetched/verified all eight missing artifacts,
imported them through the official CLI, configured the order and options, and
reported zero missing artifacts. The existing owner installation also verifies
against all eight pinned sources without modification. The fresh test has not
prepared a game copy, authenticated ownership, changed network isolation,
imported an account or run a mission; these qualifications remain distinct.

Failure, lost-acknowledgement/restart recovery, corrupted recovery metadata,
idempotency, unpacked-cache repair and profile-file preservation have passed with
the real official CLI in the isolated installation. The three permitted archives
and catalog are published as `forge-gameplay-build25480438-r1`; hosted size/digest
verification passed. The owner installation matches all eight pins without changes.

Before publishing the launcher: finish UI/docs/package checks
for Windows 1.12.0 / Linux 0.6.2 / bridge 0.3.9, run CI, then activate tested
artifacts/update feeds. Do not describe multiplayer as tested from these checks.
