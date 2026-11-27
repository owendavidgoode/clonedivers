# Empire per-attack TIE (v0.3.1)

HellForge SDK 16, game 25480438. Client-only (`gameplay: false`, `coop: per-player`) code driver paired
with `EmpireDivers r52` across all 5 performance tiers (`full`, `lighter`, `reduced1024`, `reduced512`, `potato256`).
Version 0.3.1 expands `assets.h` and `asset_gate.h` from 3 tiers (`0x07u`) to all 5 `EmpireDivers` texture tiers
(`[0]=full`, `[1]=lighter`, `[2]=reduced1024`, `[3]=reduced512`, `[4]=potato256`, `TIE_ALL_TIERS_MASK = 0x1Fu`),
retaining lazy `hellforge_lua_mod` registration out of `hellforge_mod_init()` and:

1. **Direct `eagle_base` (`0x2C26BC4C6592FA14`) residency fallback (`package_pin.h`):**
   - Calls `hf->load_package(mod, 0x2C26BC4C6592FA14ull)` first (succeeds natively on patched loaders).
   - When stock HellForge `0.4.0-beta` omits `0x2C26BC4C6592FA14` from `kRefPackages` and returns `0`,
     verifies all four build `25480438` 32-byte code pins (`game.dll` `0x12FE350` `RefcountedPackageRequest`,
     `0x12FE680` `ArePackagesIdle`; `helldivers2.exe` `0x321330` `has_loaded`, `0x60DA80` `find`), checks
     `< 75%` refmap headroom at `[game.dll + 0x347CEB0]`, acquires/holds `0x2C26BC4C6592FA14` on the game
     thread, and confirms residency in `helldivers2.exe` (`0x1A10208 -> +0x400 -> +0x208`).
   - Fails closed with zero native calls or crashes if any pin mismatches or the refmap is `>= 75%` full.
2. **4-Player Concurrent Eagle Sortie Tracking (`TIE_MAX_SORTIES = 16`):**
   - Correlates `HF_ON_STRATAGEM` throwers against `PlayerManager` slots `0..3` with `HF_ON_SPAWN` delivery
     spawns (both local and remote co-op peers), prunes expired sorties on TTL / mission exit, and clears
     bridge edits cleanly in `hellforge_mod_unload`.

## Delivery

The appearance candidate combines all r46 visuals with the sealed private Fighter
and Bomber closures. Its extended Eagle package replaces the original inside the
same 32-archive pack. There are no duplicate resource keys or separate visual addon
to order after a base pack. Do not enable r45-v4/r46/Clones alongside this candidate.
The code-only ZIP can be ordered before or after this appearance pack.

The previous 0.1.0 ZIP already contained data, but its lab session-mods.json lists it
before the base Empire pack. Installing later did not establish staging priority.
The previous guard hid the failing payload. The new log/status gives the exact
resource, type, part (0=main, 1=stream, 2=GPU), expected SHA-256 and selected file.
It distinguishes missing resources/files, changed payloads and ambiguous providers.

## Design

Each distinct Eagle delivery definition owns its model before spawning. Strafing and
110mm use Fighter `f62538008a0edc18`. Airstrike, 500kg, cluster, napalm, smoke and gas
use Bomber `81787742b65c43f4`. Smoke/gas and 110mm classification remain lab defaults
for owner review. Definitions remain constant during sorties.

Each private model owns its UNIT, native-compatible BONES, STATE_MACHINE and PHYSICS,
private surfaces and textures. The original native/r46 Eagle package entries remain
in order before the added closures. The driver hashes all 135 nonempty closure
payloads, including GPU buffers, and refuses changed or ambiguous staged providers.

After the SDK reports the Eagle package resident, the driver assigns only the model
u64 on eight exclusively owned delivery records. All remaining Unit and Eagle-control
bytes must remain exact. Partial failures clear the edits through the bridge. Lost
ownership or staged file changes disarm it. Restart for asset changes.

The native named-field SDK omits this model field. The DLL dynamically binds bridge 3
and registers holder `lua.clonedivers.empire-tie-switch` for the aligned raw-field
ownership/reload layer. No Lua loader is required. The mod is restart-only.

## Offline build

`build_assets.py --refresh` regenerates the sealed visual addon under
`build/asset-input/data` and its evidence copy, using the separately audited TIE-sa
source. The legacy launcher option labelled Bomber is an older Fighter build and
cannot establish Bomber identity.

`build_delivery.py` requires fresh `empire-forge-r46-tie` and `pass2` outputs. It pins
the verified r46/addon inputs, preserves unrelated r46 resources, merges the closures
by resource key and runs the unmodified full verifier, including the external
geometry-provider rule. Evidence: `D:\ClaudeWork\empire-forge\empire-tie-switch-candidate\pass2`.

`build-offline.ps1` builds through the real CLI in an isolated workspace, runs the
C regression host and gate against the installable appearance pack, and packages
the code-only ZIP. Compiler caches remain outside HellForge. The source folder has
no top-level data directory. The CLI source ZIP rebuilds the DLL on installation.

`build/tie_test.exe --scan <staged-directory>` runs the same closure guard offline.
Its regression tests include exact missing Bomber UNIT, absent GPU sidecar, package
hash/priority, changed GPU and ambiguous-provider diagnostics, in addition to model
assignment, rollback, unchanged controls, residency and simulated reload coverage.

## Runtime acceptance

Use the serialized lab and only `clonedivers-armory`. Require the 135-part qualified
closure line, resident Eagle package and all eight assignment lines before testing.
Stop on any `off` line and retain the full diagnostic.

Capture Fighter strafe and twin-hull Bomber 500kg in the same mission, alternate
three pairs, then repeat after normal rearm and in a second mission. Keep rendered
silhouettes separate from spawn/definition logs. Package acknowledgement and model
readback cannot prove what was rendered. Private physics, flight, effects, materials,
additional deliveries and co-op remain live acceptance items.

`D:\ClaudeWork\clonedivers-lab\controls-demo.md` was absent at pass-2 preparation.
The queue therefore uses manual stratagem selection and beacon throws. No unqualified
`stratagem` command or raw address is assumed. A later lab may switch to automation
only if that controls-demo supplies successful evidence and exact supported syntax.
