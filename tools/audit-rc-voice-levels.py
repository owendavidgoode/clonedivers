#!/usr/bin/env -S uv run
"""Audit RC source uniqueness and mapped-clip loudness without changing audio."""

import argparse
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import logging
import math
from pathlib import Path
import statistics
import subprocess
import sys
from typing import Any
import wave

LOG = logging.getLogger(__name__)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def pcm_identity(path: Path) -> str:
    with wave.open(str(path), "rb") as audio:
        fmt = (audio.getnchannels(), audio.getsampwidth(), audio.getframerate())
        return sha(repr(fmt).encode() + audio.readframes(audio.getnframes()))


def measure(ffmpeg: Path, root: Path, row: dict[str, Any]) -> dict[str, Any]:
    path = root / row["rc_wav"]
    if sha(path.read_bytes()) != row["rc_sha256"]:
        raise ValueError(f"Source hash mismatch: {path}")
    result = subprocess.run(
        [str(ffmpeg), "-hide_banner", "-nostdin", "-nostats", "-threads", "1",
         "-i", str(path), "-af", "loudnorm=print_format=json", "-f", "null", "-"],
        capture_output=True, text=True, check=True, timeout=60,
        creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0,
    )
    data = json.loads(result.stderr[result.stderr.rfind("{"):])
    values = {key: float(data[key]) for key in ("input_i", "input_tp")}
    return {"source": row["rc_wav"], "sha256": row["rc_sha256"],
            "pcm_sha256": pcm_identity(path),
            "lufs": values["input_i"] if math.isfinite(values["input_i"]) else None,
            "true_peak_dbtp": values["input_tp"] if math.isfinite(values["input_tp"]) else None}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    try:
        root = args.workspace.resolve()
        mapping_path = root / "build-inputs/rc/voice-mapping.json"
        mapping = read_json(mapping_path)
        lock = read_json(root / "build-inputs/rc/inputs.lock.json")
        if sha(mapping_path.read_bytes()) != lock["portableMappingSha256"]:
            raise ValueError("Mapping differs from release input lock")
        candidates = read_json(root / "dist/rc-upgrade/rc-delta-candidates.json")
        unique = {row["rc_sha256"]: row for slot in mapping["slots"] for row in slot["rows"]}
        ffmpeg = root / "dist/rc-upgrade/filediver-current/filediver-cli/ffmpeg.exe"
        with ThreadPoolExecutor(max_workers=4) as pool:
            measurements = list(pool.map(lambda row: measure(ffmpeg, root, row), unique.values()))
        by_hash = {row["sha256"]: row for row in measurements}
        summaries = []
        for slot in mapping["slots"]:
            hashes = {row["rc_sha256"] for row in slot["rows"]}
            finite = [by_hash[h]["lufs"] for h in hashes if by_hash[h]["lufs"] is not None]
            semantics: dict[str, set[str]] = defaultdict(set)
            for row in slot["rows"]:
                semantics[row["semantic"]].add(row["rc_sha256"])
            summary = {
                "character": slot["character"], "slot": slot["slot"],
                "targets": len(slot["rows"]), "fallback_targets": slot["targets_in_transcript"] - len(slot["rows"]),
                "unique_wav_hashes": len(hashes), "unique_pcm_hashes": len({by_hash[h]["pcm_sha256"] for h in hashes}),
                "measurable_loudness_clips": len(finite), "median_lufs": statistics.median(finite),
                "min_lufs": min(finite), "max_lufs": max(finite),
                "max_true_peak_dbtp": max(by_hash[h]["true_peak_dbtp"] for h in hashes if by_hash[h]["true_peak_dbtp"] is not None),
                "semantic_median_lufs": {
                    semantic: statistics.median(levels) for semantic, group in semantics.items()
                    if (levels := [by_hash[h]["lufs"] for h in group if by_hash[h]["lufs"] is not None])
                },
            }
            summaries.append(summary)
            LOG.info("%s: %d unique recordings, median %.2f LUFS", slot["character"], len(hashes), summary["median_lufs"])
        candidates_pcm = []
        for row in candidates:
            path = Path(row["file"])
            if sha(path.read_bytes()) != row["sha256"]:
                raise ValueError(f"Candidate source hash mismatch: {path}")
            candidates_pcm.append(pcm_identity(path))
        report = {
            "mapping_sha256": sha(mapping_path.read_bytes()),
            "method": "FFmpeg loudnorm input measurements; equal weighting per unique clip, not runtime event frequency. No bank/bus gain or radio processing modeled. Short unmeasurable clips excluded from LUFS summaries.",
            "ffmpeg_sha256": sha(ffmpeg.read_bytes()),
            "mapped_targets": sum(len(slot["rows"]) for slot in mapping["slots"]),
            "unique_mapped_wav_hashes": len(unique),
            "unique_mapped_pcm_hashes": len({r["pcm_sha256"] for r in measurements}),
            "delta_candidates": len(candidates),
            "candidate_unique_wav_hashes": len({r["sha256"] for r in candidates}),
            "candidate_unique_pcm_hashes": len(set(candidates_pcm)),
            "candidate_identity_evidence": dict(Counter(r["evidence"] for r in candidates)),
            "slots": summaries, "measurements": measurements,
            "audio_modified": False, "gameplay_validated": False,
        }
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
        LOG.info("Wrote %s", args.out)
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, wave.Error, subprocess.SubprocessError):
        LOG.exception("RC voice audit failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
