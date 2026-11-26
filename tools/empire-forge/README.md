# EmpireDivers for HellForge (Build 25480438)

Offline tooling that ports the Steam-era Empire revisions r20–r44 onto HellForge's
game build (`25480438`) and verifies the result before any game run. Inputs (the old
development checkout, the installed HellForge game copy) are read-only; outputs go
to a separate workspace (`D:\ClaudeWork\empire-forge`).

- `empire_port.py inventory|build`: resource inventory and candidate build (32 archive triplets).
- `verify_candidate.py`: archive rules (aligned MAIN, Wwise slot bounds, type-contiguous rows,
  stream bounds), payload readback, dependency and external-provider closure, audio codecs, text.
  (Running against full candidate folders uses local native/expected pins under `D:\ClaudeWork\empire-forge\`
  which are not checked into git.)
- `prepare_droids.py`: hash-based `droids` on/off resource classification and variant build helper.
- `prepare_lighter.py`: restores small textures compressed by `tools/optimize-pack.ps1`, verifies
  streamed chains and resident tails, and prepares Lighter metadata/upload plans.
- `triage/`: the r45 crash analysis (`TRIAGE.md`) and the variant builders used to bisect it live.

History: r45-v3 crashed on 3 of 3 live drops at `helldivers2.exe+0x2bd8ad` (an unchecked `0xffffffff`
index while resolving `g_medium_cape`, then `g_shock_trooper_cape`). Native cape UNITs that rely on
an external GeometryGroup do not survive being overridden from the consolidated pack; r19's inline
cape payloads do. **r45-v4** restores those two UNITs and passed 3 of 3 live D11 drops. The verifier
now rejects that pattern. See `REPORT-r45.md` and `triage/TRIAGE.md`.

## Shipped Catalog vs Historical Staging Resources

In `v2.1.5` / `linux-v0.10.5`, the shipping applications (`Clonedivers`,
`Clonedivers.ForgeBridge`, and `Clonedivers.Linux`) embed:
- **`Clonedivers/Resources/hellforge-empire-r52.json`** (`EmpireDivers 2026.11.25-r52`, `681` rows across `5` texture tiers × `3` droid skins × `3` Covenant/Squid skins × Encrypted Death Trooper voice)
- **`Clonedivers/Resources/hellforge-clones-r23.json`** (`Clonedivers 2026.11.25-r23`, `640` rows across `clonedivers`/`commandos` × `5` texture tiers × `3` droid skins × `3` Covenant/Squid skins)
- **`Clonedivers/Resources/forge-gameplay-build25480438-r12.json`** (`18` verified gameplay & SDK-16 code mod archives, `8,521,273` bytes)

Earlier `r45-v4`, `r46`, and `r47` manifests remain in `Clonedivers/Resources/` and `Clonedivers.Tests.csproj` for backwards-compatible receipt, option-gating, and migration tests. Historical `r47` notes follow below.

## r47 Full and `r47-shuttles` Candidate

- **`D:\ClaudeWork\empire-forge\empire-forge-r47`:** `102` files (`8,734,570,645` bytes total;
  `96` archive files = `8,734,568,240` bytes, `6,880` resources across `32` archive triplets).
  Removes both external-`GeometryGroup` cape UNIT overrides (restoring native cape geometry
  including Foesmasher while preserving hidden Gilded Quill), adds Stormtrooper SEAF (`2` UNITs),
  three Phase II clone sets (`99` resources), `crew66` (`5a646eee21712c8e`), Stormtrooper lower legs
  (`620f7dcb7f6cd8f2`, `ca672d99456b3ffe`), HD cargo Lambda (`75be82ed8592a6b3`), US text updates,
  and red Spear trails. Passes `verify_candidate.py`.
- **`D:\ClaudeWork\empire-forge\empire-forge-r47-shuttles`:** `102` files (`8,738,709,413` bytes
  total; `96` archive files = `8,738,707,008` bytes). NTFS-hardlinks `100` unchanged files from
  `empire-forge-r47` and overlays the two `empire-forge-r47-lambda-fix` delta files
  (`9ba626afa44a3aa3.patch_1030` `3,572,640` bytes and `9ba626afa44a3aa3.patch_1030.gpu_resources`
  `302,305,408` bytes; `+4,138,768` bytes total) repairing SEAF personnel Lambda `18f11a4e18192cfe`
  with `+0.200000 m` ground clearance above the native gear plane while leaving extraction Pelican
  `ef3a4136b21592cb` (`patch_1011`) unchanged. Passes `verify_candidate.py` (`0` missing deps,
  `0` codec mismatches, `975` UNITs).
- **Staged `r47-shuttles` release catalog and upload manifest:** Generated in
  `D:\ClaudeWork\workstreams-20261115\r47-shuttles-catalog\` (`hellforge-empire-r47-shuttles.json`
  and `UPLOAD-MANIFEST.json`): reuses `34` files (`455,980,288` bytes) from published
  `pack-2026.11.08-r45-v4-files` and plans `62` uploads (`8,282,726,720` bytes / `~7.71 GiB`)
  to unpublished `pack-2026.11.09-r47-files`. Wave 2's **`W4` `12+12` D11 Automaton A/B gate**
  passed (`14` cycles, `0` crash events vs Arm A `r45-v4` `12` cycles / `1` `+0x1e6b3e`
  event), and Gate 3 `release-preflight` passed; promotion awaits Wave 3 `R1` upload approval
  and `R2` attended visual clearance verification (feeds remain unchanged until upload).
- **Unshipped 4-variant `r47 (+ lambda-fix)` matrix:** Earlier pre-fix Lighter/CIS-off variant
  payloads were retired after an earlier Lighter in-mission freeze without measured RAM/VRAM
  savings. During Wave 2 offline variant verification, all four `r47 (+ lambda-fix)` packs
  (`Full` `r47-shuttles`, `r47-nodroids-lfix`, `r47-lighter-lfix`, and
  `r47-nodroids-lighter-lfix`) were built and `verify_candidate.py`-verified on `D:\` (`318`
  unique upload assets = `29,169,414,696` bytes + `34` reused `r45-v4` files), with conditional
  four-way UI staged on `claude/exp-variants-ui-20261115` (`0ed1ac8`); `Lighter` and
  `droids=false` remain outside the default shipping baseline pending `R1` and owner policy.
