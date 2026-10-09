#!/usr/bin/env python3
"""Prepare r24 from the two accepted archive repairs and version-only contexts."""
from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
LOOP = ROOT / "dist/empire-launch-crash-2026-10-08/loop-2026-10-09"
SOURCE = LOOP / "fixed-public-r23-minimal-v1/manifest.json"
OUT = ROOT / "dist/production-r24-2026-10-09/candidate-v1"
OLD_VERSION = "2026.10.08-r23"
VERSION = "2026.10.09-r24"
TAG = "pack-2026.10.09-r24-files"
URL = "https://github.com/owendavidgoode/clonedivers/releases/download/" + TAG + "/"
NOTES = "Startup repair for Clonedivers and EmpireDivers; full Star Destroyer models, audio and existing features retained."
STATUS = "Startup archive corrections with matching Eagle and LEGO Yoda runtime contexts. Existing in-mission visual, physics and audio playtest checklist remains applicable. Launcher 1.7.3 retained."
ADDONS = (
    ("yoda", "codex_lego_yoda/context_spec", "47bd034fe7ec5e84ed6fda7ed4ec219b69261d2658d22f73ed6853b293b87d86", ROOT / "dist/empire-yoda-death-2026-10-08/runtime/candidate-v4/bundle/9ba626afa44a3aa3.patch_0"),
    ("eagle", "codex_empire_vehicles/context_spec", "04eef25bcd1f5300b7bd93f8bcb7a3d737fc689b98034da3d2a08365a48d04bb", ROOT / "dist/production-r23-2026-10-08/eagle-v1/eagle/9ba626afa44a3aa3.patch_0"),
)


def require(ok: Any, message: str) -> None:
    if not ok:
        raise ValueError(message)


