#!/usr/bin/env python3
"""Validate the staged expansion against the shipped mapping, audit, and archive."""

import argparse
import csv
import hashlib
import importlib.util
import json
import re
import struct
from pathlib import Path
from typing import Any


def read(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def check(root: Path, stage: Path) -> dict[str, Any]:
    baseline_path = root / "build-inputs/rc/voice-mapping.json"
    baseline_hash = hashlib.sha256(baseline_path.read_bytes()).hexdigest()
    assert baseline_hash == "35e7187d84ab9ecfb0c46f8451fcc03e46a2029c2d51b0b607024b077d01e62a"
    baseline = read(baseline_path)
    mapping = read(stage / "mapping.json")
    summary = read(stage / "summary.json")
    report = read(stage / "patch/build-report.json")
    audited = {r["sha256"]: r for r in map(json.loads, (root / "dist/rc-audio-review-2026-09-22/clips.jsonl").read_text(encoding="utf-8").splitlines())}
    old_sources = {row["rc_sha256"] for slot in baseline["slots"] for row in slot["rows"]}
    assert report["mapping_sha256"] == hashlib.sha256((stage / "mapping.json").read_bytes()).hexdigest()
    spec = importlib.util.spec_from_file_location("archive_builder", root / "tools/build-rc-voices.py")
    assert spec and spec.loader
    builder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(builder)
    template = (stage / "template/9ba626afa44a3aa3.patch_159").read_bytes()
    patch = (stage / "patch/9ba626afa44a3aa3.patch_0").read_bytes()
    original_entries = builder.archive_entries(template)
    new_entries = builder.archive_entries(patch)
    protected_count = 0
    used = set()
    for slot in mapping["slots"]:
        number = slot["slot"]
        rows = slot["rows"]
        assert slot["character"] == builder.CHARACTERS[number]
        ids = {row["hd2_media_id"] for row in rows}
        assert len(ids) == len(rows) == slot["mapped"]
        path = next((root / "dist/rc-upgrade/hd2-voice-transcripts").glob(f"voice{number}_*.csv"))
        with path.open(encoding="utf-8-sig", newline="") as handle:
            targets = {int(row["wem_short_id"]): row["text"] for row in csv.DictReader(handle)}
        fallback = {row["hd2_media_id"] for row in mapping["unmapped"] if row["slot"] == number}
        assert not ids & fallback and ids | fallback == set(targets)
        assert len(fallback) == slot["unmapped"]
        for media, text in targets.items():
            # Coordinates, distances, extraction boarding and kill claims need specific evidence.
            if re.fullmatch(r"(?:\d+(?: meters)?|north|south|east|west|last one down|entering shuttle)[.! ]*", text, re.I):
                assert media in fallback, (number, media, text)
                protected_count += 1
        for row in rows:
            sha = row["rc_sha256"]
            used.add(sha)
            assert audited[sha]["character"] == slot["character"]
            assert hashlib.sha256((root / row["rc_wav"]).read_bytes()).hexdigest() == sha
            assert row["rc_duration_seconds"] <= 4.5
            assert not any(re.search(r"\b(?:three[- ]eight|boss|fixer|scorch|sev|geonosis|kashyyyk)\b", re.sub(r"^Delta \d+:\s*", "", t), re.I) for t in row["rc_text"])
            if sha not in old_sources:
                assert (audited[sha]["subtitle_word_similarity"] or 0) >= .8
                assert not set(audited[sha]["flags"]) & {"uncertain_recognition", "no_speech_recognized"}
            if row["semantic"] in {"attack", "battle_cry"}:
                assert not any(re.search(r"\b(?:destroyed|dead|killed|terminated|eliminated|secured|downed)\b", text, re.I) for text in row["rc_text"])
        # Restore the reported codec fields, then compare the entire bank byte-for-byte.
        bank_key = (builder.BANK_IDS[number], builder.BANK)
        before = original_entries[bank_key]
        after = new_entries[bank_key]
        original_bank = template[before[2]:before[2] + before[7]]
        restored_bank = bytearray(patch[after[2]:after[2] + after[7]])
        bank_report = next(s for s in report["slots"] if s["slot"] == number)
        assert {r["media_id"] for r in bank_report["changes"]} == ids
        for change in bank_report["changes"]:
            offset = 16 + change["codec_offset"]
            assert struct.unpack_from("<I", restored_bank, offset)[0] == 0x10001
            struct.pack_into("<I", restored_bank, offset, 0x40001)
        assert restored_bank == original_bank
    assert len(used) == summary["after_unique"] == mapping["unique_rc_recordings"]
    assert len(used-old_sources) == summary["new_recordings"]
    assert len(old_sources-used) == summary["retired_recordings"]
    for file in report["files"]:
        data = (stage / "patch" / file["name"]).read_bytes()
        assert len(data) == file["size"] and hashlib.sha256(data).hexdigest() == file["sha256"]
    return {"passed": True, "unique_recordings": len(used), "mapped_entries": sum(s["mapped"] for s in mapping["slots"]), "protected_fallback_entries": protected_count, "all_four_banks_codec_changes_only": True, "shipped_mapping_unchanged": True, "runtime_tested": False}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--stage", type=Path, required=True)
    args = parser.parse_args()
    result = check(args.workspace.resolve(), args.stage)
    (args.stage / "validation.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
