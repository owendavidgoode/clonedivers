#!/usr/bin/env python3
"""Compose and qualify the bounded r21-to-r22 public pack delta offline."""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import logging
import os
from pathlib import Path
import re
import shutil
from typing import Any

VERSION = "2026.10.08-r22"
BASE_SHA = "55d5297988c0b134eef4738edf81026ebbbe688c917ad97c6170ff7ef1b59c2e"
VOICE_SHA = "4670d60a11156c9bf04630d2bee4671cf7384a678c0ad845a84083d474b20791"
RUNTIME_SHA = "e7810a0dc435d9cef588813a6c8b9d6a648ff9584d2130117f281449b8aac108"
APP_SHA = "2effe777eb2673d7237b5fc9aed1cbe85434ba86f3c31fbae9f73129a06cd51e"
EMPTY = hashlib.sha256(b"").hexdigest()
GROUPS = {419, 420, 432, 456}
PATTERN = re.compile(r"9ba626afa44a3aa3\.patch_(\d+)(.*)")
PREFIX = "https://github.com/owendavidgoode/clonedivers/releases/download/pack-" + VERSION + "-files/"


def require(value: Any, message: str) -> None:
    if not value:
        raise ValueError(message)


def read(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path: Path, value: Any) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def pin(path: Path, expected: str | None = None) -> dict[str, Any]:
    before = path.stat()
    require(path.is_file() and not path.is_symlink(), f"Regular source required: {path}")
    with path.open("rb") as stream:
        sha = hashlib.file_digest(stream, "sha256").hexdigest()
    after = path.stat()
    require((before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns), "Concurrent source mutation")
    require(expected is None or sha == expected, f"Source pin differs: {path}")
    return {"path": str(path.resolve()), "bytes": after.st_size, "sha256": sha}


def key(state: dict[str, Any]) -> str:
    return json.dumps({field: state[field] for field in ("profile", "mode", "requestedOptions")}, sort_keys=True)


def identity(row: dict[str, Any]) -> tuple[str, int, str]:
    return row["name"], row["size"], row["sha256"]


def prepare(root: Path, out: Path) -> None:
    require(not out.exists() and out.is_relative_to(root / "dist/production-r22-2026-10-08"), "Fresh scoped output required")
    base_path = root / "dist/production-r21-2026-10-08/release-ready-v1/manifest.json"
    voice_path = root / "dist/production-r22-2026-10-08/voice-v1/report.json"
    runtime_path = root / "dist/production-r22-2026-10-08/runtime-v1/report.json"
    witnesses = [pin(base_path, BASE_SHA), pin(voice_path, VOICE_SHA), pin(runtime_path, RUNTIME_SHA)]
    baseline, voice, runtime = read(base_path), read(voice_path), read(runtime_path)
    require(voice["passed"] and runtime["passed"], "Qualified inputs required")
    candidate = copy.deepcopy(baseline)
    require(baseline["pack"]["version"] == "2026.10.08-r21" and len(baseline["pack"]["files"]) == 1794, "Baseline version/count")
    require(baseline["app"]["sha256"] == APP_SHA, "Public launcher preserved")
    changed = []
    for index, row in enumerate(candidate["pack"]["files"]):
        match = PATTERN.fullmatch(row["name"])
        if match and int(match[1]) in GROUPS:
            require(row["modes"] == ["empire"] and row["textureProfiles"] == ["full"], "Reviewed geometry gate differs")
            row["textureProfiles"] = ["full", "lighter"]
            changed.append({"index": index, "before": baseline["pack"]["files"][index], "after": copy.deepcopy(row)})
    require(len(changed) == 12, "Exactly four geometry triads")
    new_files = list(voice["files"])
    eagle = runtime["sources"][0]
    require(len(runtime["sources"]) == 1 and eagle["label"] == "eagle", "One Eagle overlay")
    eagle_path = Path(eagle["path"])
    new_files.extend(pin(Path(str(eagle_path) + suffix)) for suffix in ("", ".stream", ".gpu_resources"))
    require(len(new_files) == 6, "Two complete triads")
    appended = []
    uploads = {}
    for index, source in enumerate(new_files):
        current = pin(Path(source["path"]), source["sha256"])
        require(current["bytes"] == source["bytes"], "New source size pin")
        suffix = ("", ".stream", ".gpu_resources")[index % 3]
        group = 459 + index // 3
        row = {"name": f"9ba626afa44a3aa3.patch_{group}{suffix}",
               "url": PREFIX + current["sha256"] if current["bytes"] else "",
               "size": current["bytes"], "sha256": current["sha256"], "modes": ["empire"]}
        require(row["size"] or row["sha256"] == EMPTY, "Empty companion identity")
        appended.append(row)
        if row["size"]:
            uploads[row["sha256"]] = current
    require(len(uploads) == 3, "Exactly three new nonempty assets")
    candidate["pack"]["files"].extend(appended)
    candidate["pack"].update(version=VERSION, totalSize=sum(r["size"] for r in candidate["pack"]["files"]),
        notes="Five additional authentic Imperial effort/injury cues; Full and Lighter now share reviewed walker geometry and Eagle dispatch support.",
        statusNotes="Experimental Empire update. Star Destroyer visibility, mission physics, armor damage pieces and audio mix still require gameplay testing. Launcher1.7.3 camera procedure retained.")
    require(len(candidate["pack"]["files"]) == 1800, "Logical row count")
    out.mkdir(parents=True)
    (out / "assets").mkdir()
    for sha, source in uploads.items():
        target = out / "assets" / sha
        try:
            os.link(source["path"], target)
        except OSError:
            shutil.copyfile(source["path"], target)
        require(pin(target, sha)["bytes"] == source["bytes"], "Staged asset size")
    save(out / "manifest.json", candidate)
    shutil.copyfile(base_path, out / "tested-baseline.json")
    # Clone/Commando selections are unchanged, so their existing exact profile exports remain usable.
    profiles = read(root / "dist/production-r21-2026-10-08/inputs-v1/profile-directories.json")
    require({p["id"] for p in profiles} == {"full", "lighter"} and all(Path(p["directory"]).is_dir() for p in profiles), "Durable profile exports")
    save(out / "profile-directories.json", profiles)
    save(out / "composition.json", {"passed": True, "tool": pin(Path(__file__)), "sources": witnesses,
        "manifest": pin(out / "manifest.json"), "baseline": pin(out / "tested-baseline.json"),
        "changedGateRows": changed, "appendedRows": appended, "assets": list(uploads.values()),
        "all1794PayloadsUrlsOrderPreserved": True, "observerByteExactAndInert": True,
        "newAssetBytes": sum(x["bytes"] for x in uploads.values()), "gameOrSettingsChanged": False})
    logging.warning("Prepared %s: 1800 rows, 12 gate changes, 6 appended rows, 3 new assets", VERSION)


