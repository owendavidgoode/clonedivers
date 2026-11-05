> Integrated source status: Windows1.9.8, Linux0.6.0-preview.8 and bridge0.3.7
> have a published launcher/source prerelease. Linux gameplay/account capture and
> separate-PC multiplayer remain unqualified. The owner manually disabled host
> mod sharing and the Windows launcher now shows Ready to launch. See FORGE.md
> for the current setup and update policy. The dated sections below preserve the
> earlier implementation checkpoints; private/unpublished statements there do
> not describe the current launcher/source release.
> November4 23:52 UTC deployment update: owner cancelled the timer and authorized
> deployment. The app automation is confirmed PAUSED. Exact r9 Windows/Linux/source
> archives are published as a separate experimental prerelease at
> https://github.com/owendavidgoode/clonedivers/releases/tag/forge-preview-2026-11-04
> Stable feeds/latest remain unchanged; no restricted mod adaptations were shared.
> Host sharing OFF, permitted peer onboarding and actual multiplayer/Linux testing
> remain open. Earlier private/unpublished statements below describe the staging
> checkpoint and are superseded only for this limited launcher/source publication.
# Forge tester candidate — November 4, 2026

This is a private review candidate. No public update feed has changed. Windows
1.9.1 has reached the ship with the owner's imported level 85 account and passed
official End session cleanup. Linux 0.6.0-preview.8 is cross-built source work:
it requires a real Linux qualification trial before a player rollout.

## Compatibility and installation

Use a separately installed, owned HellForge 0.3.0-beta, engine 0.1.41, SDK 15,
game build 25480438, with its supported network isolation and sandbox enabled.
Obtain HellForge from its official author. This package contains no game,
account, engine or third-party gameplay mod archives. Keep Steam installed,
running and signed into the account that owns HD2.

Windows: extract the complete candidate into a new folder outside Steam and
HellForge, then open Clonedivers.exe. Choose the separate HellForge installation
with Locate. Do not replace an active launcher. The desktop retains Regular,
Clones and Empire, Delta armor, CIS/Covenant and full/lighter texture choices.
Regular here means Forge gameplay with regular appearance. The official online
game remains available directly in Steam. The --steam argument opens the legacy
launcher workflow; it is not a way to send Forge progression to HD2.

Linux: the complete x86-64 archive must keep clonedivers and its adjacent forge/
directory together. Run ./clonedivers --gui (Forge is the package default).
Use ./clonedivers --steam --gui for the preserved Steam workflow. A dedicated
Proton prefix is required, with Forge inside its pfx/drive_c folder, outside
Steam's game and live prefix directories. Linked installation/resource/cache
paths are refused. ARM64, Deck, Flatpak/Snap Forge and native Linux account
capture have not been qualified. The official namespace scripts launch one
persistent Windows helper; do not invoke Wine directly or kill its process tree.
Namespace/security or authentication prompts require the player to act manually.

The Linux package includes GPL-3.0 source scripts and COPYING. Only the wrapper's
process-lifetime detection is adapted; the namespace/Steam/security code stays
pinned. The provenance manifest records exact original and staged hashes. Native
terminal --help/status/mods/account diagnostics exist; terminal gameplay mutation
and launch are not exposed in this candidate.

## Account and mod prerequisites

Every player needs their own verified, one-way account import through the
official backend. Never share captures, profile folders, import receipts or seal
keys. Forge progress saves locally and does not count in the official HD2
account. Importing again can replace the local state; do not reimport merely to
test persistence. Linux capture is a qualification blocker, not an unlock-all or
copy-another-player's-account fallback.

The gameplay preset is mandatory even for Regular appearance. See
FORGE-MOD-COVERAGE.md and preset.json. Prepare Star Wars pack handles appearance;
it does not install the gameplay dependencies. The current owner installation
has the 12-mod Clones preset. New installations remain blocked until author
original acquisition and reproducible, permitted local Heavy/Squad adaptation
are complete. Do not redistribute the owner's repaired archives.

Use native Smarter SEAF, Heavy SEAF plus Squad Stratagems, the bundled Editor,
D11 and Vanilla Plus, Bingus loader 20, Mod Options Menu 1.2.1, Guard Dogs 5.2.0,
Operation Reroller 0.1.2 and mine-boundary fix 0.1.1. Turn Targeting Marker OFF;
keep the dog/turret intelligence and safety options ON. Start Heavy at one wave
per 120 seconds, additional population zero. Leave optional/conflicting mods OFF. In the official Mods page → When I host, turn OFF “Share my gameplay mods with players who join” before using private Heavy/Squad adaptations. Every peer must obtain permitted matching mods separately; peer-to-peer mod sync is not a redistribution permission.
Do not enable Vanilla Plus and Beacon Fix together, Lua and native Smarter SEAF,
multiple troop spawners/rosters, or a default FRV replacer alongside D11's FRV.

## Controlled player acceptance

1. Confirm version/build, own imported level/Warbonds, selected mode, exact mod
   versions/options and sandbox/isolation. Ready checks presence and selected
   options; it is not complete content attestation or multiplayer certification.
2. Launch, reach ship, change equipment, then enter a controlled mission. Record
   planet, faction, difficulty, weapons, all stratagems, booster and reroller use.
   Observe Heavy waves and each Squad call-in; check marker-off dog/turret combat.
3. Complete/extract if possible. Record rewards and an ordinary purchase, close
   through End session and reopen to check actual progression persistence.
   Never manufacture rewards or reset the imported profile to make a test pass.
