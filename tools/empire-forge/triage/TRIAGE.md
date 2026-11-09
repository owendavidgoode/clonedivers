# EmpireDivers r45 crash triage

November 8, 2026. Offline analysis for Forge game build 25480438.

## Finding

The leading resource suspect is the **medium cape UNIT and its shared GeometryGroup residency**. The fault is an unchecked 16-byte array access using index `0xffffffff`. The preserved thin-name hash is `a61438bc`, which resolves to **`g_medium_cape`** with the retained Filediver dictionary and the game's Murmur64A high-32-bit thin-hash convention.

The two selected cape UNITs are byte-exact native resources with external geometry. Their common GeometryGroup is present in the native index, but absent from r45's consolidated startup-family overrides. r19 instead supplies complete inline cape geometry. The cape pair are the **only two changed/added r45 UNITs** that depend on a GeometryGroup supplied solely by native archives. This makes a render-mesh lookup against an unpopulated runtime cape the most likely interpretation. The producer of the invalid index and the dependency's actual runtime residency still require caller code or live variant outcomes. [Captured code](hellforge-dump.asm.txt), [cape contracts](cape-contracts.json), [changed UNIT contracts](changed-unit-contracts.csv).

## Evidence and timing

Read the authority report first from `D:\ClaudeWork\clonedivers-qa\docs\QA-SWEEP-2026-11-08.md`. The old C-drive checkout supplied read-only analysis dependencies, hash dictionaries and retained format/build recipes.

The original run is `D:\ClaudeWork\harness-home\runs\20261108-212200-mods-hellforge`. Its session log confirms HellForge 0.4.0-beta, engine 0.1.47, SDK 16, build 25480438 and pack `3098c19e-46e7-4779-8c1e-020106452438`. The matching larger HellForge dump is `hellforge-session\crash-10888.dmp`. The game dump and larger dump independently agree on thread 24100, exception, instruction pointer and registers. The executable SHA-256 is `f5fee03dcfdb2e553a4752c283590950ac13316b376d8196aa556ff0400d5f06`, matching the run receipt. [Parsed larger dump](hellforge-dump.json), [parsed game dump](game-dump.json).

The retained second run at `20261108-212452-mods-hellforge` repeats the same site and significant register values on thread 29412. During this analysis the cycle ledger also contained a third failure at `20261108-212715-mods-hellforge`, thread 18908. All three have `rdx=ffffffff`, `r8=1fffffffe`, `rbx=a61438bc`, and `r14=85d`, with different heap addresses. The observed sample is therefore three matching crashes. [Second dump](second-game-dump.json), [third dump](third-game-dump.json).

The loop ledger records ship commands returning zero, followed by mission commands failing because the process has exited. The first run's own receipt says `reached_ship=null`, `missions=[]`, and “never reached the ship”. Its last package messages request generated loadout packages immediately before the fault. These records establish an early failure before a recorded mission. They leave the exact war-table, ship-arrival or travel trigger uncertain. The first crash log time is November 9 at 03:22:50 UTC, corresponding to November 8 at 21:22:50 local time. Dump filename times differ from that crash-log time.

## Faulting instruction and memory context

These instructions come from the larger dump's captured executable bytes.

```asm
; helldivers2.exe RVA
002bd890  mov eax, dword ptr [rcx+0xc8]
002bd896  ret

002bd8a0  mov rax, qword ptr [rcx+0xd0]
002bd8a7  mov r8d, edx
002bd8aa  add r8, r8
002bd8ad  mov rax, qword ptr [rax+r8*8]  ; fault
002bd8b1  ret

002bd8c0  mov rax, qword ptr [rcx+0xd0]
002bd8c7  mov r8d, edx
002bd8ca  add r8, r8
002bd8cd  movzx eax, byte ptr [rax+r8*8+8]
002bd8d3  ret
```

For the first crash the arithmetic is exact.

