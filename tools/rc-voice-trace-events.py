#!/usr/bin/env python3
"""Trace subtitle-labeled HD2 voice media to current bank event paths without changing assets."""

import argparse
import csv
import hashlib
import json
import logging
import struct
import sys
from collections.abc import Sequence
from pathlib import Path
from typing import Any

LOGGER = logging.getLogger(__name__)
BANKS = {
    1: "helldiver_soldier_female1_VO.bnk",
    2: "helldiver_soldier_male1_VO.bnk",
    3: "helldiver_soldier_female2_VO.bnk",
    4: "helldiver_purist_VO.bnk",
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_chunks(path: Path) -> dict[str, bytes]:
    data = path.read_bytes()
    result = {}
    offset = 0
    while offset < len(data):
        if offset + 8 > len(data):
            raise ValueError(f"Truncated chunk header: {path}")
        tag = data[offset:offset + 4].decode("ascii")
        size = struct.unpack_from("<I", data, offset + 4)[0]
        end = offset + 8 + size
        if end > len(data) or tag in result:
            raise ValueError(f"Invalid/duplicate chunk {tag}: {path}")
        result[tag] = data[offset + 8:end]
        offset = end
    version = struct.unpack_from("<I", result["BKHD"])[0] ^ 0x9211BCAC
    if version != 154:
        raise ValueError(f"Expected current bank version 154, found {version}: {path}")
    return result


def node_path(entries: dict[int, Any], start: int, target: int,
              seen: frozenset[int] = frozenset()) -> list[int] | None:
    if start == target:
        return [start]
    if start in seen or start not in entries:
        return None
    children_object = getattr(entries[start], "children", None)
    children = getattr(children_object, "children", [])
    for child in children:
        found = node_path(entries, child, target, seen | {start})
        if found:
            return [start, *found]
    return None


def trace_bank(bank_path: Path, transcript: Path, voice: int, parser: Any) -> dict[str, Any]:
    hierarchy = parser()
    hierarchy.load(read_chunks(bank_path)["HIRC"])
    labeled = []
    with transcript.open(encoding="utf-8-sig", newline="") as handle:
        for row in csv.DictReader(handle):
            semantic = row["text"].strip().rstrip(".!").lower()
            if semantic in {"affirmative", "negative"}:
                labeled.append((semantic, row))
    rows = []
    for semantic, label in labeled:
        media_id = int(label["wem_short_id"])
        sounds = [sound for sound in hierarchy.sounds
                  if any(source.source_id == media_id for source in sound.sources)]
        sound_records = []
        for sound in sounds:
            paths = []
            for event in hierarchy.events:
                for action_id in event.ulActionIDs:
                    action = hierarchy.entries.get(action_id)
                    if action is None or not hasattr(action, "idExt"):
                        continue
                    found = node_path(hierarchy.entries, action.idExt, sound.hierarchy_id)
                    if found:
                        paths.append({"event_id": event.hierarchy_id, "action_id": action_id,
                                      "action_type": action.ulActionType,
                                      "target_to_sound_ids": found})
            sound_records.append({"sound_id": sound.hierarchy_id,
                                  "sources": [vars(source) for source in sound.sources],
                                  "event_paths": paths})
        rows.append({"semantic": semantic, "transcribed_text": label["text"],
                     "media_id": media_id,
                     "archive_resource_hash": f"{int(label['archive_file_id']):016x}",
                     "transcript_size_bytes": int(label["size_bytes"]),
                     "source_present_in_current_bank": bool(sounds), "sounds": sound_records})
    return {"native_voice_type": voice, "bank": str(bank_path), "bank_sha256": sha256(bank_path),
            "transcript": str(transcript), "transcript_sha256": sha256(transcript), "rows": rows}


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--out", type=Path)
    parser.add_argument("-v", "--verbose", action="store_true")
    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.DEBUG if args.verbose else logging.INFO)
    root = args.workspace.resolve() / "dist/rc-upgrade"
    sys.path.insert(0, str(root / "hd2-audio-modder"))
    try:
        from wwise_hierarchy_154 import WwiseHierarchy_154

        reports = []
        for voice, bank in BANKS.items():
            transcripts = list((root / "hd2-voice-transcripts").glob(f"voice{voice}_*.csv"))
            if len(transcripts) != 1:
                raise ValueError(f"Expected one transcript for voice {voice}")
            report = trace_bank(root / "voice-bank-wwise/content/audio/us" / bank,
                                transcripts[0], voice, WwiseHierarchy_154)
            reports.append(report)
            matched = sum(row["source_present_in_current_bank"] for row in report["rows"])
            LOGGER.info("Voice %s: %s/%s media IDs present", voice, matched, len(report["rows"]))
        output = args.out or root / "rc-voice-current-event-map.json"
        output.write_text(json.dumps({
            "semantic_label_source": "https://gist.github.com/trashguy/a25afb2612dd56c05ac61d6d8f40d579",
            "validation": "Semantic labels are community transcriptions; current source membership and explicit event/action/child edges verified. No runtime playback test.",
            "banks": reports,
        }, indent=2, default=str), encoding="utf-8")
        LOGGER.info("Saved %s", output)
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, ImportError, AssertionError):
        LOGGER.exception("Voice event tracing failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