4. On separate PCs use the same pinned engine/game and complete mod versions,
   order, options and appearance cohort. Compare the full preset, not just the
   native-code digest. Use Forge's official multiplayer flow; record actual
   invite/join, host/client start, drop, extraction, reconnect and host departure.
   Test 2, 3 and intended full squad size. Steam ownership/transport does not imply
   vanilla public matchmaking compatibility. Upstream third-player issue #202 and
   experimental Linux/Windows co-op remain acceptance risks.
5. Report initialization, observed behavior, mission completion and multiplayer
   as separate results. Windows ship startup and solo death do not qualify them.

End session waits for official cleanup. Native in-game Quit hanging remains open;
the launcher control is the tested recovery. If cleanup fails, retain diagnostics
and do not repair staged gameCopy=false or start another session blindly. Ship
rendering distortion remains open, including a previous zero-mod reproduction.

## Update channels, rollback and reporting

This cohort includes capacity checks, cancellable bounded copying and
verified appearance backup/recovery. A handled failed appearance replacement uses
the official CLI to restore the previous files, switches, order and backend
choices. Account/profile data and privacy settings are never restored from an
old snapshot. Changed backups, unknown CLI outcomes and active sessions defer
recovery instead of replaying a mutation blindly. The private backup path and
outcome remain available in diagnostics.

The earlier Windows1.9.3 official-CLI reimport preserved the complete owner preset,
profile, import receipt, Steam fallback and settings byte-for-byte. Failure and
rollback cases were tested with small isolated command stand-ins; no destructive
failure was induced on the owner's installation. Whole-process interruption
recovery is fixture-tested with an abruptly exited test process; it has not been
destructively induced on the owner installation. Do not delete an in-progress
backup or retry over an unknown CLI. Native pack preparation allows time for its
bounded recovery reply;
an ambiguous connection timeout still does not imply rollback.

If an appearance operation is interrupted, both frontends block launch and offer
**Recover previous appearance** on the preparation button. The durable record is
bound to that installation, including Windows path-case variants. Recovery waits
for the game and any HellForge CLI to finish, verifies saved metadata/files, and
restores only the previous appearance, switches/order and backend choices through
the official CLI. Account/profile and privacy files are preserved. Changed
unrelated gameplay mods or damaged/foreign recovery records require review rather
than automatic replacement. Diagnostics and End session remain available.
Keep the displayed backup and recovery record for review; replacing the launcher
with an older version does not itself complete an interrupted operation. A CLI
whose exit could not be confirmed also requires reviewing its state and restarting
the launcher/connection before recovery. Recovery never blindly kills that CLI.

Before idle changes or launch, both routes require the pinned HellForge/engine/
SDK and the official platform isolation plus sandbox status. Missing or changed
details produce a compatibility notice; no automatic engine or security change
is attempted. End session stays available for an existing active game even when
compatibility checks fail. Windows WFP status cannot qualify a Linux namespace.

CLI output is bounded while it is read:4×1024² text characters for JSON/
acknowledgements and64×1024 for stderr. A failed/oversized request confirms exit of only its owned CLI before
recovery; independent game/session descendants stay under official runner control.
The namespace bridge reuses the same reader. A request failure still requires
checking session state rather than assuming that no game was started.

Account backend reviews retain at most1024² characters of stdout and64×1024 of
stderr. Incomplete or oversized read-only reviews fail closed. Once an actual
account import has started, cancellation or noisy diagnostics never kills it:
excess output is drained and discarded until its process exits. A successful
exit keeps receipt bookkeeping intact; a nonzero exit remains a failure. No
destructive import test was run on the owner's account.

Our launchers serialize the whole account operation, including policy and receipt
bookkeeping, with a per-installation lease outside the profile. Read-only reviews
may share it; import, legacy receipt migration and session launch require exclusive
ownership. A busy client asks you to wait instead of merging or replacing state.
This supplements the official backend's profile lock; it does not certify arbitrary
third-party tools or actual Linux filesystem behavior from Windows tests.

The public Windows v1.7.3/live-game manifests and Linux v0.5.0 Steam preview remain
unchanged. A proposed separate **forge-preview-build25480438** cohort must carry
Windows 1.9.8, Linux 0.6.0-preview.8, bridge 0.3.7, engine/game pins and the complete
preset fingerprint. This is a proposed channel name, not a deployed endpoint.
Windows 1.9.8 blocks legacy-channel self-update from its --steam fallback. The
Linux Forge edition also blocks legacy release notices, cache reads/writes and
release-page actions in its --steam fallback. Do not change legacy pack build IDs
or let their auto-updaters overwrite this
candidate. Explicit migration and public publication approval are still needed.

Keep the previous executable/package outside the new folder. Before changing an
idle installation, privately back up its official profiles, receipts, mod options
and launcher preferences. If the candidate fails, End session first, restore the
previous launcher and verified preset through official mod controls. Do not
restore a stale profile merely to roll back a launcher. Steam fallback needs no
Forge profile conversion or live game-file edit.

Session details and official session logs should capture run ID, versions,
failure and cleanup outcome. Review logs for account identifiers/tokens/private
paths before voluntarily sharing them; this candidate does not automatically
upload diagnostics or enroll telemetry. Public release/feed/push and tester
invitations have not been authorized or performed.

Open rollout gates: author dependency/bootstrap permission and reproducibility;
real Linux runtime/account path; extraction/reward-spend persistence; marker-off
combat; intended-size separate-PC multiplayer; exact public channel/migration
implementation. A review archive is not a clean-machine-ready installer.
