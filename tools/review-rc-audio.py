#!/usr/bin/env -S uv run
"""Run a resumable, local-only Whisper review of distinct Republic Commando audio.

Requires the isolated audio-review environment and a downloaded model snapshot.
Subtitles are compared after recognition, never supplied to the recognizer.
"""

import argparse
from collections import Counter, defaultdict
import csv
from difflib import SequenceMatcher
import hashlib
import importlib.metadata
import json
import logging
import math
import os
from pathlib import Path
import re
import sys
import time
from typing import Any

LOG = logging.getLogger(__name__)


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def normalized(text: str) -> str:
    text = re.sub(r"^\s*(?:delta\s*\d+|boss|sev|fixer|scorch)\s*:\s*", "", text, flags=re.I)
    return " ".join(re.findall(r"[a-z0-9]+", text.lower().replace("’", "'")))


def make_row(source: dict[str, Any], segments: list[Any], samples: Any,
             used: dict[str, list[dict[str, Any]]]) -> dict[str, Any]:
    import numpy as np

    text = " ".join(s.text.strip() for s in segments).strip()
    refs = sorted({s["text"] for alias in source["aliases"] for s in alias.get("subtitles", [])})
    scores = [(SequenceMatcher(None, normalized(text).split(), normalized(ref).split()).ratio(), ref)
              for ref in refs]
    score, best = max(scores, default=(None, None))
    flags = []
    if not text:
        flags.append("no_speech_recognized")
    if any(s.avg_logprob < -1.0 or s.no_speech_prob > .6 or s.compression_ratio > 2.4 for s in segments):
        flags.append("uncertain_recognition")
    if refs and score is not None and score < .8:
        flags.append("subtitle_disagreement")
    if not refs:
        flags.append("no_subtitle_reference")
    if re.search(r"\b(?:fixer|scorch|sev|geonosis|kashyyyk|trandoshan|tarfful)\b", text, re.I):
        flags.append("named_character_or_setting")
    if len(text.split()) > 14:
        flags.append("long_callout")
    peak = float(np.max(np.abs(samples)))
    rms = float(np.sqrt(np.mean(samples.astype(np.float64) ** 2)))
    return {
        "sha256": source["sha256"], "character": source["character"],
        "file": source["file"], "objects": [r["object"] for r in source["aliases"]],
        "duration_seconds": len(samples) / 16000,
        "transcript": text, "subtitle_references": refs, "best_subtitle": best,
        "subtitle_word_similarity": score, "flags": flags,
        "peak_dbfs": 20 * math.log10(peak) if peak else None,
        "rms_dbfs": 20 * math.log10(rms) if rms else None,
        "near_full_scale_samples": int(np.sum(np.abs(samples) >= .999)),
        "shipped_targets": used.get(source["sha256"], []),
        "segments": [{"text": s.text, "avg_logprob": s.avg_logprob,
                      "no_speech_prob": s.no_speech_prob,
                      "compression_ratio": s.compression_ratio} for s in segments],
        "assessment": "Automated transcription and signal checks; not a human listening judgment or in-game mix validation.",
    }