```text
rcx/rdi                         0x228b1654590
qword [rcx+0xd0] = rax           0x2254de5e700
rdx                             0x00000000ffffffff
r8 = 2 * uint32(rdx)             0x1fffffffe
effective read = rax + r8*8      0x2354de5e6f0
effective read - table base      0xffffffff0
```

The supplied index is zero-extended to 4,294,967,295 and multiplied by 16, producing an offset of 64 GiB minus 16 bytes. The access-violation parameter equals that effective address in all three dumps.

The larger first dump captures the receiver. Its fields at `+0xc8/+0xcc` contain `0/4`, consistent with an empty array and capacity four. The pointer at `+0xd0` exactly equals `rax`. Four reserved entries contain a zero qword followed by flag byte one. The receiver's `u32` at `+8` equals `0x85d`, matching `r14`. Treating `0x85d` as an opaque runtime identity fits these bytes. Its semantic meaning remains unconfirmed. Nearby `Collidable_head` and `Collidable_hips` strings have no established ownership.

The accessor reads an object entry from that table. Its adjacent accessor reads the entry's byte at `+8`. The repeated `g_medium_cape` value, native cape descriptors and missing external geometry provider support the render-mesh interpretation. Confirming the caller's lookup/return convention requires its code pages.

### Caller-disassembly blocker

The requested stack values occur at `rsp+0`, `rsp+0x20`, and `rsp+0x50` for `+0x1c8eb9`, `+0x9d921`, and `+0x8a1332`. Additional stack values include `+0x669e34` and `+0x5c2e4b`. They are raw stack candidates with no completed unwind.

Their code pages are absent from the inspected dumps. The installed executable is WinLicense-packed on disk, as corroborated by the pinned HellForge source's `docs/multiplayer.md`. Searches of the reference source, HellForge installation and retained Clonedivers artifacts found no `exe.img`. Caller disassembly is blocked pending a retained unpacked build-25480438 image or a dump containing those pages. The delivered assembly files mark unavailable regions explicitly. All fault disassembly uses captured memory bytes.

## Resource suspects

The resource-level r19 comparison contains 559 changed/added UNITs, comprising 34 changed and 525 added. It also contains 233 added MATERIALs, 611 added TEXTUREs, and 13 changed English STRINGS tables. The complete visual/text list includes typed identities, resolved names, archive locations and `port-decisions.json` provenance. The broader delta has 4,205 selected changed/added resources. [Visual/text inventory](changed-visual-text-resources.csv), [all selected changes](all-resource-delta.csv).

### Highest-ranked cape resources

| Typed resource | r45 contract | r19 contract | Reason to test |
| --- | --- | --- | --- |
| `3c33cf10a26cbb3e.e0a48d0be9a7453f` | Native medium cape, MAIN 15,168 bytes, GPU 0 | MAIN 20,084 bytes, GPU 286,512 | Contains descriptor name `a61438bc`, matching the repeated register value |
| `ea846ef460cc5ba0.e0a48d0be9a7453f` | Native shock-trooper cape, MAIN 7,712 bytes, GPU 0 | MAIN 12,728 bytes, GPU 159,356 | Shares the same external geometry contract |
| `c6a69260cc389bcd.c4f0f4be7fb0c8d6` | Native-only dependency, absent from r45 pack | Inline geometry bypasses this dependency | Native owner family `18235e0c9ec0e636`, MAIN 14,896 bytes, GPU 5,908,982 |

Both cape UNITs are inherited from r41, with payload hashes exactly equal to freshly read Forge-native controls. Both reference the GeometryGroup at UNIT header `+16`. Both corresponding BONES resources are byte-identical between r19 and r45. The medium cape's four serialized descriptor names are `ff2f36fa`, `a61438bc`, `2a928fcd`, and `463c7e9c`. External geometry makes their runtime population dependent on loading the shared geometry resource.

There are also 111 later cape-policy texture additions. Their identities are enumerated in the visual/text CSV through source paths containing `cape-policy`. They rank below the native cape UNIT/geometry contract because the fault is a sentinel-indexed object-table read. A broad rollback of post-r41 assets would retain these two inherited cape UNITs, so the four controls concentrate on this narrower evidence.

