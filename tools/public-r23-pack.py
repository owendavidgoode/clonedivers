#!/usr/bin/env python3
"""Compose and verify the additive LEGO Yoda death update using real launcher selections."""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import logging
import os
from pathlib import Path
import shutil
from typing import Any

VERSION = "2026.10.08-r23"
BASE_SHA = "3a70933d5a1f7efb60627224a6befcdcdbe235c957032834101560b4383e0a03"
APP_SHA = "2effe777eb2673d7237b5fc9aed1cbe85434ba86f3c31fbae9f73129a06cd51e"
EMPTY = hashlib.sha256(b"").hexdigest()
SUFFIXES = ("", ".stream", ".gpu_resources")
PREFIX = f"https://github.com/owendavidgoode/clonedivers/releases/download/pack-{VERSION}-files/"


def require(value: Any, message: str) -> None:
    if not value:
        raise ValueError(message)


def read(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path: Path, value: Any) -> None:
    require(not path.exists(), f"Fresh output required: {path}")
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def pin(path: Path, expected: str | None = None) -> dict[str, Any]:
    require(path.is_file() and not path.is_symlink(), f"Regular file required: {path}")
    before = path.stat()
    with path.open("rb") as handle:
        digest = hashlib.file_digest(handle, "sha256").hexdigest()
    after = path.stat()
    require((before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns), "Concurrent mutation")
    require(expected is None or digest == expected, f"Source pin changed: {path}")
    return {"path": str(path.resolve()), "bytes": after.st_size, "sha256": digest}


def state_key(row: dict[str, Any]) -> str:
    return json.dumps({key: row[key] for key in ("profile", "mode", "requestedOptions")}, sort_keys=True)


def prepare(root: Path, out: Path, audio_path: Path, runtime_path: Path) -> None:
    require(not out.exists() and out.is_relative_to(root / "dist/production-r23-2026-10-08"), "Fresh scoped output required")
    baseline_path = root / "dist/production-r22-2026-10-08/release-ready-v1/manifest.json"
    baseline_pin = pin(baseline_path, BASE_SHA)
    baseline = read(baseline_path)
    require(baseline["pack"]["version"] == "2026.10.08-r22" and len(baseline["pack"]["files"]) == 1800, "Baseline version/count")
    require(baseline["app"]["sha256"] == APP_SHA, "Launcher identity preserved")
    eagle_path = root / "dist/production-r23-2026-10-08/eagle-v1/report.json"
    reports = [("audio", audio_path), ("runtime", runtime_path), ("eagle", eagle_path)]
    sources, additions, uploads = [], [], {}
    for offset, (label, path) in enumerate(reports):
        witness = pin(path)
        report = read(path)
        require(report.get("passed") is True, f"Unqualified {label}")
        files = report["files"] if label != "eagle" else report["sources"][0]["files"]
        require(len(files) == 3, f"Complete triad required: {label}")
        sources.append({"label": label, "report": witness})
        for suffix, source in zip(SUFFIXES, files, strict=True):
            current = pin(Path(source["path"]), source["sha256"])
            require(current["bytes"] == source["bytes"], f"Source size changed: {label}")
            row = {"name": f"9ba626afa44a3aa3.patch_{461 + offset}{suffix}", "url": PREFIX + current["sha256"] if current["bytes"] else "", "size": current["bytes"], "sha256": current["sha256"], "modes": ["empire"]}
            require(row["size"] or row["sha256"] == EMPTY, "Empty companion identity")
            additions.append(row)
            if row["size"]:
                uploads[row["sha256"]] = current
    candidate = copy.deepcopy(baseline)
    candidate["pack"]["files"].extend(additions)
    candidate["pack"].update(version=VERSION, totalSize=sum(row["size"] for row in candidate["pack"]["files"]), notes="Classic LEGO Star Wars Yoda death scream for the local player's LEGO Stormtrooper and LEGO Bikini Stormtrooper armor.", statusNotes="Experimental Empire update. LEGO death routing and sound playback require gameplay testing; existing visual, physics and audio playtest checklist remains applicable. Launcher1.7.3 retained.")
    require(candidate["pack"]["files"][:1800] == baseline["pack"]["files"], "Every prior manifest row preserved")
    out.mkdir(parents=True)
    (out / "assets").mkdir()
    for digest, source in uploads.items():
        target = out / "assets" / digest
        try:
            os.link(source["path"], target)
        except OSError:
            shutil.copyfile(source["path"], target)
        pin(target, digest)
    save(out / "manifest.json", candidate)
    shutil.copyfile(baseline_path, out / "tested-baseline.json")
    profiles = read(root / "dist/production-r22-2026-10-08/inputs-v1/profile-directories.json")
    require({row["id"] for row in profiles} == {"full", "lighter"} and all(Path(row["directory"]).is_dir() for row in profiles), "Retained Clone/Commando profile exports")
    save(out / "profile-directories.json", profiles)
    save(out / "composition.json", {"passed": True, "tool": pin(Path(__file__)), "baseline": baseline_pin, "sources": sources, "manifest": pin(out / "manifest.json"), "appendedRows": additions, "assets": list(uploads.values()), "all1800PriorRowsExact": True, "newAssetBytes": sum(row["bytes"] for row in uploads.values()), "gameOrSettingsChanged": False})
    logging.warning("Prepared %s:1809 logical rows,9 appended files,%d new nonempty assets", VERSION, len(uploads))


