#!/usr/bin/env python3
"""Independently qualify the r22 Eagle source delta and Lighter dependencies.

Runs packaged context fixtures in a fresh standalone Lua state with real Win32
path and BCrypt hashing APIs. Reads immutable archives; never launches the game
or changes existing outputs, settings, caches or installation files.
"""
from __future__ import annotations

import argparse
from collections import Counter
from collections.abc import Sequence
import hashlib
import importlib.util
import json
import logging
from pathlib import Path
import re
import struct
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
LANE = ROOT / "dist/production-r22-2026-10-08"
R21 = ROOT / "dist/production-r21-2026-10-08"
LIGHTER = ROOT / "dist/empire-offline-next-2026-10-08/lighter"
SDK = ROOT / "dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d"
ROW = struct.Struct("<7Q6I")
LUA = 0xA14E8DFA2CD117E2
GEOMETRY = {419, 420, 432, 456}
MAIN = re.compile(r"[a-f0-9]{16}\.patch_\d+$")
ASSETS = {"fb7b316e34c8466e025b1ce9d18eb13a5b5b079dc57a06e0c6d7dac21d7bc8c2": 152400,
          "18096c55af1459c5508b689f2223895267fc5473ee892bc12abac3253fca4456": 290643200}


def require(ok: Any, message: str) -> None:
    if not ok:
        raise ValueError(message)


def pin(path: Path) -> dict[str, Any]:
    before = path.stat()
    with path.open("rb") as handle:
        checksum = hashlib.file_digest(handle, "sha256").hexdigest()
    after = path.stat()
    require((before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns), "File changed during review")
    return {"path": str(path.resolve()), "bytes": after.st_size, "sha256": checksum}


def checked(reference: dict[str, Any]) -> Path:
    path = Path(reference["path"])
    require(pin(path) == reference, f"Immutable input changed: {path}")
    return path


def load(path: Path, expected: str | None = None) -> tuple[dict[str, Any], dict[str, Any]]:
    receipt = pin(path)
    require(expected is None or receipt["sha256"] == expected, f"Sealed proof drift: {path}")
    return json.loads(path.read_text(encoding="utf-8-sig")), receipt


def lua_source(path: Path, identity: int) -> str:
    data = path.read_bytes()
    require(struct.unpack_from("<3I", data) == (0xF0000011, 1, 1) and struct.unpack_from("<Q", data, 32)[0] == len(data), "Singleton complete MAIN")
    require(struct.unpack_from("<Q", data, 80)[0] == LUA and struct.unpack_from("<I", data, 88)[0] == 1, "Singleton Lua type directory")
    row = ROW.unpack_from(data, 104)
    require(row[:2] == (identity, LUA) and row[12] == 0 and row[8:10] == (0, 0) and row[2] % 16 == 0 and row[2] >= 184 and row[2] + row[7] <= len(data), "Exact Lua resource bounds/identity")
    payload = data[row[2]:row[2] + row[7]]
    require(struct.unpack_from("<II", payload) == (len(payload) - 8, 2), "Exact source-text Lua envelope")
    require(not any(data[184:row[2]]) and not any(data[row[2] + row[7]:]), "Archive padding is zero")
    return payload[8:].decode("utf-8")


def embedded(body: str) -> dict[str, tuple[str, str]]:
    """Parse each literal independently with JSONDecoder, not builder regex."""
    decoder = json.JSONDecoder()
    result = {}
    prefix = "package.preload["
    for line in body.splitlines():
        if not line.startswith(prefix):
            continue
        name, end = decoder.raw_decode(line, len(prefix))
        anchor = "]=function()return assert(loadstring("
        require(line[end:].startswith(anchor), "Exact embedded module declaration")
        source, finish = decoder.raw_decode(line, end + len(anchor))
        if line[finish:].startswith(","):
            chunk, finish = decoder.raw_decode(line, finish + 1)
            require(chunk == name, "Source chunk name matches module name")
        require(line[finish:] == "))()end" and name not in result and isinstance(source, str), "Complete unique module literal")
        result[name] = (source, line)
    require(result, "Embedded modules present")
    return result