### Ship and UI review

These ship-context UNITs remain secondary suspects based on scene association.

| UNIT identity, type `e0a48d0be9a7453f` | Change | Provenance |
| --- | --- | --- |
| `0c6567f9e37920e1` | Changed | Inherited r41 ship |
| `7dcb6f44af36ada1` | Changed | Inherited r41 background Star Destroyer |
| `bb3881db2f8d8164` | Changed | Later `ship-windows-v2` authored asset |
| `c8ccd4986bf70c7c` | Added | Inherited `content/env_ship/hangar/se_destroyer_supportcannon_planet_killer` |

Their material-map closure contains added MATERIALs `80c9a26670d38a4d`, `a0b8ad1e9d1049bf`, `b3d46a6558ab7bde`, and `f9592bccba149799`, with type `eac0b497876adedf`. The last comes from the later ship-window component. Added TEXTURE identities in that closure are `09eb3d83f1b529c6`, `5ba070aca22e302b`, `5e1dd83ac9fc0e37`, `6eee27e1e6997433`, `9d3599900740403f`, and `b67faa81f3444ae0`, with type `cd4238c6a0c69e32`. The CSV records each root ship association and provenance. [Ship-context inventory](ship-context-resources.csv).

The earlier ship-helper findings concern thin material slot `093fc934`. The existing verifier's native-equivalent helper proof passes for those sections. The present crash preserves cape mesh name `a61438bc`. The register evidence therefore ranks the cape path above those ship material helpers.

Resolved names in the selected delta produced no direct galactic-map or war-table UI matches. Of 4,205 delta rows, 1,827 names remain unresolved, so this name search has limited coverage. All 13 changed English tables were rebuilt against Forge keys. Their identities, with common STRINGS type `0d972bab10b40fd3`, are below.

```text
18056969d61dbfe9  1c978e745545d8ae  1da0d903c74893c8
4d8008b91190d1f1  62e20c40739e2dec  650f129823bb54f8
7c7587b563f10985  85eac40709cec253  9524ed24479d06f5
b35722607089772c  b6659775b3b94fcc  c4681eb2038587f1
d29d9f674db28566
```

## Delivered bisection variants

All folders are directly under `D:\ClaudeWork\empire-forge`. Each has the r45 flat layout, 32 patch triplets, five inherited attribution/notes files and one manifest, totaling 102 files. Each has a separate GUID/name and an empty Options array. Each control is independently derived from v3.

| Folder | Exact resource delta from v3 | Resources | Full verifier |
| --- | --- | --- | --- |
| `empire-forge-r45-triage-a-medium-r19` | Replace only medium cape UNIT with complete r19 MAIN/STREAM/GPU payloads | 6,780 | PASS |
| `empire-forge-r45-triage-b-native-capes` | Remove both cape UNIT overrides, allowing native providers | 6,778 | PASS |
| `empire-forge-r45-triage-c-cape-geometry` | Add exact native shared cape GeometryGroup and all GPU bytes | 6,781 | PASS |
| `empire-forge-r45-triage-d-native-text` | Remove all 13 English STRINGS overrides, allowing native text | 6,767 | PASS |

| Variant | GUID | Bytes |
| --- | --- | --- |
| A | `cc6dc325-947d-564d-9ee0-2598739b2ad5` | 8,435,312,339 |
| B | `36f22712-923e-5709-8547-232d8b39770a` | 8,434,998,015 |
| C | `a17c5213-8c31-57e0-b08b-35be99fc9357` | 8,440,944,792 |
| D | `b95c4212-1210-5f44-8832-de8fb8bb10bb` | 8,433,883,676 |

### Test order and interpretation

Use **A, C, B, D**, keeping the orchestrator's r19/v3 comparison conditions and the `clonedivers-armory` profile. Load one appearance variant per session. A useful discriminating result is three consecutive successful drops under the same conditions that reproduce v3's fault. Preserve the failure site, hash, scene and timestamps for any crash.

