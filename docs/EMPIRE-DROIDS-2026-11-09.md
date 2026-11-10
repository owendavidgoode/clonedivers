# Empire CIS droid option — 2026-11-09

Branch: `codex/empire-droids-20261109`, based on `96a25e5`
(`codex/empire-lighter-20261109`). The requested worktree already existed,
clean, at the correct commit. No other working tree was edited.

## What Empire players face

Static composition evidence, not new live visual acceptance:

| Faction | Shipped r45-v4 / fixed r46 | New checkbox off |
| --- | --- | --- |
| Automatons | CIS replacement models for covered infantry, vehicles and structures | Native Automaton models |
| Terminids | Native Terminids | Unchanged |
| Illuminate | Native Illuminate; Covenant forced off | Unchanged |

The CIS set includes B1 troopers/commissars/MG troopers, B2 berserkers and
devastators, MagnaGuards, dwarf/spider walkers, brown AAT/STAP/MTT, HMP
dropships/gunships and themed fabricators. Unconverted/newer enemy types can
remain native. Enemy audio, shared projectiles and shared VFX are not controlled
by this appearance checkbox. Off is not a promise of vanilla audio or weapons.

Evidence: `pack-recipe.json`, the retained deployment report
`docs/pack/2026.09.05-r3/deploy-report.json`, the original r46 catalogs and the
retained v76 production selector. The original pinned Empire selector forced
`droids=true`, `covenant=false`; all 96 Empire file rows were unconditional.
The option metadata alone did not make the droid setting switchable.

## Implementation

Both Forge UIs show **CIS droids** in Empire appearance, on by default, and
retain Full/Lighter texture selection. Windows selection keys include the
actual droid choice and a new catalog generation; old fixed-r46 readiness is
invalidated. Linux already persisted and transmitted this preference; its
shared bridge selector now honors it. Covenant stays off for Empire.

The embedded r46 catalogs use the existing `option: droids` and
`unlessOption: droids` file-row mechanism. Conditional Empire rows are pruned
before the existing selector can renumber them, preserving layers 1000–1031.
Receipts carry the actual options and file hashes and reject the opposite
setting. Regression checks cover UI wiring, defaults, persistence, bridge
arguments, selection keys, stable names and receipt mismatch.

`tools/empire-forge/prepare_droids.py` compares on/off source **hashes**, since
the retained production selector renumbered filenames. It identifies 836
CIS-only typed resources. Seven overlapping resources have shared providers
and stay unchanged. A separate hash-verified r19 source cross-check found no
additional CIS-only resource left in r46 outside this classification. Never
classify the installed r19 folder by filenames: its patches were renumbered.

## Candidate and evidence

Wrapper: `D:\ClaudeWork\empire-forge\empire-forge-r46-droids`.
Install one variant subfolder, never the wrapper or its evidence directory:

| Folder | Setting | Resources | Verification |
| --- | --- | ---: | --- |
| `full-cis` | Full / on | 6,781 | PASS |
| `native-automatons` | Full / off | 5,945 | PASS |
| `lighter` | Lighter / on | 6,781 | PASS |
| `lighter-native-automatons` | Lighter / off | 5,945 | PASS |

Each has a distinct private GUID/name and 32 archive triplets. On preserves
every existing r46 archive hash. Off removes only the 836 option resources;
all retained resource payloads match the corresponding source profile.
The full verifier passed archive rules, payload readback, GeometryGroup
residency, dependency closure and audio checks for all four variants.
Two inherited native missing-external-media observations remain unchanged;
there are zero new missing dependencies, codec mismatches or geometry findings.

Evidence under `evidence/`: `classification.json`, `r19-crosscheck.json`,
`build-summary.json`, each variant's `verification-summary.json`, and
`upload-plan.json`. The Full-on verifier was rerun after moving its files
into the isolated `full-cis` installation folder. Resource deltas:
`D:\ClaudeWork\empire-forge\deltas\droids.json` (old payload hashes and null
new hashes for each conditional removal, separately for Full/Lighter).

## Validation and handoff

- Python: `python -m unittest discover -s tools/empire-forge -p test_prepare_droids.py -v`
  — 3 passed. Uses the owner's Python 3.13; no dependency installation.
- Windows test project: `dotnet build Clonedivers.Tests -c Release -p:NuGetAudit=false`
  — passed, including compilation of the launcher and new checks.
- Bridge test project: `dotnet build Clonedivers.ForgeBridge.Tests -c Release -p:NuGetAudit=false`
  — passed with no warnings.
- Linux test project: `dotnet build Clonedivers.Linux.Tests -c Release -p:ForgeEdition=false`
  and `=true` (with `-p:NuGetAudit=false`, `AVALONIA_TELEMETRY_OPTOUT=1`)
  — both passed; existing nullable warning in the account fixture.
- The standalone Windows app build is blocked by absent `dist/PresentMon.exe`.
  No placeholder was substituted.
- The .NET suites were not executed: `helldivers2.exe` PID 26844 was active
  during this work, and shared rules require the game closed. No session was
  started, stopped or modified. Run all four specified suites after it closes.

Lab request: `D:\ClaudeWork\lab-queue\empire-r46-droids-20261109.json`.
It requests D3 Automaton drops on profile `clonedivers-armory`, both settings
and profiles, matched Stormtrooper loadouts and enemy close-ups. Close-ups need
attended movement or nearby enemies: qualified camera orbit targets the local
avatar, not an enemy. Unavailable close-ups must be reported as blocked.

No HellForge files, releases or public feeds changed. Nothing pushed.
No commit could be created: `git add` failed creating
`C:/Users/goode/OneDrive/Desktop/clonedivers/.git/worktrees/sw-empire-droids/index.lock`
with Permission denied. The worktree and branch exist; changes remain unstaged.
The orchestrator must stage/commit them with writable Git metadata and append
`Co-Authored-By: Codex (gpt-6.1-sol) <noreply@openai.com>`.
The catalog references **160 staged, unpublished assets (11,366,003,824 bytes)**
under proposed tag `pack-2026.11.09-r46-droids-files`. Publication, complete
launcher tests, live visual acceptance and owner ship approval remain required
before releasing this branch. Existing source r46 asset publication requirements
also continue to apply.