def module(path: Path) -> Any:
    spec = importlib.util.spec_from_file_location("r22_runtime_independent_lua_runner", path)
    require(spec is not None and spec.loader is not None, "Standalone primary Lua runner available")
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


def state_key(state: dict[str, Any]) -> str:
    return json.dumps({key: state[key] for key in ("profile", "mode", "requestedOptions")}, sort_keys=True)


def closure_peer(public: dict[str, Any], out: Path) -> dict[str, Any]:
    closure, closure_pin = load(LIGHTER / "closure-v1.json", "78185f5941b6830c7f4965e56318d7d82d262841268d6dc8e1a3d1ef7db9301c")
    candidate, candidate_pin = load(checked(closure["candidate"]))
    selected, selected_pin = load(checked(closure["actualProduction96"]))
    old, old_pin = load(R21 / "inputs-v1/production96.json", "ef209413ef14b7ecbdefadcc4d161bfc12ce5d9ef0608d0dad81d457988f3140")
    require(closure["passed"] and closure["additionalFullOnlyGeometryDependenciesRequired"] == [] and selected["requestedStates"] == 96 and selected["manifestSha256"] == candidate_pin["sha256"], "Qualified geometry/control closure and actual96 binding")
    changed, targets = [], []
    for index, (before, after) in enumerate(zip(public["pack"]["files"], candidate["pack"]["files"], strict=True)):
        if before != after:
            number = int(before["name"].split(".patch_", 1)[1].split(".", 1)[0])
            require(number in GEOMETRY and before["textureProfiles"] == ["full"] and after["textureProfiles"] == ["full", "lighter"], "Only four reviewed geometry gates widen")
            restored = dict(after)
            restored["textureProfiles"] = ["full"]
            require(restored == before, "Geometry gates retain every hash/size/order/option/mode field")
            targets.append(before)
            changed.append({"index": index, "name": before["name"], "sha256": before["sha256"], "size": before["size"]})
    require(len(changed) == 12, "Exactly twelve geometry triad rows widened")
    catalog, catalog_pin = load(ROOT / "dist/empire-deep-qa-2026-10-08/qa/all-assets-v1.json", "d2c9eb99f6af223b0c77d62bb9743cb142932545a290313e0a882ba0f903e2d8")
    hints = {row["sha256"]: Path(row["path"]) for row in catalog["assets"]}
    refreshed, directories = {}, {}

    def source(row: dict[str, Any]) -> Path:
        sha = row["sha256"]
        paths = [R21 / "release-ready-v1/assets" / sha, R21 / "inputs-v1/assets" / sha]
        if sha in hints:
            paths.append(hints[sha])
        found = next((path for path in paths if path.is_file() and path.stat().st_size == row["size"]), None)
        require(found is not None, "Bounded existing archive provider available: " + sha)
        if sha not in refreshed:
            actual = pin(found)
            require(actual["sha256"] == sha and actual["bytes"] == row["size"], "Fresh archive bytes match selected immutable hash")
            refreshed[sha] = actual
        return found

    def directory(row: dict[str, Any]) -> dict[tuple[int, int], tuple[int, ...]]:
        sha = row["sha256"]
        if sha not in directories:
            path = source(row)
            with path.open("rb") as handle:
                header = handle.read(72)
                magic, types, count = struct.unpack_from("<III", header)
                require(magic == 0xF0000011 and types < 1000 and count < 1_000_000, "Valid bounded archive header")
                table = handle.read(types * 32 + count * 80)
            require(len(table) == types * 32 + count * 80, "Complete archive directory")
            rows = list(ROW.iter_unpack(table[types * 32:]))
            require(len({item[:2] for item in rows}) == count and all(item[2] + item[7] <= row["size"] for item in rows), "Unique bounded typed MAIN payloads")
            directories[sha] = {item[:2]: item for item in rows}
        return directories[sha]

    target_keys = {key for row in targets if MAIN.fullmatch(row["name"]) for key in directory(row)}
    require(len(target_keys) == closure["distinctTargetResources"] == 168, "Independent exact168 geometry resource scope")
    # Hash affected nonempty geometry companions, without rescanning all streams.
    for row in targets:
        if row["size"]:
            source(row)
    cached_winners = {}

    def winners(report: dict[str, Any], state: dict[str, Any]) -> dict[tuple[int, int], str]:
        identity = state["fileSet"]
        if identity not in cached_winners:
            result = {}
            for row in report["fileSets"][identity]:
                if MAIN.fullmatch(row["name"]) and row["size"]:
                    for key in directory(row):
                        result[key] = row["sha256"]
            cached_winners[identity] = result
        return cached_winners[identity]

    prior_states = {state_key(state): state for state in old["selections"]}
    new_states = {state_key(state): state for state in selected["selections"]}
    require(len(prior_states) == len(new_states) == 96 and prior_states.keys() == new_states.keys(), "All96 identical requested states")
    expected_additions = Counter((row["sha256"], row["size"]) for row in targets if row["size"])
    comparisons = []
    for identity, state in new_states.items():
        prior = prior_states[identity]
        old_files, new_files = old["fileSets"][prior["fileSet"]], selected["fileSets"][state["fileSet"]]
        eligible = state["profile"] == "lighter" and state["mode"] == "EmpireDivers"
        before = Counter((row["sha256"], row["size"]) for row in old_files if row["size"])
        after = Counter((row["sha256"], row["size"]) for row in new_files if row["size"])
        require(state["effectiveOptions"] == prior["effectiveOptions"] and after == before + (expected_additions if eligible else Counter()), "Only reviewed geometry additions for Lighter Empire")
        before_winners, after_winners = winners(old, prior), winners(selected, state)
        changed_keys = {key for key in before_winners.keys() | after_winners.keys() if before_winners.get(key) != after_winners.get(key)}
        require(changed_keys <= target_keys if eligible else not changed_keys, "All96 typed winner changes limited to reviewed geometry")
        require(all(key[1] != 0xCD4238C6A0C69E32 for key in changed_keys), "Every streamed texture provider retained")
        if state["mode"] == "EmpireDivers":
            same = dict(state)
            same["profile"] = "full"
            full_winners = winners(selected, new_states[state_key(same)])
            require(all(after_winners.get(key) == full_winners.get(key) for key in target_keys), "All168 target providers identical in matching Full and Lighter states")
            for sha, size in ASSETS.items():
                require(sum(row["sha256"] == sha and row["size"] == size for row in new_files) == 1, "Both runtime-required Eagle asset identities selected in every Empire profile")
        comparisons.append({"profile": state["profile"], "mode": state["mode"], "fileSet": state["fileSet"], "changedTypedWinners": len(changed_keys),
                            "onlyReviewedGeometryAdded": eligible, "allStreamedTextureProvidersExact": True,
                            "eagleAssetClosureSelected": state["mode"] == "EmpireDivers"})
    source_proof = out / "closure-sources.json"
    source_proof.write_text(json.dumps(list(refreshed.values()), indent=2) + "\n", encoding="utf-8")
    return {"closure": closure_pin, "candidate": candidate_pin, "selector": selected_pin, "priorSelector": old_pin,
            "sourceCatalog": catalog_pin, "geometryGateChanges": changed, "distinctTargetKeys": len(target_keys),
            "actual96StateComparisons": comparisons, "freshMainAndAffectedCompanionHashes": pin(source_proof),
            "freshUniqueArchives": len(refreshed), "freshArchiveBytes": sum(row["bytes"] for row in refreshed.values()),
            "allStreamedTextureWinnersPreserved": True, "everyEmpireProfileContainsBothExactEagleAssets": True,
            "geometryControlClosureProofRetained": True, "nativeMaterialDuplicateProvidersNotReaudited": True}


