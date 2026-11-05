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
# Current private review artifacts

Revision r9 pairs Windows1.9.8, Linux0.6.0-preview.8 and Windows namespace bridge
0.3.7. It is staged locally; nothing has been published or sent to testers.

| Artifact | Bytes | SHA256 |
| --- | ---: | --- |
| clonedivers-forge-windows-1.9.8-review.zip | 61177050 | 3f667a95f529fd16c51f350aac3d226c13df0c4a9b783cf1332399142b932f92 |
| clonedivers-forge-linux-x64-0.6.0-preview.8-review.tar.gz | 67434831 | c8fe5f8f896fd4359dba4e71ae6f0db4285b0f82fedfa7eb325d4385fec4efb3 |

Files live in
`D:\CodexScratch\hellforge\player-readiness-20261104\review-rollout-r9`.
Adjacent artifacts.json, archive-verification.json, channel-validation.json and
forge-channel-proposal.json retain the exact metadata and qualifications.

Windows has 10 verified payload files and Linux21, plus each archive's own
package-files.json. No extra files were found after extraction. Linux executable
and five shell scripts have mode755; data files644. The native binary is ELF64
x86-64. The author receipts, preset and licenses accompany each review package.
There are no owner accounts, profiles, seal keys, captures, game files or third-party
mod archives in them. Previous review revisions are retained and superseded.

The proposed forge-preview-build25480438 channel remains separate from public
Windows1.7.3/Linux0.5 Steam feeds. Tag and download URLs in the proposal do not
exist as a result of this work. Both launcher fallbacks block cross-channel
executable replacement/release offers. This candidate has no Forge auto-updater;
the proposal specifies complete-package manual updates and retained rollback.

The read-only rollout validator checks exact engine/game pins, isolation policy,
repository/tag/filename, archive bytes and SHA256. Five small fixtures cover blocked
publication, a wrong game build, a foreign URL and tampered same-size content.
Review validation passed with Publishable=false and RuntimeQualified=false.
Publication requires separate explicit approval and permitted mod onboarding.
Player-ready qualification additionally requires real mission/persistence,
marker-off combat, Linux/account runtime and intended-size separate-PC squads.

The owner sharing-OFF action remains pending: HellForge → Mods → When I host →
“Share my gameplay mods with players who join.” Windows1.9.8 intentionally blocks
the private Heavy/Squad preset while that option is ON. Automation has not changed
that privacy setting. See FORGE-TESTER-ROLLOUT.md and FORGE-MOD-COVERAGE.md for
acquisition, rollback and exact acceptance instructions.

The packages include durable interruption records, a verified official-CLI
recovery action and transactional Regular-mode preparation. Windows602 and
bridge196 full assertions passed; three additional native result-budget checks
passed. An abruptly terminated disposable test process qualified interruption
recovery against command fixtures. The earlier actual Windows1.9.3 same-preset
reimport preserved account, settings, preset and Steam fallback; no destructive
failure was induced on the owner's installation. Linux remains a cross-build:
no Linux gameplay or account-capture qualification is implied. Recovery refuses
changed evidence, unrelated gameplay choices, active sessions and outstanding
CLIs; diagnostics and End session remain available.

The shared client now rejects changed or missing HellForge/engine/SDK and official
isolation/sandbox details before idle mutation or launch. A compatibility notice
does not hide cleanup for an active session. No engine/security setting was
changed to exercise this guard; all adverse cases used fixture status responses.

Both routes now share the same reader, bounded during reading at4×1024² text
characters for JSON/acknowledgements and64×1024 for stderr. Eleven new fixtures
cover exact limits, line endings, live stdout/stderr floods, CLI exit confirmation
and independent child survival. Captured text limits do not describe the entire
process's memory use. No flood was induced on the owner's installation.

Account backend collection is also bounded. Nine additional Windows checks prove
oversized read-only reviews fail closed, noisy committing imports finish despite
cancellation, nonzero exits remain failures, and inherited diagnostic pipes do
not hide an exit0 outcome or kill independent descendants. The actual owner
read-only review passed; no owner capture was reimported to exercise commits.

Eighteen additional Windows checks exercise whole-account leases across separate
processes, shared reviews, installation/case identity, launch exclusion and
cancellation during a committing import. The backend's own profile lock remains
in place. These checks establish wrapper exclusion, not a reproduced production
double-commit or actual Linux filesystem qualification.

An additional private source handoff lives in
`D:\CodexScratch\hellforge\player-readiness-20261104\source-review-r9`.
clonedivers-forge-source-r9-review.zip is446729 bytes, SHA256
02e96ff989e01255713cae4999e9cc0eb56bf00287fa2b10ec50a1c5eaa4b75a.
It contains a complete53-file changed/new-source overlay and Git patch against
base6f755c209d4d91b4e54cd08eaae70f9bc6d5558e, plus manifest/build instructions.
The reconstructed base passed602 Windows/196 bridge assertions and native Forge
build; dependency restores with audit enabled passed. The staged Windows/Ubuntu
source-only workflow was not pushed or executed remotely. This qualifies source
reconstruction, not bit-identical binary reproducibility. Runtime r9 archives and
their hashes above are unchanged; source handoff contains no executable/game/mod/
account archives or private adaptation source.
