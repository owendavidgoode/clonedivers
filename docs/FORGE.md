# Clonedivers Forge preview

The [published test preview](https://github.com/owendavidgoode/clonedivers/releases/tag/forge-preview-2026-11-04)
contains Windows 1.9.8, Linux x86-64 0.6.0-preview.8, bridge 0.3.7, the original
source snapshot and SHA256SUMS.txt. Its launcher and platform source are integrated
in this repository. The release tag identifies the snapshot's base commit; the
source ZIP contains the changes. Building current main includes the integration
plus Windows 1.9.9's footer fix for short displays. The published 1.9.8 archive is
unchanged; a source merge does not replace its reviewed bytes.

This is a controlled test preview. The owner has used Heavy SEAF and Squad
Stratagems in a solo D11 mission, ending in death. Separate-PC multiplayer,
extraction/reward-spend persistence and Linux gameplay/account handling remain
unqualified. Software tests and source builds do not establish these results.

## Install and preserve the official game

Every player needs their own Steam-owned HD2 and an official, separate HellForge
0.3.0-beta installation: engine 0.1.41, SDK 15, game build 25480438, with supported
isolation and sandbox enabled. Keep Steam signed into that player's own account.
The packages contain no game, engine, account data or third-party mod archives.
Do not install them over Steam's game or change the pinned engine implicitly.

Windows: extract the complete ZIP into a new folder outside Steam and HellForge,
run Clonedivers.exe, and use Locate to select the separate HellForge installation.
Regular, Clones and Empire change Forge appearance. Use default Steam to play
official online HD2. Keep the previous launcher/package for rollback.

Linux: extract the complete TAR.GZ and keep clonedivers next to forge/. Run
`./clonedivers --gui`. Use the official HellForge Linux setup with a dedicated
Proton prefix and Forge under its pfx/drive_c, outside the live Steam prefix/game.
The companion runs through the pinned official namespace scripts; do not invoke
Wine directly or disable isolation. ARM64, Deck, Flatpak/Snap and actual Linux
account capture are not qualified. See [Linux implementation notes](HELLFORGE-LINUX-INTEGRATION.md).

Import only the current player's own verified HD2 account through the official
backend. The direction is HD2 → Forge only. Forge progress saves locally and does
not count back in official HD2. Do not share profiles, captures, receipts or seal
keys. A new import may replace local state; do not replay a stale capture to test
persistence or undo a software update.

## Gameplay dependencies and multiplayer testing

The launcher does not install the gameplay preset. New peers need permitted,
matching artifacts before they can reproduce the owner's setup. The public
Heavy/Squad originals required local repairs; their private adaptations and
source are not distributed. Author permission/build-compatible artifacts and
reproducible onboarding remain open. Do not substitute the originals silently.

See [mod coverage](FORGE-MOD-COVERAGE.md), the package's preset.json and author
receipts for exact pins, sources and conflicts. The baseline uses native Smarter
SEAF, Heavy + Squad, D11, Editor, Vanilla Plus, Bingus Shared Loader 20, Mod Options
Menu 1.2.1, Guard Dogs 5.2.0 with Targeting Marker OFF, reroller and the mine-boundary
fix. The original MIT [mine-fix source and fixtures](../tools/hellforge-mine-boundary/README.md)
are included here; no compiled mod archive is published by this merge.

In official HellForge Mods → When I host, manually turn OFF “Share my gameplay
mods with players who join” before using private adaptations. The owner has done
this and the local launcher shows Ready to launch. Every peer needs their own
permitted matching mods; mod sync is not redistribution permission.

First validate Linux startup on the available Linux PC, then attempt a Windows–
Linux two-PC test using Forge's official multiplayer flow. Record joins, drops,
SEAF call-ins, extraction, restart/persistence and reconnects. Then test three and
the intended full squad size. Upstream third-player issue #202 remains tracked;
do not disable the sandbox as a workaround. Follow the [squad acceptance checklist](HELLFORGE-SQUAD-ACCEPTANCE.md).

Use End session for official cleanup. Native in-game Quit hanging and ship
distortion remain open. An active gameCopy=false is staged session state, not a
reason to repair an active game.

## Updates and source verification

The Forge preview is a separate manual-update channel. Old Steam-edition Windows
1.7.3 and Linux 0.5.0 feeds remain unchanged; merging this source does not make old
launchers update to Forge. Forge editions block legacy executable-update offers.
Explicit migration and permitted dependency onboarding are still needed before
the old fleet can move. Download a complete Forge package into a new folder.

Build with .NET 8. Fetch the pinned PresentMon input for Windows builds using
`tools/fetch-presentmon.ps1`. Native Linux defaults to the preserved Steam edition;
pass `-p:ForgeEdition=true` when building/publishing the Forge edition, and stage
its complete companion resources with the reviewed tools. A standalone native
binary is not the complete package.

The source readiness workflow runs Windows and Linux fixtures with read-only
repository permissions. Local evidence before integration was 602 Windows
assertions, 196 bridge assertions and three separate native deadline checks.
The integration's short-screen footer regression check brings the local Windows
suite to 603 assertions. Ubuntu source fixtures also run real symlink cases;
those checks still do not exercise Proton or gameplay.
Two symlink cases were skipped on Windows and require Linux. Actual CI results
are visible on the pull request/Actions; neither fixtures nor compilation certify
gameplay, account capture or multiplayer. Review [tester instructions](FORGE-TESTER-ROLLOUT.md)
and [artifact provenance](FORGE-REVIEW-ARTIFACTS-2026-11-04.md) for the snapshot.