def run(out: Path) -> dict[str, Any]:
    require(out.resolve().is_relative_to(LANE / "qa") and not out.exists(), "Fresh scoped independent report folder")
    runtime, runtime_pin = load(LANE / "runtime-v1/report.json", "e7810a0dc435d9cef588813a6c8b9d6a648ff9584d2130117f281449b8aac108")
    public, public_pin = load(R21 / "release-ready-v1/manifest.json", "55d5297988c0b134eef4738edf81026ebbbe688c917ad97c6170ff7ef1b59c2e")
    require(pin(ROOT / "tools/public-r22-runtime.py")["sha256"] == "f9605dfab112dad2bb013eb81b026748e0cef9d686011258ed40cae7d3282968", "Reviewed runtime builder source unchanged")
    source = runtime["sources"][0]
    require(runtime["passed"] and len(runtime["sources"]) == 1 and source["typedKey"] == "6d133274694b50a9.a14e8dfa2cd117e2", "Only Eagle runtime changed")
    before_path = checked(source["oldArchive"])
    after_path = Path(source["path"])
    require(pin(after_path)["sha256"] == source["newSha256"] == "18c3f428677e0cb31b86959dfa5db27fc406abb2d602f7a93eaafc1c13b721d2", "Fresh candidate archive hash")
    before = lua_source(before_path, 0x6D133274694B50A9)
    after = lua_source(after_path, 0x6D133274694B50A9)
    require(after == checked(source["addon"]).read_text(encoding="utf-8"), "Packaged source matches source receipt")
    old_modules, new_modules = embedded(before), embedded(after)
    prefix = "codex_empire_vehicles/"
    context, spec = prefix + "context", prefix + "context_spec"
    require(old_modules.keys() == new_modules.keys() and {name for name in old_modules if old_modules[name][0] != new_modules[name][0]} == {context, spec}, "Exactly context and version-spec modules differ")
    old_assert = "    assert(config.TextureProfile=='full' and config.InstalledTextureProfile=='full' and receipt.TextureProfile=='full',\n        'CONTEXT_REFUSED: only independently checked Full closure is enabled')"
    new_assert = "    local profile=config.TextureProfile\n    assert((profile=='full' or profile=='lighter') and config.InstalledTextureProfile==profile and\n        receipt.TextureProfile==profile,'CONTEXT_REFUSED: reviewed texture profile agreement required')"
    require(old_modules[context][0].count(old_assert) == new_modules[context][0].count(new_assert) == 1 and
            new_modules[context][0].replace(new_assert, old_assert) == old_modules[context][0], "Only exact profile-agreement assertion changes in context")
    require(old_modules[spec][0].count('"2026.10.08-r21"') == new_modules[spec][0].count('"2026.10.08-r22"') == 1 and
            new_modules[spec][0].replace('"2026.10.08-r22"', '"2026.10.08-r21"') == old_modules[spec][0], "Only exact pack version changes in spec")
    reverted = after
    for name in (context, spec):
        require(reverted.count(new_modules[name][1]) == 1, "Unique complete replaced module declaration")
        reverted = reverted.replace(new_modules[name][1], old_modules[name][1])
    require(reverted == before, "Every byte of entry point and native gateway/write/guards exact outside two reviewed declarations")
    for reference in source["files"][1:]:
        require(checked(reference).stat().st_size == 0, "Lua companions remain empty")
    out.mkdir(parents=True)
    runner_path = checked(runtime["primaryLuaRunner"])
    runner = module(runner_path)
    require(pin(Path(runner.default_dll())) == runtime["offlineLuaDll"], "Fresh standalone Lua DLL pin")
    fixture_path = checked(runtime["fixtures"]["eagle"]["program"])
    fixture = fixture_path.read_text(encoding="utf-8")
    fixture_modules = embedded(fixture)
    for name in (prefix + "json", context, spec):
        require(fixture_modules[name][0] == new_modules[name][0], "Fixture executes actual packaged context modules")
    for name in ("metrics", "windows_ffi", "windows_readonly"):
        require(fixture_modules["hd2runtime/runtime/" + name][0] == (SDK / "runtime" / (name + ".lua")).read_text(encoding="utf-8"), "Actual Win32/BCrypt runtime source in fixture")
    require("runtime.read=function()error('unexpected native read')end" in fixture and "runtime.module_hash=function()error('unexpected native image read')end" in fixture and
            "runtime.mkdir=function()error('unexpected directory creation')end" in fixture and "assert(mode=='rb','write refused')" in fixture, "Fixtures deny game-image/native memory reads and directory/file writes")
    rerun = json.loads(runner.execute(fixture.encode("utf-8")))
    require(rerun == runtime["fixtures"]["eagle"]["result"] and rerun["passed"] and len(rerun["checks"]) == 56 and rerun["filesystemWrites"] == 0 and
            rerun["realWindowsPathApis"] and rerun["realWindowsBcryptSha256"] and not rerun["realEngineOpened"], "Fresh independent rerun of all56 actual Win32/BCrypt context fixtures")
    observer = runtime["unchangedObserver"]
    observer_path = checked(observer["archive"])
    require(observer["archive"]["sha256"] == "39ed19f63a36c14dbc135cc63a5d4c1ab29b6328976466a12330430ff715a2e3", "Public observer retained byte exact")
    observer_body = lua_source(observer_path, 0xEA6B75393EE0B75D)
    # Construct our own denial harness around the actual serialized observer.
    inert = "require=function()error('peer denies all require')end\nos.getenv=function()error('peer denies all environment reads')end\nio.open=function()error('peer denies all file I/O')end\nlocal result=(function()\n" + observer_body + "\nend)()\nassert(result.status=='public_diagnostic_disabled' and result.started==false and result.scaleWrites==0 and result.nativeSpawnScaleEnabled==false)\nreturn 'PASS'"
    require(runner.execute(inert.encode("utf-8")) == b"PASS", "Fresh independent observer denial harness")
    (out / "observer-peer-fixture.lua").write_text(inert, encoding="utf-8")
    closure = closure_peer(public, out)
    report = {"passed": True, "tool": pin(Path(__file__)), "runtime": runtime_pin, "sealedR21Manifest": public_pin,
              "runtimeBuilder": pin(ROOT / "tools/public-r22-runtime.py"), "oldArchive": pin(before_path), "newArchive": pin(after_path),
              "changedModules": [context, spec], "allOtherEmbeddedModules": [{"name": name, "sha256": hashlib.sha256(value[0].encode()).hexdigest()} for name, value in old_modules.items() if name not in (context, spec)],
              "reversibleSourceDeltaOnlyProfileAgreementAndVersion": True, "nativeWriteGatewayEntryAndAllOtherGuardsByteExact": True,
              "rawSingletonArchiveBoundsTypeEnvelopeAndPaddingExact": True, "freshFixtureProgram": pin(fixture_path), "freshContextResults": rerun,
              "actualPackagedContextFreshlyExecuted": True, "standaloneLuaDll": pin(Path(runner.default_dll())),
              "publicObserverByteExact": pin(observer_path), "freshObserverDenialFixture": pin(out / "observer-peer-fixture.lua"),
              "observerNoRequiresEnvironmentOrFileIo": True, "geometryAndEagleProfileDependencyPeer": closure,
              "readyForCandidateCompositionReview": True, "runtimeAccepted": False, "gameSettingsOrDeploymentChanged": False,
              "limits": ["Checks qualify source delta, guards, metadata selection and archive closure; native dispatch and rendering remain unaccepted.",
                         "Previous pinned geometry closure retains its native material-provider limitation; duplicate native material providers were not reaudited."]}
    path = out / "report.json"
    path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return {"passed": True, "report": pin(path), "freshContextCases": 56, "allRequestedSelectorStates": 96,
            "geometryTypedKeys": 168, "freshMainAndAffectedCompanionArchives": closure["freshUniqueArchives"]}


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=Path, default=LANE / "qa/runtime-peer-v1")
    logging.basicConfig(level=logging.INFO)
    try:
        print(json.dumps(run(parser.parse_args(argv).out), indent=2))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception("Independent r22 runtime review failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