def summarize(rows: list[dict[str, Any]], total: int, output: Path) -> None:
    flags = Counter(flag for row in rows for flag in row["flags"])
    shipped = [r for r in rows if r["shipped_targets"]]
    result = {
        "reviewed": len(rows), "expected": total, "complete": len(rows) == total,
        "audio_minutes": sum(r["duration_seconds"] for r in rows) / 60,
        "shipped_recordings_reviewed": len(shipped),
        "flags": dict(flags),
        "shipped_flags": dict(Counter(f for r in shipped for f in r["flags"])),
        "subtitle_exact_matches": sum(r["subtitle_word_similarity"] == 1 for r in rows),
        "subtitle_close_matches": sum(r["subtitle_word_similarity"] is not None and r["subtitle_word_similarity"] >= .8 for r in rows),
        "characters": dict(Counter(r["character"] for r in rows)),
        "paid_api_cost_usd": 0, "audio_uploaded": False, "game_files_modified": False,
        "limitations": "ASR may hallucinate on grunts and radio noise. Agreement is with reference subtitles, not a calibrated correctness probability. Missing subtitles and content flags need model-assisted editorial review. RMS measures level, not perceived loudness.",
    }
    (output / "summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    columns = ["character", "sha256", "duration_seconds", "transcript", "best_subtitle",
               "subtitle_word_similarity", "flags", "rms_dbfs", "shipped_target_count", "file"]
    with (output / "review.csv").open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=columns)
        writer.writeheader()
        for row in rows:
            record = {key: row.get(key) for key in columns}
            record["flags"] = "; ".join(row["flags"])
            record["shipped_target_count"] = len(row["shipped_targets"])
            writer.writerow(record)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--batch-size", type=int, default=16)
    parser.add_argument("--limit", type=int)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    try:
        if args.batch_size < 1 or (args.limit is not None and args.limit < 1):
            raise ValueError("Batch size and optional limit must be positive")
        os.environ["HF_HUB_OFFLINE"] = "1"
        os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
        import numpy as np
        from faster_whisper import BatchedInferencePipeline, WhisperModel
        from faster_whisper.audio import decode_audio

        root = args.workspace.resolve()
        catalog_path = root / "dist/rc-upgrade/rc-delta-candidates.json"
        mapping_path = root / "build-inputs/rc/voice-mapping.json"
        sources: dict[str, dict[str, Any]] = {}
        for row in read_json(catalog_path):
            source = sources.setdefault(row["sha256"], {**row, "aliases": []})
            source["aliases"].append(row)
        used: dict[str, list[dict[str, Any]]] = defaultdict(list)
        for slot in read_json(mapping_path)["slots"]:
            for row in slot["rows"]:
                used[row["rc_sha256"]].append({"character": slot["character"],
                    "media_id": row["hd2_media_id"], "game_text": row["hd2_text"], "semantic": row["semantic"]})
        if not set(used) <= set(sources):
            raise ValueError("Shipped audio missing from full catalog")
        ordered = sorted(sources.values(), key=lambda r: (r["sha256"] not in used, r["character"] != "Boss", r["sha256"]))
        args.out.mkdir(parents=True, exist_ok=True)
        config = {"catalog_sha256": digest(catalog_path), "mapping_sha256": digest(mapping_path),
                  "model_snapshot": str(args.model.resolve()), "model_sha256": digest(args.model / "model.bin"),
                  "packages": {name: importlib.metadata.version(name) for name in ["faster-whisper", "ctranslate2", "av", "numpy"]},
                  "language": "en", "beam_size": 3, "temperature": 0,
                  "subtitles_supplied_to_recognizer": False, "clip_isolation": "Each WAV decoded independently as a separate explicitly bounded batch chunk; trailing silence only.",
                  "batch_size": args.batch_size}
        config_path = args.out / "run.json"
        if config_path.exists() and read_json(config_path) != config:
            raise ValueError("Resume configuration differs; use a fresh output directory")
        config_path.write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")
        journal = args.out / "clips.jsonl"
        reviewed = [json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()] if journal.exists() else []
        done = {r["sha256"] for r in reviewed}
        if len(done) != len(reviewed) or not done <= set(sources):
            raise ValueError("Invalid resume journal")
        pending = [r for r in ordered if r["sha256"] not in done]
        if args.limit:
            pending = pending[:args.limit]
        model = WhisperModel(str(args.model.resolve()), device="cuda", compute_type="float16", local_files_only=True)
        pipeline = BatchedInferencePipeline(model)
        logging.getLogger("faster_whisper").setLevel(logging.WARNING)
        started = time.monotonic()
        with journal.open("a", encoding="utf-8") as handle:
            for offset in range(0, len(pending), args.batch_size):
                batch = pending[offset:offset + args.batch_size]
                waves, padded, clips, starts = [], [], [], {}
                cursor = 0
                for index, source in enumerate(batch):
                    path = Path(source["file"])
                    if digest(path) != source["sha256"]:
                        raise ValueError(f"Source changed: {path}")
                    samples = decode_audio(str(path), sampling_rate=16000)
                    if not len(samples) or len(samples) > 28 * 16000:
                        raise ValueError("Unexpected empty or long clip")
                    waves.append(samples)
                    size = (math.ceil(len(samples) / 16000) + 1) * 16000
                    padded.append(np.pad(samples, (0, size - len(samples))))
                    starts[cursor // 160] = index
                    clips.append({"start": cursor / 16000, "end": (cursor + size) / 16000})
                    cursor += size
                segments, _ = pipeline.transcribe(np.concatenate(padded), language="en", beam_size=3,
                    temperature=0, condition_on_previous_text=False, vad_filter=False,
                    without_timestamps=True, max_new_tokens=128, batch_size=args.batch_size,
                    clip_timestamps=clips)
                grouped: dict[int, list[Any]] = defaultdict(list)
                for segment in segments:
                    if segment.seek not in starts:
                        raise ValueError(f"Transcript escaped explicit source boundary: {segment.seek}")
                    grouped[starts[segment.seek]].append(segment)
                for index, source in enumerate(batch):
                    result = make_row(source, grouped[index], waves[index], used)
                    handle.write(json.dumps(result, ensure_ascii=False, allow_nan=False) + "\n")
                    reviewed.append(result)
                handle.flush()
                LOG.info("Reviewed %d/%d distinct clips (%.1f seconds this run)", len(reviewed), len(sources), time.monotonic() - started)
                summarize(reviewed, len(sources), args.out)
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        LOG.exception("Local audio review failed; completed journal rows retained")
        return 1


if __name__ == "__main__":
    sys.exit(main())