1. **A first** isolates the one medium-cape UNIT. Repeated success sharply narrows the regression to its external-geometry contract or processing of that provider.
2. **C next** tests whether including the exact shared native geometry in the consolidated family is sufficient. Success alongside A supports the residency hypothesis while retaining the native cape UNITs. Failure leaves package requests, initialization timing and other contract differences open.
3. **B then** tests native archive ownership by removing explicit cape UNIT providers. A passing B with a failing C points toward ownership/load-order behavior that an extra geometry provider alone does not resolve. If A fails while B or C succeeds, investigate the shared/two-cape contract.
4. **D last** separates the text hypothesis if all cape controls fail. A passing D would redirect analysis toward the rebuilt string tables and any earlier corruption reaching the mesh lookup.

If all four fail with the same fault, the next round needs the missing caller pages and a broader ship/armor/resource-residency split. Success or failure at a different site has a separate interpretation. These artifacts provide offline controls, with runtime outcomes pending.

## Verification and reproduction

`build_variants.py` imports the unchanged root `verify_candidate.py` and calls its normal `verify(candidate)` entry point. It redirects `empire_port.ROOT` to a per-variant evidence folder containing that variant's exact expected payload hashes and byte-identical native pins. This isolates receipts while preserving every verification rule. Each variant folder has physical file copies.

All four runs return `passed=true`, 32/32 archive-rule checks, zero duplicates, zero payload mismatches, zero missing dependencies, zero codec mismatches and zero missing custom external media. Each checks 437 banks and 2,361 native STREAM wrappers. A/B/C check 13 text tables, D checks zero selected text overrides. Existing native exceptions and the two inherited native missing-media contracts retain their verifier classification. Per-variant build receipts enumerate every resource delta and rebuilt bucket. [Full build/verifier log](variants.log), [A receipt](a-medium-r19/verification-summary.json), [B receipt](b-native-capes/verification-summary.json), [C receipt](c-cape-geometry/verification-summary.json), [D receipt](d-native-text/verification-summary.json).

Commands ran from this workspace with the supplied Python 3.13 interpreter. Capstone commands used the retained read-only analysis venv.

```powershell
$py = 'C:\Users\goode\AppData\Local\Programs\Python\Python313\python.exe'
$analysisPy = 'C:\Users\goode\OneDrive\Desktop\clonedivers\dist\empire-launch-crash-2026-10-08\analysis-venv\Scripts\python.exe'
$run = 'D:\ClaudeWork\harness-home\runs\20261108-212200-mods-hellforge'
& $analysisPy -B triage-r45\analyze_dump.py "$run\hellforge-session\crash-10888.dmp" --output triage-r45\hellforge-dump --regions 2bd830:fd 1c8eb4:20 9d8f0:50 8a1300:60
& $analysisPy -B triage-r45\inventory_suspects.py
& $analysisPy -B triage-r45\compare_capes.py
& $analysisPy -B triage-r45\expand_inventory.py
& $py -B triage-r45\build_variants.py
& $py -B -m unittest -v test_empire_port.py
& $analysisPy -B -m unittest discover -s triage-r45 -p test_triage.py -v
& $py -B triage-r45\audit_delivery.py
```

The build command requires fresh output folders. The audit command is repeatable. Existing archive/helper regressions pass **13/13**. Focused triage regressions pass **4/4**, including three-dump address arithmetic, captured receiver fields, native/inline cape contracts, unique GUIDs and exact variant deltas. Final rehashing checks every delivered file against its verifier receipt and confirms that v3 matches its original receipt. [Existing tests](tests.log), [triage tests](triage-tests.log), [delivery audit](delivery-audit.json).

## Completion and limitations

Outputs comprise this report, analysis/inventory scripts and receipts under `triage-r45`, plus the four candidate folders. This workspace has no Git repository, so there is no commit. Runtime validation and caller disassembly remain pending. The chosen controls prioritize the cape evidence and include a text fallback. Ship-only and post-r41-wide controls were deferred within the four-variant limit.

