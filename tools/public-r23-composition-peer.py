#!/usr/bin/env python3
"""Independently review the r23 additive manifest and actual 96-state selector."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
LANE = ROOT / "dist/production-r23-2026-10-08"
R22 = ROOT / "dist/production-r22-2026-10-08"
ROW = struct.Struct("<7Q6I")
MAIN = re.compile(r"[a-f0-9]{16}\.patch_\d+$")
EAGLE = (0x6D133274694B50A9, 0xA14E8DFA2CD117E2)
BANK = (0xB7CC016F2537E3D3, 0x535A7BD3E650D799)
OBSERVER = (0xEA6B75393EE0B75D, 0xA14E8DFA2CD117E2)
EMPTY = hashlib.sha256(b"").hexdigest()


def require(value: Any, message: str) -> None:
    if not value:
        raise ValueError(message)


def pin(path: Path) -> dict[str, Any]:
    before = path.stat()
    require(path.is_file() and not path.is_symlink(), "Regular source file")
    with path.open("rb") as handle:
        digest = hashlib.file_digest(handle, "sha256").hexdigest()
    after = path.stat()
    require((before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns), "Concurrent source mutation")
    return {"path": str(path.resolve()), "bytes": after.st_size, "sha256": digest}


def read(path: Path, sha: str | None = None) -> Any:
    require(sha is None or pin(path)["sha256"] == sha, f"Sealed source changed: {path}")
    return json.loads(path.read_text(encoding="utf-8-sig"))


def state_key(row: dict[str, Any]) -> str:
    return json.dumps({key: row[key] for key in ("profile", "mode", "requestedOptions")}, sort_keys=True)


def group(name: str) -> tuple[str, int, str]:
    archive, rest = name.split(".patch_", 1)
    number, dot, suffix = rest.partition(".")
    return archive, int(number), dot + suffix


def run(inputs: Path, out: Path) -> dict[str, Any]:
    require(inputs.resolve().is_relative_to(LANE) and out.resolve().is_relative_to(LANE / "qa") and not out.exists(), "Scoped inputs and fresh peer output")
    baseline_path = R22 / "release-ready-v1/manifest.json"
    baseline = read(baseline_path, "3a70933d5a1f7efb60627224a6befcdcdbe235c957032834101560b4383e0a03")
    manifest_path = inputs / "manifest.json"
    candidate, composition = read(manifest_path), read(inputs / "composition.json")
    require(pin(manifest_path) == composition["manifest"] and composition["passed"], "Actual candidate composition pin")
    old_path, selected_path = R22 / "inputs-v1/production96.json", inputs / "production96.json"
    old = read(old_path, "9652cd9331deb344c043b0c401a0973c226a16a1d89899e2f3458f2f38e797bb")
    selected = read(selected_path)
    require(candidate["app"] == baseline["app"] and candidate["app"]["version"] == "1.7.3", "Launcher definition unchanged")
    require(len(candidate["pack"]["files"]) == 1809 and len(baseline["pack"]["files"]) == 1800 and candidate["pack"]["files"][:1800] == baseline["pack"]["files"], "All 1800 prior rows/URLs/order/gates exact")
    require(candidate["pack"]["version"] == "2026.10.08-r23" and candidate["pack"]["totalSize"] == sum(r["size"] for r in candidate["pack"]["files"]), "Version/count/size")
    a, b = dict(baseline["pack"]), dict(candidate["pack"])
    for field in ("files", "version", "totalSize", "notes", "statusNotes"):
        a.pop(field, None)
        b.pop(field, None)
    require(a == b and {k: v for k, v in baseline.items() if k != "pack"} == {k: v for k, v in candidate.items() if k != "pack"}, "All unreviewed manifest fields exact")
    require([row["label"] for row in composition["sources"]] == ["audio", "runtime", "eagle"], "Reviewed input order")
    reports, receipts = [], []
    for source in composition["sources"]:
        receipt = source["report"]
        require(pin(Path(receipt["path"])) == receipt, "Fresh input report pin")
        report = read(Path(receipt["path"]))
        require(report.get("passed") is True, "Qualified input report")
        reports.append(report)
        receipts.extend(report["sources"][0]["files"] if source["label"] == "eagle" else report["files"])
    require(len(receipts) == 9, "Three complete triads")
    eagle_path = Path(composition["sources"][2]["report"]["path"])
    require(pin(eagle_path)["sha256"] == "8adebe21f416a55cdfe9cc7291307f50db4dc878cc1c62f3d37756ec572c1dba", "Frozen Eagle rebind report")
    eagle = reports[2]
    eagle_old = Path(eagle["sources"][0]["oldArchive"]["path"]).read_bytes()
    eagle_new = Path(eagle["sources"][0]["path"]).read_bytes()
    require(eagle_old.count(b"2026.10.08-r22") == 1 and eagle_old.replace(b"2026.10.08-r22", b"2026.10.08-r23") == eagle_new, "Whole Eagle archive differs only by one version literal")
    fixture_result = eagle["fixtures"]["result"]
    require(fixture_result["passed"] and len(fixture_result["checks"]) == 56 and all(r["passed"] for r in fixture_result["checks"]) and fixture_result["realWindowsBcryptSha256"], "All 56 Eagle context fixtures passed")
    runtime_peer_path = R22 / "qa/runtime-peer-v1/report.json"
    prior_peer = read(runtime_peer_path, "b5823c09feee7dfb0416dbc7aabc053022575175a68c0f646bcb2cec00739ab2")
    proof = prior_peer["geometryAndEagleProfileDependencyPeer"]["freshMainAndAffectedCompanionHashes"]
    sources = {r["sha256"]: r for r in read(Path(proof["path"]), proof["sha256"])}
    for report_path in (R22 / "voice-v1/report.json", R22 / "runtime-v1/report.json"):
        report = read(report_path)
        for r in report.get("files", report.get("sources", [{}])[0].get("files", [])):
            sources[r["sha256"]] = r
    additions = candidate["pack"]["files"][1800:]
    for index, (row, receipt) in enumerate(zip(additions, receipts, strict=True)):
        require(group(row["name"]) == ("9ba626afa44a3aa3", 461 + index // 3, ("", ".stream", ".gpu_resources")[index % 3]), "Exact ordered 461/462/463 triads")
        require(set(row) == {"name", "url", "size", "sha256", "modes"} and row["modes"] == ["empire"], "Both profiles, Empire-only, no option gates")
        actual = pin(Path(receipt["path"]))
        require(actual == receipt and actual["sha256"] == row["sha256"] and actual["bytes"] == row["size"], "Fresh input asset identity")
        expected_url = "https://github.com/owendavidgoode/clonedivers/releases/download/pack-2026.10.08-r23-files/" + row["sha256"] if row["size"] else ""
        require(row["url"] == expected_url and (row["size"] or row["sha256"] == EMPTY), "Immutable public asset URL or exact empty companion")
        if row["size"]:
            composed = pin(inputs / "assets" / row["sha256"])
            require(composed["sha256"] == row["sha256"] and composed["bytes"] == row["size"], "Fresh composed asset identity")
            sources[row["sha256"]] = composed
    require(len({row["sha256"] for row in additions if row["size"]}) == 3, "Exactly three new nonempty assets")
    dirs, dir_pins = {}, []

    def directory(row: dict[str, Any]) -> dict[tuple[int, int], tuple[int, ...]]:
        digest = row["sha256"]
        if digest not in dirs:
            source = sources[digest]
            path = Path(source["path"])
            require(path.is_file() and path.stat().st_size == row["size"] == source["bytes"], "Pinned MAIN extent")
            with path.open("rb") as handle:
                head = handle.read(72)
                magic, types, count = struct.unpack_from("<III", head)
                require(magic == 0xF0000011 and types < 1000 and count < 1_000_000, "Bounded typed archive")
                table = handle.read(types * 32 + count * 80)
            require(len(table) == types * 32 + count * 80, "Complete fresh directory")
            rows = list(ROW.iter_unpack(table[types * 32:]))
            require(len({r[:2] for r in rows}) == count and all(r[2] + r[7] <= row["size"] for r in rows), "Unique bounded typed records")
            dirs[digest] = {r[:2]: r for r in rows}
            dir_pins.append({"wholeFileIdentity": source, "freshDirectoryBytes": len(head + table), "freshDirectorySha256": hashlib.sha256(head + table).hexdigest()})
        return dirs[digest]

    require(set(directory(additions[0])) == {BANK} and set(directory(additions[6])) == {EAGLE}, "Exact singleton BANK and Eagle keys")
    yoda_keys = set(directory(additions[3]))
    require(len(yoda_keys) == 1 and next(iter(yoda_keys))[1] == 0xA14E8DFA2CD117E2 and not yoda_keys & {BANK, EAGLE, OBSERVER}, "Exactly one disjoint Yoda Lua key")
    expected_keys = yoda_keys | {BANK, EAGLE}
    previous = {state_key(s): s for s in old["selections"]}
    require(selected["passed"] and selected["requestedStates"] == 96 and selected["uniqueFileSets"] == 16 and selected["manifestSha256"] == pin(manifest_path)["sha256"] and {state_key(s) for s in selected["selections"]} == previous.keys(), "All 96 actual selector states bound to candidate")
    winner_cache = {}

    def winners(report: dict[str, Any], state: dict[str, Any], tag: str) -> dict[tuple[int, int], str]:
        cache_key = tag, state["fileSet"]
        if cache_key not in winner_cache:
            result = {}
            for row in report["fileSets"][state["fileSet"]]:
                if MAIN.fullmatch(row["name"]) and row["size"]:
                    result.update({key: row["sha256"] for key in directory(row)})
            winner_cache[cache_key] = result
        return winner_cache[cache_key]

    comparisons = []
    for state in selected["selections"]:
        before_state = previous[state_key(state)]
        before, after = old["fileSets"][before_state["fileSet"]], selected["fileSets"][state["fileSet"]]
        empire = state["mode"] == "EmpireDivers"
        require(state["effectiveOptions"] == before_state["effectiveOptions"] and after[:len(before)] == before, "All prior physical rows/order/options retained")
        if empire:
            require(len(after) == len(before) + 9 and [r["sha256"] for r in after[len(before):]] == [r["sha256"] for r in additions], "Empire appends exactly nine ordered files")
        else:
            require(after == before, "Every Clone/Commando physical row exact")
        numbers = {}
        for row in after:
            archive, number, _ = group(row["name"])
            numbers.setdefault(archive, set()).add(number)
        require(all(sorted(v) == list(range(len(v))) for v in numbers.values()), "Dense patch numbering")
        old_winners, new_winners = winners(old, before_state, "old"), winners(selected, state, "new")
        changed = {k for k in old_winners.keys() | new_winners.keys() if old_winners.get(k) != new_winners.get(k)}
        require(changed == (expected_keys if empire else set()), "Only BANK, Eagle and Yoda winning typed resources change")
        require(old_winners.get(OBSERVER) == new_winners.get(OBSERVER), "Observer winning identity exact")
        if empire:
            require(new_winners[BANK] == additions[0]["sha256"] and new_winners[EAGLE] == additions[6]["sha256"] and all(new_winners[k] == additions[3]["sha256"] for k in yoda_keys), "Actual new typed winners")
        comparisons.append({"profile": state["profile"], "mode": state["mode"], "fileSet": state["fileSet"], "before": len(before), "after": len(after), "changedTypedKeys": len(changed), "passed": True})
    out.mkdir(parents=True)
    (out / "fresh-directories.json").write_text(json.dumps(dir_pins, indent=2) + "\n", encoding="utf-8")
    report = {"passed": True, "tool": pin(Path(__file__)), "composerTool": pin(ROOT / "tools/public-r23-pack.py"), "baseline": pin(baseline_path), "candidate": pin(manifest_path), "actualSelector": pin(selected_path), "priorSelector": pin(old_path),
              "all1800PriorRowsExact": True, "allUnreviewedManifestFieldsExact": True, "threeNewTriads": additions, "allNewAssetsFreshlyHashed": True,
              "eagleVersionOnlyWholeArchiveReversible": True, "all56EagleContextFixturesPassed": True, "all64CloneCommandoStatesExact": True, "all32EmpireStatesOnlyOrderedAdditions": True, "allPhysicalPatchIndicesDense": True,
              "changedEmpireTypedKeys": [f"{k[0]:016x}.{k[1]:016x}" for k in sorted(expected_keys)], "allOtherBanksStreamsMaterialsTexturesGeometryObserverWinnersExact": True,
              "actual96SelectionComparisons": comparisons, "freshDirectories": pin(out / "fresh-directories.json"), "priorWholeMainHashProofReused": proof,
              "gameSettingsOrDeploymentChanged": False, "runtimeAccepted": False}
    (out / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return {"passed": True, "report": pin(out / "report.json"), "freshDirectories": len(dir_pins), "empireTypedDelta": len(expected_keys)}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inputs", type=Path, default=LANE / "inputs-v1")
    parser.add_argument("--out", type=Path, default=LANE / "qa/composition-peer-v1")
    print(json.dumps(run(parser.parse_args().inputs.resolve(), parser.parse_args().out.resolve())))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
