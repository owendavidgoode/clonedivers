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
# Player readiness loop — November 4, 2026

The owner asks whether all Clonedivers endpoints are deployed, whether the Forge
launcher closely matches the old UI, whether Windows/Linux players can test,
whether the appropriate discussed mods are installed, and whether edge cases have
been anticipated/corrected. Continue implementation and QA until **18:00 Chicago
November4 =00:00 UTC November5**, with final verification/handoff at the deadline.
Resume saved work; do not repeat passing tests/startups/hashes without new cause.

## Starting evidence and preserved state

- Installed Windows1.8.10 is private/local,514 assertions, actual Launch/End session
  pass. Default account import is owner-verified; no stale reimport/reset/rewards.
  Original12-mod Republic/Delta/full+CIS, marker OFF, WFP/sandbox/engine0.1.41 /
  HF0.3.0-beta/game25480438 and lab/Steam fallback remain pinned.
- Morning handoff/overnight ledger retain exact previous evidence. Gameplay,
  extraction/reward-spend persistence and separate-PC multiplayer are distinct
  from initialization/startup. Native Quit and ship distortion remain open.
- New baseline/source/account/settings snapshot, excluding distributable auth:
  D:\CodexScratch\hellforge\player-readiness-20261104\baseline.
- Preserve the heavily dirty Windows workspace. Public main has Linux0.5.0 code
  absent from this checkout. Isolated branch codex/hellforge-player-readiness-20261104
  at6f755c209d4d91b4e54cd08eaae70f9bc6d5558e lives at
  D:\CodexScratch\clonedivers-player-readiness-20261104. No merge/pull/reset over
  dirty work. Read any applicable AGENTS/skills before editing new areas.
- WSL executable exists but reports WSL is not installed; no Docker/Wine/gh CLI
  found on PATH. Do not install OS features or claim native Linux gameplay from
  Windows cross-builds. Linux has an existing Avalonia desktop + terminal package,
  linked shared pure core, Linux CI and Ubuntu package checks to reuse.

## Initial findings

Public GitHub latest Windows isv1.7.3. All3 public manifests advertise that binary:
manifest.json format2/r8/build24826606; manifest-v3.json format3/r25/build26251122;
manifest-v3-current.json format3/r25/build26251122. The Forge pack is pinned25480438.
Do not retarget these legacy channels blindly. Linux previewlinux-v0.5.0 exists,
withx64/arm64 deb/tar.gz, update notices and reported Ubuntu26.04 gameplay for the
Steam workflow. Forge parity is absent/unqualified. Fetch once/store observations
and verify current endpoints before an actual approved rollout.

The old Windows UI screenshot is docs/screenshots/three-modes-1.7.png. Current
Forge screenshot is D:\CodexScratch\hellforge\qa-20261104\1.8.10-running.png.
Colors/mode cards match; narrower/taller layout, smaller cards, extra actions and
scrolling differ substantially. Bring the familiar layout/primary action/options
back while keeping account sync and the tested End session behavior.

Guard Dogs and Armored contain no-redistribution terms. Audit exact licenses for
all player dependencies/private repairs. Obtain restricted originals from their
authors; do not publish private modified archives or invent permission.

## Queue

1. READ01: Finish a dated endpoint/platform/mod/license matrix from actual public
   feeds/releases, source and artifacts. Include telemetry health only (GET),
   update routing and clean-machine onboarding. No metric upload/enrollment.
2. READ02: Restore Windows visual/interaction parity with the old UI in a new
   staged candidate. Keep Regular/Clones/Empire, one-way account-only flow, explicit
   profiledefault, idle guards, End session, useful status and accessible controls.
   Check compact/normal/high-DPI/window-size and keyboard states without expanding
   advanced controls into the main flow. Regressions only for concrete defects.
3. READ03: Reuse the existing Linux Avalonia/terminal source in the isolated
   worktree for a Forge backend/preset/account/session path. Delegate engine/copy/
   network/session work to supported official HellForge Linux scripts/CLI.
   Preserve Steam-native/Flatpak/Snap routing for the fallback; do not let Forge
   operations edit live Steam files. Study official upstream Linux limitations
   before designing account sync. Cross-platform code/package checks and actual
   Linux runtime evidence must be labelled separately. Reuse pinned CI without
   pushing/dispatching until publication approval is explicit.
4. READ04: Stage a complete reviewer-ready Windows/Linux tester rollout: exact
   artifacts/checksums, author dependency acquisition, repeatable preset/options,
   channel/migration plan, clean install/update/repair/rollback, diagnostics and
   concise tester instructions. Never include profiles/seals/keys/private captures.
   Keep old Steam channels and fallback recoverable. No new public release/feed,
   push/commit or publication is authorized merely by the owner's audit questions;
   ask for explicit approval only after the concrete rollout is ready to review.
5. READ05: Review previously discussed mod gaps, compatibility, dependencies and
   licenses. Core Heavy+Squad/Smarter/GuardDogs/Reroller/mine fix stays baseline.
   BeaconFix is exclusive withVP; Armored handling/Goliath remain isolated optional
   trials, Commander is gated on exact SEAF Drop, Pick/SP Companion excluded from
   squads, Supreme Command rejected. No random expansion or unverified substitutions.