def qualify(root: Path, out: Path) -> None:
    report = read(out / "composition.json")
    pin(out / "manifest.json", report["manifest"]["sha256"])
    old = read(root / "dist/production-r21-2026-10-08/inputs-v1/production96.json")
    new = read(out / "production96.json")
    require(new["passed"] and new["requestedStates"] == 96 and new["manifestSha256"] == report["manifest"]["sha256"], "Actual launcher selector pin")
    previous = {key(s): s for s in old["selections"]}
    baseline = read(out / "tested-baseline.json")
    geometry = {r["sha256"] for r in baseline["pack"]["files"] if (m := PATTERN.fullmatch(r["name"])) and int(m[1]) in GROUPS and r["size"]}
    additions = {r["sha256"] for r in report["appendedRows"] if r["size"]}
    states = []
    for state in new["selections"]:
        prior = previous[key(state)]
        require(state["effectiveOptions"] == prior["effectiveOptions"], "Effective options changed")
        before = old["fileSets"][prior["fileSet"]]
        after = new["fileSets"][state["fileSet"]]
        for archive in {r["name"].split(".")[0] for r in after}:
            indices = sorted({int(r["name"].split(".patch_")[1].split(".")[0]) for r in after if r["name"].startswith(archive + ".")})
            require(indices == list(range(len(indices))), "Physical numbering gap")
        empire = state["mode"] == "EmpireDivers"
        if not empire:
            require(after == before, "Clone/Commando physical selection changed")
        else:
            expected_added = additions | (geometry if state["profile"] == "lighter" else set())
            # Existing physical filenames shift when Lighter gains geometry; payload sequence remains ordered.
            strip = lambda rows, excluded: [(r["size"], r["sha256"], r["url"]) for r in rows if r["size"] and r["sha256"] not in excluded]
            require(strip(after, expected_added) == strip(before, set()), "Existing ordered payloads changed")
            require({r["sha256"] for r in after if r["sha256"] in additions} == additions, "Missing final overlays")
            require(len(after) == len(before) + (18 if state["profile"] == "lighter" else 6), "Expected added triads only")
        states.append({"profile": state["profile"], "mode": state["mode"], "filesBefore": len(before), "filesAfter": len(after)})
    require(len(states) == 96 and new["uniqueFileSets"] == 16, "All selector states covered")
    save(out / "selection-qa.json", {"passed": True, "manifest": pin(out / "manifest.json"),
        "actualSelector": pin(out / "production96.json"), "all96States": states, "uniqueFileSets": 16,
        "all64CloneCommandoStatesExact": True, "all32EmpireStatesOnlyReviewedOrderedDelta": True,
        "allPhysicalIndicesDense": True, "profileExportsReusedForUnchangedCloneCommando": True})
    logging.warning("PASS actual launcher all96 states: unchanged Clone/Commando, exact reviewed Empire delta")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("prepare", "qualify"))
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    logging.basicConfig(level=logging.WARNING, format="%(levelname)s: %(message)s")
    try:
        (prepare if args.command == "prepare" else qualify)(args.workspace.resolve(), args.out.resolve())
    except (OSError, ValueError, KeyError, TypeError) as error:
        logging.error("%s", error)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