All work used offline file reads and workspace outputs. The final audit pins the input dumps, dictionaries, verifier, port decisions and native index. The original r45 pack is hash-identical to its verified receipt. The causal resource and runtime residency mechanism remain hypotheses until the orchestrator tests the variants or supplies the missing caller image.

## Follow-up on the moved crash and v4

November 8, 2026 local time. The new thin hash **`ed9eeb61` resolves uniquely in the retained dictionaries to `g_shock_trooper_cape`**, using `Murmur64A(name) >> 32`. The scan found it in exactly two r45 resources, both in `9ba626afa44a3aa3.patch_1000`.

| Typed resource | Resolved resource name | MAIN offsets containing `ed9eeb61` |
| --- | --- | --- |
| `ea846ef460cc5ba0.e0a48d0be9a7453f` | `content/fac_helldivers/capes/shock_trooper_cape` UNIT | 6984, 7020, 7380 |
| `ea846ef460cc5ba0.18dead01056b72e9` | Same resource name, BONES | 56 |

The second cape was still external in variant A. Two recorded A cycles fail at `+0x2bd8ad` with `rbx=ed9eeb61`, `rdx=ffffffff`, `r8=1fffffffe` and `r14=8ff`. This supports a cape-contract problem affecting both objects. The newest available C cycles also fail twice at the same site, preserving `a61438bc` and `r14=85d`. Including the native GeometryGroup was therefore insufficient in those runs. A missing provider's mere presence cannot explain the full fault. Provider registration, load ordering, external-geometry initialization or another contract difference remain open. V4 uses complete r19 inline payloads for both capes. [Captured live results](v4/live-bisection-snapshot.json).

### Complete provider scan

The new scan reads all **6,780 resources across 16 types**, totaling **539,544,625 MAIN bytes**, and decodes the external GeometryGroup field at `+16` in **all 905 UNITs**, including unchanged UNITs. It also searches every MAIN payload for every GeometryGroup identity in the native 25480438 index and pack, comprising 12 native and two selected identities. No non-UNIT geometry consumer was found. The native index has no separate mesh resource type. All three external UNIT contracts are below. [Inventory, names, offsets and input pins](v4/provider-inventory.json).

| UNIT, common type `e0a48d0be9a7453f` | Mesh name relevant to crash | GeometryGroup | Effective-provider finding |
| --- | --- | --- | --- |
| `3c33cf10a26cbb3e`, medium cape | `a61438bc`, `g_medium_cape` | `c6a69260cc389bcd` | Offender. Native family `18235e0c9ec0e636` only, no pack provider |
| `ea846ef460cc5ba0`, shock-trooper cape | `ed9eeb61`, `g_shock_trooper_cape` | `c6a69260cc389bcd` | Offender. Same native-only provider |
| `15678e360612b805`, admiral | UNIT descriptor-name list empty | `0a2f9ca86c814e7a` | Included provider, owner and GPU extents verified |

These are the complete two offenders under the conservative closure rule. Native family residency at the fault is **unknown**. No package-residency trace establishes it. Copying a UNIT into startup family `9ba626afa44a3aa3` slots 1000–1031 does not establish that its original provider family `18235e0c9ec0e636` is loaded. The rule therefore requires providers inside the pack and grants no exemption based solely on the native index. This is an explicit conservative build policy. The C results establish that passing this policy alone does not establish runtime correctness.

Outside UNIT headers, the scan detects literal references to known GeometryGroup identities. Opaque, encoded or unknown provider schemas are outside its decoded coverage. Existing material, texture, bones, state-machine and audio dependency checks still run through the full verifier. Their native-index existence checks do not prove runtime residency.

### Delivered v4