6. READ06: Exercise/fix concrete edge cases: fresh machine/missingHF/failed setup,
   spaces/non-ASCII/links/case-sensitive paths, active-session detection, stale
   caches/selections, cancellations/partial imports/downloads, disk shortage,
   compatibility/update channels, wrong profiles/accounts/restores, shutdown
   cleanup, restart/save persistence and option/preset conflicts. Use small isolated
   fixtures and controlled official sessions; no fake rewards or live patchers.
7. READ07: Finalize docs/artifacts/evidence and explicit platform acceptance gaps.
   At18:00 Chicago stop tests, clean unattended sessions officially and pause the
   automation. If blocked on manual input/publication/Linux PCs, continue other
   useful authorized work; do not loop unchanged tests or certify missing evidence.

## Boundaries

Use official CLI for mods/settings/sessions, official backend for seal/import,
supported computer-use APIs for UI. Recheck processes/session before changes;
active gameCopy=false is staging, never repair. Keep private snapshots before
changes and retained prior EXEs before local promotion. No security/auth dialog
automation, bypass, driver/security change, gated-download bypass, post-EAC live
patcher, friend message/invite, profile reset or reverse HD2 progression write.
Actual multiplayer requires separate PCs. No silent engine/sandbox upgrade.
No subagents unless the user or applicable instructions explicitly request them.
Save readiness-ledger-20261104.json and a dated handoff after meaningful chunks.

## Readiness checkpoint — 2026-11-04T21:10Z (15:10 Chicago)

This checkpoint summarizes the owner's five readiness questions. It is a draft
for the18:00 handoff, not a claim that remaining physical acceptance has passed.

| Question | Current evidence and answer | Remaining work |
| --- | --- | --- |
| Deployed to every Clonedivers endpoint? | No. Installed local Windows1.9.8; private reviewr9 pairs Linux0.6.0-preview.8/bridge0.3.7. Public Windows feeds still route1.7.3/live-game assets; public Linux0.5.0 remains Steam workflow. Initial endpoint/telemetry GET audit is saved. | Permitted mod onboarding, compatible separate channel/migration, explicit publication approval. No feed/push/release/telemetry enrollment performed. |
| Does the launcher resemble the old UI? | Familiar Regular/Clones/Empire cards, helmet art, blue selected state, primary launch, Delta/CIS/Covenant and texture choices restored. Windows normal/compact/layout/keyboard/lifecycle checks and actual idle UI passed. Account/End-session controls remain. | Native Linux runtime display/input is not qualified by Windows-host Avalonia previews. |
| Ready for Windows and Linux players? | Windows is a controlled local candidate with imported owner account and retained rollback.602 integrated Windows/196 bridge assertions pass; Linux x64 cross-build and review archives pass. | Manual owner sharing-OFF action; author/bootstrap gates; real mission/extraction/spend persistence/markerOFF combat; actual Linux capture/runtime and separate-PC squads. No general player-ready certification. |
| Installed everything appropriate? | Core12-mod Republic/Delta/full+CIS baseline preserved: Heavy+Squad, native Smarter SEAF, Guard Dogs, Reroller, boundary fix and their loader/options/bundled dependencies. Target markerOFF, other dog/sentry intelligence/safetyON, conservative Heavy population. | Commander exact SEAF Drop is Ayaka-gated. Armored/Goliath remain OFF optional trials. Beacon Fix overlaps active VP. Pick/SP Companion excluded from squads; Supreme Command owner-rejected. No unverified port/new expansion substituted. |
| Edge cases anticipated and corrected? | Protected one-way account identity/stale-capture rules, queue/close/End priority, root/path checks, gameCopy=false active staging, capacity/cancellation/hash checks, official-CLI import rollback plus durable crash recovery, version/isolation pins, bounded CLI/backend output, committing-import lifetime and whole-account leases tested. | Tests have explicit scope;2 actual symlink cases skipped. Native Quit/ship distortion remain open. No solo/source/cross-build result certifies multiplayer, Linux or physical progression persistence. |

Current review hashes/artifacts and all publication/runtime gates are in
FORGE-REVIEW-ARTIFACTS-2026-11-04.md and the private review-rollout-r9 metadata.
FORGE-TESTER-ROLLOUT.md and HELLFORGE-SQUAD-ACCEPTANCE.md supply the controlled
physical acceptance sequence. Mod versions/dependencies/terms are in
FORGE-MOD-COVERAGE.md and forge-author-receipts-20261104.json. The author brief
is prepared but unsent; private adaptations are not in the packages.

All three appearance modes share profiledefault and one-way HD2→Forge import.
Forge progress stays local; the official game opens through Steam. No stale
capture reimport/profile rollback is used to test persistence. Actual normal pack
reimport evidence is1.9.3; most recent actual ship/End evidence is1.9.1. Current
1.9.8 real evidence is idle status/mod/account review. No unattended game remains.

Continue until18:00 with useful new evidence/owner responses; do not repeat
passing tests or regenerate the sealedr9 without cause. In the last20 minutes,
confirm owned-session cleanup, preserve the verified preset, finalize this
checkpoint against the final ledger and pause the automation.
