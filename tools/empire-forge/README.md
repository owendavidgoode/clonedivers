# EmpireDivers for HellForge (build 25480438)

Offline tooling that ports the Steam-era Empire revisions r20–r44 onto HellForge's game build and
verifies the result before any game run. Inputs (the old development checkout, the installed
HellForge game copy) are read only; outputs go to a separate workspace (`D:\ClaudeWork\empire-forge`).

- `empire_port.py inventory|build`: resource inventory and candidate build (32 archive triplets).
- `verify_candidate.py`: archive rules (aligned MAIN, Wwise slot bounds, type-contiguous rows,
  stream bounds), payload readback, dependency and external-provider closure, audio codecs, text.
- `triage/`: the r45 crash analysis (`TRIAGE.md`) and the variant builders used to bisect it live.

History: r45-v3 crashed on 3 of 3 live drops at `helldivers2.exe+0x2bd8ad` (an unchecked `0xffffffff`
index while resolving `g_medium_cape`, then `g_shock_trooper_cape`). Native cape UNITs that rely on
an external GeometryGroup do not survive being overridden from the consolidated pack; r19's inline
cape payloads do. r45-v4 restores those two UNITs and passed 3 of 3 live D11 drops. The verifier
now rejects that pattern. See `REPORT-r45.md` and `triage/TRIAGE.md`.

## r45-v4 Lighter textures

Run `tools/optimize-pack.ps1` from the launcher workspace with `-Source
D:\ClaudeWork\empire-forge\empire-forge-r45-v4` and `-OutDir
D:\ClaudeWork\empire-forge\empire-forge-r45-v4-lighter`, then run
`prepare_lighter.py --workspace D:\ClaudeWork\empire-forge --source-catalog
Clonedivers/Resources/hellforge-empire-r45-v4.json --output-catalog
Clonedivers/Resources/hellforge-empire-r45-v4-lighter.json`.

Preparation restores any small textures the optimizer compressed, proves exact
streamed chains and resident tails, preserves all other resource bytes, and runs
the complete candidate verifier. Evidence, hash-named assets and JSON/CSV upload
plans go to `D:\ClaudeWork\empire-forge\lighter-r45-v4`. The separate embedded
catalog preserves existing Full receipt identities. Lighter gameplay acceptance
and asset publication are still required before shipping the launcher.
