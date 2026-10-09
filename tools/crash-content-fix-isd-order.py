#!/usr/bin/env python3
"""Correct only the public logical429 resource-table order, without repacking data."""
from __future__ import annotations

from collections import Counter
import copy
import hashlib
import json
from pathlib import Path
import struct
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
AREA = ROOT / "dist/empire-launch-crash-2026-10-08/loop-2026-10-09"
OUT = AREA / "fixed-isd-order-v1"
PUBLIC = ROOT / "dist/production-r23-2026-10-08/release-ready-v1/manifest.json"
SOURCE = ROOT / "dist/empire-finish-2026-10-08/ships/alignment-candidate-v1/bundle/9ba626afa44a3aa3.patch_0"
GAME = Path("C:/Program Files (x86)/Steam/steamapps/common/Helldivers 2")
ROW, TYPE = struct.Struct("<7Q6I"), struct.Struct("<IIQIIII")
NOTES = "Local-only full public r23 startup test: logical429 Star Destroyer MAIN resource rows are sorted by native type/name order with dense ordinals. Every resource payload, offset, companion and other pack row is unchanged. Startup and Star Destroyer appearance remain to be tested."


def read(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def pin(path: Path) -> dict[str, Any]:
    raw = path.read_bytes()
    return {"path": str(path.resolve()), "bytes": len(raw), "sha256": sha(raw)}


def require(value: Any, message: str) -> None:
    if not value:
        raise ValueError(message)


def parse(raw: bytes) -> tuple[list[tuple[int, ...]], list[tuple[int, ...]], int]:
    magic, nt, nf = struct.unpack_from("<III", raw)
    require(magic == 0xF0000011 and nt == 3 and nf == 6, "Exact original six-resource ship closure")
    start = 72 + nt * TYPE.size
    types = list(TYPE.iter_unpack(raw[72:start]))
    rows = list(ROW.iter_unpack(raw[start:start + nf * ROW.size]))
    require(len({row[:2] for row in rows}) == nf and all(row[2] + row[7] <= len(raw) for row in rows), "Unique bounded native resource directory")
    return types, rows, start


def valid_grouping(types: list[tuple[int, ...]], rows: list[tuple[int, ...]]) -> bool:
    return [row[1] for row in rows] == [typ[2] for typ in types for _ in range(typ[3])] and [row[:2] for row in rows] == sorted((row[:2] for row in rows), key=lambda key: (key[1], key[0])) and [row[12] for row in rows] == list(range(len(rows)))


def run() -> dict[str, Any]:
    require(not OUT.exists(), "Fresh immutable output required")
    source = read(PUBLIC)
    require(pin(PUBLIC)["sha256"] == "b08614df277dad2388a70ddeab7e230fa9990b7045e0abbe3c687e2c6966e5f7", "Exact sealed public r23 manifest")
    rows_by_name = {row["name"]: row for row in source["pack"]["files"]}
    original = rows_by_name["9ba626afa44a3aa3.patch_429"]
    raw = SOURCE.read_bytes()
    require(sha(raw) == original["sha256"] == "8672ec27fa2e94a52f2734b93b3015ef2388f1bd6d5795b283902f4dd2fb4b90" and len(raw) == original["size"] == 279824, "Original public MAIN source identity")
    types, rows, start = parse(raw)
    require([typ[2] for typ in types] == sorted(typ[2] for typ in types) and Counter(row[1] for row in rows) == Counter({typ[2]: typ[3] for typ in types}), "Type table order/counts already correct")
    require(not valid_grouping(types, rows) and [row[:2] for row in rows] == sorted(row[:2] for row in rows), "Original name-first table reproduces independent finding")
    changed = bytearray(raw)
    sorted_rows = []
    for index, row in enumerate(sorted(rows, key=lambda row: (row[1], row[0]))):
        value = list(row)
        value[12] = index
        sorted_rows.append(tuple(value))
        ROW.pack_into(changed, start + index * ROW.size, *value)
    result = bytes(changed)
    after_types, after_rows, after_start = parse(result)
    end = start + len(rows) * ROW.size
    require(valid_grouping(after_types, after_rows) and after_types == types and after_start == start, "Native contiguous type-table ranges, sorted names and dense ordinals corrected")
    require(result[:start] == raw[:start] and result[end:] == raw[end:] and len(result) == len(raw), "Every byte outside the original resource row table remains exact")
    before_keys, after_keys = {row[:2]: row for row in rows}, {row[:2]: row for row in after_rows}
    payloads = []
    for key, old in before_keys.items():
        new = after_keys[key]
        require(old[:12] == new[:12], "All keyed descriptor fields exact except dense ordinal")
        require(raw[old[2]:old[2] + old[7]] == result[new[2]:new[2] + new[7]], "All MAIN payload bytes exact")
        payloads.append({"key": f"{key[0]:016x}.{key[1]:016x}", "originalOrdinal": old[12], "correctedOrdinal": new[12], "allOtherDescriptorFieldsExact": True, "mainOffset": old[2], "mainBytes": old[7], "mainSha256": sha(raw[old[2]:old[2] + old[7]]), "streamOffset": old[3], "streamBytes": old[8], "gpuOffset": old[4], "gpuBytes": old[9]})
    companion_pins = []
    for suffix in (".stream", ".gpu_resources"):
        expected = rows_by_name[original["name"] + suffix]
        path = SOURCE.with_name(SOURCE.name + suffix)
        require(pin(path)["sha256"] == expected["sha256"] and path.stat().st_size == expected["size"], "Original companion source identity exact")
        companion_pins.append({"manifestRow": expected, "source": pin(path)})
    # Compare each resource payload against the original correctly grouped sibling.
    sibling_row = rows_by_name["9ba626afa44a3aa3.patch_367"]
    sibling_path = GAME / "mods_download" / sibling_row["sha256"]
    require(pin(sibling_path)["sha256"] == sibling_row["sha256"], "Correctly grouped sibling MAIN source pin")
    sibling = sibling_path.read_bytes()
    sibling_types, sibling_rows, _ = parse(sibling)
    require(valid_grouping(sibling_types, sibling_rows) and {row[:2] for row in sibling_rows} == set(before_keys), "Sibling has exactly same six resources in correct type/name order")
    sibling_gpu_row = rows_by_name["9ba626afa44a3aa3.patch_367.gpu_resources"]
    sibling_gpu_path = GAME / "mods_download" / sibling_gpu_row["sha256"]
    require(pin(sibling_gpu_path)["sha256"] == sibling_gpu_row["sha256"], "Correctly grouped sibling GPU source pin")
    gpu, sibling_gpu = SOURCE.with_name(SOURCE.name + ".gpu_resources").read_bytes(), sibling_gpu_path.read_bytes()
    for old in sibling_rows:
        new = before_keys[old[:2]]
        require(sibling[old[2]:old[2] + old[7]] == raw[new[2]:new[2] + new[7]] and sibling_gpu[old[4]:old[4] + old[9]] == gpu[new[4]:new[4] + new[9]], "Sibling MAIN/GPU payload bytes all exact")
    different = [index for index, (old, new) in enumerate(zip(raw, result, strict=True)) if old != new]
    runs = []
    for index in different:
        if not runs or index != runs[-1][1]:
            runs.append([index, index + 1])
        else:
            runs[-1][1] += 1
    require(different and all(start <= index < end for index in different), "Explicit byte delta confined to original480B table")
    bad_ordinal = list(after_rows)
    value = list(bad_ordinal[0]); value[12] = 5; bad_ordinal[0] = tuple(value)
    bad_count = list(types)
    value = list(bad_count[0]); value[3] += 1; bad_count[0] = tuple(value)
    require(not valid_grouping(types, rows) and not valid_grouping(types, bad_ordinal) and not valid_grouping(bad_count, after_rows), "Original interleaving, bad ordinals and bad type counts all rejected")
    target = copy.deepcopy(source)
    index = next(index for index, row in enumerate(target["pack"]["files"]) if row["name"] == original["name"])
    corrected_row = copy.deepcopy(original)
    corrected_row["sha256"] = sha(result)
    corrected_row["url"] = "http://127.0.0.1:18165/assets/" + sha(result)
    target["pack"]["files"][index] = corrected_row
    target["pack"]["statusNotes"] = NOTES
    target["pack"]["totalSize"] = sum(row["size"] for row in target["pack"]["files"])
    OUT.mkdir(parents=True)
    (OUT / "assets").mkdir()
    asset_path = OUT / "assets" / sha(result)
    asset_path.write_bytes(result)
    manifest_path = OUT / "manifest.json"
    manifest_path.write_text(json.dumps(target, indent=2) + "\n", encoding="utf-8")
    report = {"passed": True, "tool": pin(Path(__file__)), "sourceManifest": pin(PUBLIC), "manifest": pin(manifest_path), "sourceMain": pin(SOURCE), "correctedMain": pin(asset_path), "changes": [{"index": index, "before": original, "after": corrected_row}], "sourceCompanions": companion_pins, "sourceSiblingMain": pin(sibling_path), "sourceSiblingGpu": pin(sibling_gpu_path), "siblingHasSameSixMainAndGpuPayloads": True, "sourceRows": [list(row) for row in rows], "correctedRows": [list(row) for row in after_rows], "typeRowsExact": [list(row) for row in types], "resourcePayloadProofs": payloads, "resourceRowTable": {"offset": start, "bytes": end - start, "endExclusive": end}, "wholeByteDelta": {"changedByteCount": len(different), "allChangedOffsets": different, "contiguousChangedRanges": [{"offset": begin, "bytes": finish - begin, "beforeHex": raw[begin:finish].hex(), "afterHex": result[begin:finish].hex()} for begin, finish in runs], "everyByteOutsideResourceRowsExact": True, "sizeUnchanged": True, "headerTypeTableAndAllPayloadOffsetsSizesAlignmentsExact": True}, "typeGroupingPassed": True, "negativeControls": ["original name-first row interleaving rejected", "non-dense ordinal rejected", "type-count mismatch rejected"], "builderFix": pin(ROOT / "tools/finish-ships-build.py"), "literalStatusNotes": NOTES, "newPayloadAssets": 1, "hostOrPublicWrites": 0, "runtimeAccepted": False, "rootCauseProvenByLaunch": False}
    report_path = OUT / "report.json"
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"manifest": pin(manifest_path), "report": pin(report_path), "correctedMain": pin(asset_path), "changedByteCount": len(different)}))
    return report


if __name__ == "__main__":
    try:
        run()
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"ISD order correction failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