def qualify(root: Path, out: Path) -> None:
    composition = read(out / "composition.json")
    pin(out / "manifest.json", composition["manifest"]["sha256"])
    old = read(root / "dist/production-r22-2026-10-08/inputs-v1/production96.json")
    current = read(out / "production96.json")
    require(current["passed"] and current["requestedStates"] == 96 and current["manifestSha256"] == composition["manifest"]["sha256"], "Actual launcher selector proof")
    prior = {state_key(row): row for row in old["selections"]}
    added = {row["sha256"] for row in composition["appendedRows"] if row["size"]}
    states = []
    for state in current["selections"]:
        previous = prior[state_key(state)]
        require(state["effectiveOptions"] == previous["effectiveOptions"], "Effective options changed")
        before, after = old["fileSets"][previous["fileSet"]], current["fileSets"][state["fileSet"]]
        empire = state["mode"] == "EmpireDivers"
        require(after[:len(before)] == before, "Prior physical file selections changed")
        if empire:
            require(len(after) == len(before) + 9 and {row["sha256"] for row in after[len(before):] if row["size"]} == added, "Expected Empire additive triads")
        else:
            require(after == before, "Clone/Commando selection changed")
        for archive in {row["name"].split(".")[0] for row in after}:
            groups = sorted({int(row["name"].split(".patch_")[1].split(".")[0]) for row in after if row["name"].startswith(archive + ".")})
            require(groups == list(range(len(groups))), "Physical patch numbering gap")
        states.append({"profile": state["profile"], "mode": state["mode"], "before": len(before), "after": len(after)})
    require(len(states) == 96 and current["uniqueFileSets"] == 16, "Complete state coverage")
    save(out / "selection-qa.json", {"passed": True, "manifest": pin(out / "manifest.json"), "actualLauncher": pin(out / "production96.json"), "states": states, "all64CloneCommandoStatesExact": True, "all32EmpireStatesAdditiveOnly": True, "allPatchIndicesDense": True})
    logging.warning("PASS actual launcher all96 state selections")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("prepare", "qualify"))
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--audio-report", type=Path)
    parser.add_argument("--runtime-report", type=Path)
    args = parser.parse_args()
    logging.basicConfig(level=logging.WARNING)
    try:
        root, out = args.workspace.resolve(), args.out.resolve()
        if args.command == "prepare":
            require(args.audio_report and args.runtime_report, "Both input reports required")
            prepare(root, out, args.audio_report.resolve(), args.runtime_report.resolve())
        else:
            qualify(root, out)
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, TypeError):
        logging.exception("r23 composition failed")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