Folder **`D:\ClaudeWork\empire-forge\empire-forge-r45-v4`**. Name `EmpireDivers Forge r45 v4 inline cape repair (25480438)`. New GUID **`df8782b2-aa8c-5466-82cc-632f39257bc2`**. Empty Options array. Flat 32-triplet layout, 102 files, 6,780 resources, 8,435,476,728 bytes.

Only the following two typed payloads differ from v3. Both are byte-exact complete r19 MAIN/STREAM/GPU payloads, with `GeometryGroup=0` and inline geometry. The restored GPU sizes are 286,512 and 159,356 bytes respectively. Buckets 1030 and 1000 were rebuilt. All remaining resource payloads match v3.

- `3c33cf10a26cbb3e.e0a48d0be9a7453f`, medium cape.
- `ea846ef460cc5ba0.e0a48d0be9a7453f`, shock-trooper cape.

The build script selects the requested repair order for each finding. It restores available complete r19 inline UNITs first, includes an exact native provider otherwise, and drops an override with a native fallback only if neither payload is available. Both actual repairs took the first branch. [Build receipt and exact payload hashes](v4/build.json).

### New verifier and regression evidence

`verify_candidate.geometry_provider_scan` is called by the normal full verifier entry point. It fails unresolved external geometry independently of ordinary native-index dependency closure, verifies included UNIT providers' owner and mesh-name coverage, checks provider GPU extents, and emits `geometry-provider-scan.json`. Every resource type is searched so a known GeometryGroup consumer outside UNIT cannot bypass closure. The summary now exposes `geometryProviderClosurePassed` and `unresolvedGeometryProviders`.

| Evidence | Result |
| --- | --- |
| Updated full verifier against original v3, isolated receipts | Expected FAIL, exactly two unresolved geometry providers, all prior archive/dependency/audio checks pass |
| Updated full verifier against v4 | PASS, 32 archives, zero unresolved providers, duplicates, payload mismatches, missing dependencies, missing custom media or codec mismatches |
| Audio and text coverage in both full runs | 437 banks, 2,361 native stream wrappers, 13 English tables |
| Existing archive/helper regressions | 13/13 PASS |
| New provider regressions | 9/9 PASS, including actual v3 rejection and actual v4 acceptance |
| Existing triage regressions | 4/4 PASS |
| Independent delivery audit | PASS, all 102 v4 files and 6,780 payloads rehashed, all 102 original v3 files match original pins, six distinct candidate GUIDs |

The new focused checks also cover unknown UNIT provider IDs, native existence without residency, included providers, incorrect owner identity, missing mesh names, missing provider GPU bytes and non-UNIT consumers. [V3 full rejection](v3-provider-regression/verification-summary.json), [v4 full verification](v4/verification-summary.json), [new tests](v4-provider-tests.log), [existing tests](v4-existing-tests.log), [triage tests](v4-triage-tests.log), [delivery audit](v4/delivery-audit.json).

```powershell
$py = 'C:\Users\goode\AppData\Local\Programs\Python\Python313\python.exe'
$analysisPy = 'C:\Users\goode\OneDrive\Desktop\clonedivers\dist\empire-launch-crash-2026-10-08\analysis-venv\Scripts\python.exe'
& $py -B triage-r45\build_v4.py
& $py -B -m unittest -v test_empire_port.py
& $py -B -m unittest discover -s triage-r45 -p test_geometry_provider.py -v
& $analysisPy -B -m unittest discover -s triage-r45 -p test_triage.py -v
& $py -B triage-r45\audit_v4.py
```

`build_v4.py` requires fresh v4 output folders. Tests and audit are repeatable. The v3 full-verifier run used an isolated `P.ROOT` at `triage-r45/v3-provider-regression`, with copies of its unchanged original expected-resource list and native pins. Its output is retained in `v3-provider-regression.log` and confirms the expected failure.

This workspace has no Git repository, so no commit was created. All changes and outputs are under this workspace. No game or HellForge session was launched or modified by this offline follow-up. **V4 runtime acceptance remains pending**, and the caller-page blocker described above remains. B and D had no completed results in the captured ledger snapshot.