def pin(path: Path) -> dict[str, Any]:
    raw = path.read_bytes()
    return {"path": str(path.resolve()), "bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}


def read(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path: Path, value: Any) -> None:
    require(not path.exists(), "Fresh artifact required: " + str(path))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def module(name: str, path: Path) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    require(spec is not None and spec.loader is not None, "Source module available")
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


def rebind(api: Any, runner: Any, label: str, name: str, sha: str, path: Path) -> dict[str, Any]:
    require(pin(path)["sha256"] == sha, "Exact accepted source addon " + label)
    raw = path.read_bytes()
    row, payload = api.unpack(raw)
    body = payload.decode("utf-8")
    before_modules = api.modules(body)
    before_spec, match = before_modules[name]
    require(before_spec.count(OLD_VERSION) == 1 and VERSION not in body, "One context version anchor")
    after_spec = api.replace_once(before_spec, OLD_VERSION, VERSION)
    statement = api.replace_once(match.group(0), OLD_VERSION, VERSION)
    changed_body = body[:match.start()] + statement + body[match.end():]
    require(api.replace_once(changed_body, statement, match.group(0)) == body, "Whole body reversal exact")
    after_modules = api.modules(changed_body)
    require(before_modules.keys() == after_modules.keys(), "Module set exact")
    require(after_modules[name][0] == after_spec, "Only spec version changed")
    require(all(after_modules[key][0] == value[0] for key, value in before_modules.items() if key != name), "Every other embedded module exact")
    changed_payload = changed_body.encode()
    require(len(changed_payload) == len(payload), "Version-only byte length exact")
    start = row[2] + 8
    new_raw = raw[:start] + changed_payload + raw[start + len(payload):]
    diffs = [i for i, (a, b) in enumerate(zip(raw, new_raw, strict=True)) if a != b]
    require(len(diffs) == 2, "Exactly two context version bytes changed")
    new_row, roundtrip = api.unpack(new_raw)
    require(new_row == row and roundtrip == changed_payload, "All archive header/type/descriptor/padding bytes exact")
    require(runner.execute(("assert(loadstring(" + json.dumps(changed_body, ensure_ascii=False) + "));return 'PASS'").encode()) == b"PASS", "Actual packaged addon syntax")
    folder = OUT / label
    folder.mkdir(parents=True)
    addon = folder / "addon.lua"
    addon.write_bytes(changed_payload)
    (folder / "context_spec.lua").write_text(after_spec, encoding="utf-8")
    new_sha = hashlib.sha256(new_raw).hexdigest()
    asset = OUT / "assets" / new_sha
    asset.write_bytes(new_raw)
    return {"label": label, "module": name, "source": pin(path), "asset": pin(asset), "addon": pin(addon), "contextSpec": pin(folder / "context_spec.lua"), "typedKey": f"{row[0]:016x}.{row[1]:016x}", "differentByteOffsets": diffs, "allArchiveBytesOutsideTwoVersionBytesExact": True, "allOtherModulesExact": True, "nativeBehaviorUnchanged": True}


def main() -> int:
    require(not OUT.exists(), "Fresh immutable release candidate required")
    require(pin(SOURCE)["sha256"] == "af51fa33b59141c2db938e2a67ee255c859e86e92beb444ecdb894d39176d380", "Exact accepted full r23 two-fix input")
    source = read(SOURCE)
    require(source["pack"]["version"] == OLD_VERSION and len(source["pack"]["files"]) == 1809, "Exact source version and row count")
    clone_acceptance = LOOP / "attempt-002-fixed-clone/ship-result.json"
    require(read(clone_acceptance)["reachedShip"] and read(clone_acceptance)["manifestSha256"] == "ba0c912347883c901d1731881e82fe74b680bbdbf5dae455008a6aa82a724c46", "Actual full Clone acceptance is pinned")
    # The parent owns the actual Empire launch and final r24 acceptance records.
    api = module("r24_runtime_api", ROOT / "tools/public-r22-runtime.py")
    runner = module("r24_lua_runner", api.SDK_ROOT / "sdk/tools/lua_runner.py")
    (OUT / "assets").mkdir(parents=True)
    addons = [rebind(api, runner, *item) for item in ADDONS]
    assets = []
    for proof in read(LOOP / "fixed-public-r23-minimal-v1/assets.json")["assets"]:
        old_path = Path(proof["path"])
        require(pin(old_path)["sha256"] == proof["sha256"], "Exact accepted archive correction")
        target = OUT / "assets" / proof["sha256"]
        shutil.copyfile(old_path, target)
        assets.append({**pin(target), "size": target.stat().st_size})
    assets.extend({**row["asset"], "size": row["asset"]["bytes"]} for row in addons)
    by_old = {row["source"]["sha256"]: row for row in addons}
    target = copy.deepcopy(source)
    changes = []
    for index, row in enumerate(target["pack"]["files"]):
        before = copy.deepcopy(row)
        if row["sha256"] in by_old:
            row["sha256"] = by_old[row["sha256"]]["asset"]["sha256"]
            row["url"] = URL + row["sha256"]
        elif row["sha256"] in {asset["sha256"] for asset in assets}:
            row["url"] = URL + row["sha256"]
        if row != before:
            changes.append({"index": index, "before": before, "after": copy.deepcopy(row)})
    require(len(changes) == 4 and {change["after"]["name"] for change in changes} == {"9ba626afa44a3aa3.patch_429", "9ba626afa44a3aa3.patch_453", "9ba626afa44a3aa3.patch_462", "9ba626afa44a3aa3.patch_463"}, "Exactly four MAIN release rows")
    target["pack"]["version"] = VERSION
    target["pack"]["notes"] = NOTES
    target["pack"]["statusNotes"] = STATUS
    target["pack"]["totalSize"] = sum(row["size"] for row in target["pack"]["files"])
    require(target["app"] == source["app"], "Launcher metadata byte-value exact")
    reconstructed = copy.deepcopy(source)
    for change in changes:
        reconstructed["pack"]["files"][change["index"]] = change["after"]
    for key in ("version", "notes", "statusNotes", "totalSize"):
        reconstructed["pack"][key] = target["pack"][key]
    require(reconstructed == target, "Whole manifest exact outside explicit release delta")
    manifest = OUT / "manifest.json"
    save(manifest, target)
    catalog = OUT / "assets.json"
    save(catalog, {"manifest": pin(manifest), "assets": assets, "downloadsNeededForLocalTest": 0, "releaseTag": TAG})
    api.OUTPUT, api.VERSION = OUT / "eagle-fixtures", VERSION
    (api.OUTPUT / "eagle").mkdir(parents=True)
    eagle = next(row for row in addons if row["label"] == "eagle")
    eagle_fixtures = api.fixtures(eagle, runner)
    yoda_api = module("r24_yoda_package", ROOT / "tools/yoda-death-runtime-package-v4.py")
    yoda_api.VERSION = VERSION
    spec, _ = yoda_api.closure()
    yoda = next(row for row in addons if row["label"] == "yoda")
    yoda_fixture_out = OUT / "yoda-fixtures"
    yoda_fixture_out.mkdir()
    yoda_fixtures = yoda_api.context_fixtures(yoda_fixture_out, Path(yoda["addon"]["path"]), spec, runner)
    report = {"passed": True, "tool": pin(Path(__file__)), "sourceManifest": pin(SOURCE), "manifest": pin(manifest), "version": VERSION, "releaseTag": TAG, "literalStatusNotes": STATUS, "literalNotes": NOTES, "changes": changes, "addons": addons, "assets": assets, "assetCatalog": pin(catalog), "newAssetBytes": sum(row["bytes"] for row in assets), "globalRows": 1809, "expectedEmpireFiles": 1396, "expectedCloneFiles": 1199, "allOtherRowsOptionsProfilesVariantsOrderExact": True, "allNativeLogicUnchanged": True, "launcherUnchanged": True, "eagleFixtures": eagle_fixtures, "yodaFixtures": yoda_fixtures, "clonePriorAcceptance": pin(clone_acceptance), "parentReportedEmpireR23Accepted": True, "r24ActualRuntimeAcceptancePending": True, "publicPublished": False, "hostWrites": 0}
    save(OUT / "report.json", report)
    print(json.dumps({"manifest": pin(manifest), "report": pin(OUT / "report.json"), "assets": assets, "literalStatusNotes": STATUS}))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"r24 candidate failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
