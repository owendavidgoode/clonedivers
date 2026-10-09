#!/usr/bin/env python3
"""Combine exactly the two reviewed native archive corrections on public r23."""
from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
LOOP = ROOT / "dist/empire-launch-crash-2026-10-08/loop-2026-10-09"
OUT = LOOP / "fixed-public-r23-minimal-v1"
SOURCE = ROOT / "dist/production-r23-2026-10-08/release-ready-v1/manifest.json"
NOTES = "Local-only full public r23 startup test: logical453 Constitution MAIN is zero-padded to256 bytes and logical429 Star Destroyer MAIN rows use native type/name order with dense ordinals. All original resources, companions, features, variants, options and other rows are preserved. Actual Clone/Empire startup and appearance remain to be tested."


def read(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def pin(path: Path) -> dict[str, Any]:
    raw = path.read_bytes()
    return {"path": str(path.resolve()), "bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}


def require(value: Any, message: str) -> None:
    if not value:
        raise ValueError(message)


def main() -> int:
    require(not OUT.exists(), "Fresh immutable composer output required")
    source = read(SOURCE)
    require(pin(SOURCE)["sha256"] == "b08614df277dad2388a70ddeab7e230fa9990b7045e0abbe3c687e2c6966e5f7", "Exact public source")
    isd_path, audio_path = LOOP / "fixed-isd-order-v1/report.json", LOOP / "fixed-constitution-v1/report.json"
    isd, audio = read(isd_path), read(audio_path)
    require(isd["passed"] and audio["passed"] and audio["sourceManifestSha256"] == pin(SOURCE)["sha256"] and isd["sourceManifest"]["sha256"] == pin(SOURCE)["sha256"], "Both corrections bound to exact public source")
    changes = isd["changes"] + audio["changes"]
    require(len(changes) == 2 and {row["before"]["name"] for row in changes} == {"9ba626afa44a3aa3.patch_429", "9ba626afa44a3aa3.patch_453"}, "Only two intended MAIN rows")
    target = copy.deepcopy(source)
    changed_rows = []
    for change in changes:
        index = change["index"]
        require(target["pack"]["files"][index] == change["before"], "Exact original source row")
        after = copy.deepcopy(change["after"])
        allowed = copy.deepcopy(change["before"])
        for field in ("sha256", "size", "url"):
            allowed[field] = after[field]
        require(allowed == after, "Only MAIN SHA/size/URL mutate; gates/variants/name/order exact")
        target["pack"]["files"][index] = after
        changed_rows.append({"index": index, "before": change["before"], "after": after})
    require(len(target["pack"]["files"]) == len(source["pack"]["files"]) == 1809, "All public selected/unselected rows retained")
    target["pack"]["statusNotes"] = NOTES
    target["pack"]["totalSize"] = sum(row["size"] for row in target["pack"]["files"])
    require(target["pack"]["version"] == source["pack"]["version"] == "2026.10.08-r23", "Exact runtime context version preserved")
    assets = []
    for change, folder in ((isd["changes"][0], LOOP / "fixed-isd-order-v1/assets"), (audio["changes"][0], LOOP / "fixed-constitution-v1/assets")):
        row = change["after"]
        path = folder / row["sha256"]
        proof = pin(path)
        require(proof["sha256"] == row["sha256"] and proof["bytes"] == row["size"], "Exact durable replacement asset")
        assets.append({**proof, "size": row["size"]})
    OUT.mkdir(parents=True)
    manifest = OUT / "manifest.json"
    manifest.write_text(json.dumps(target, indent=2) + "\n", encoding="utf-8")
    catalog = OUT / "assets.json"
    catalog.write_text(json.dumps({"manifest": pin(manifest), "assets": assets, "downloadsNeeded": 0}, indent=2) + "\n", encoding="utf-8")
    report = {"passed": True, "tool": pin(Path(__file__)), "sourceManifest": pin(SOURCE), "manifest": pin(manifest), "isdCorrection": pin(isd_path), "constitutionCorrection": pin(audio_path), "changes": changed_rows, "globalRows": 1809, "allUnselectedVariantsPreserved": True, "allOtherManifestRowsAndFieldsExactExceptNotesTotal": True, "literalStatusNotes": NOTES, "expectedEmpireFiles": 1396, "expectedCloneFiles": 1199, "replacementAssetCatalog": pin(catalog), "assets": assets, "newPayloadAssetBytes": sum(row["bytes"] for row in assets), "downloadsNeeded": 0, "hostOrPublicWrites": 0, "actualSelectorsPending": True, "runtimeAccepted": False, "rootCauseProvenByLaunch": False}
    output = OUT / "report.json"
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"manifest": pin(manifest), "report": pin(output), "assetCatalog": pin(catalog), "assets": assets, "literalStatusNotes": NOTES}))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"Minimal fixes composer failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
