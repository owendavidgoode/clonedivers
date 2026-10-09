#!/usr/bin/env python3
"""Bounded independent v3-to-v4 Yoda runtime context-only delta review."""
from __future__ import annotations
import hashlib
import json
from pathlib import Path
import re
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
LANE = ROOT / "dist/empire-yoda-death-2026-10-08/runtime"
MODULE = re.compile(r'^package\.preload\[("(?:[^"\\]|\\.)*")\]=function\(\)\s*return assert\(loadstring\(("(?:[^"\\]|\\.)*"),("(?:[^"\\]|\\.)*")\)\)\(\)\s*end$', re.MULTILINE)


def require(value: Any, message: str) -> None:
    if not value:
        raise ValueError(message)


def pin(path: Path) -> dict[str, Any]:
    with path.open("rb") as handle:
        digest = hashlib.file_digest(handle, "sha256").hexdigest()
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": digest}


def run() -> dict[str, Any]:
    out = ROOT / "dist/production-r23-2026-10-08/qa/context-delta-peer-v1"
    require(not out.exists(), "Fresh output required")
    paths = [LANE / f"candidate-v{n}/report.json" for n in (3, 4)]
    expected = ["bb8f255af5294903fe07c042658b8df566715d39b7aba5b67d70025cc2acb015", "204e10bfbdca5085b7518d051fc6b0242c8c07e348bc9a32f11861506f355cf3"]
    reports = []
    for path, checksum in zip(paths, expected, strict=True):
        require(pin(path)["sha256"] == checksum, "Frozen report pin")
        reports.append(json.loads(path.read_text(encoding="utf-8")))
    bodies, modules = [], []
    for report in reports:
        addon = report["addon"]
        path = Path(addon["path"])
        require(pin(path) == addon, "Fresh addon identity")
        body, found = path.read_text(encoding="utf-8"), {}
        for match in MODULE.finditer(body):
            name, source, chunk = map(json.loads, match.groups())
            require(name == chunk and name not in found, "Unique embedded module")
            found[name] = source, match.group()
        require(len(found) >= 8, "Complete embedded module set")
        bodies.append(body)
        modules.append(found)
    require(modules[0].keys() == modules[1].keys(), "Module topology identical")
    changed = [name for name in modules[0] if modules[0][name][0] != modules[1][name][0]]
    require(len(changed) == 1 and changed[0].endswith("/context"), "Only context module changes")
    name = changed[0]
    require(bodies[1].replace(modules[1][name][1], modules[0][name][1]) == bodies[0], "All addon outer source bytes unchanged")
    old, new = modules[0][name][0], modules[1][name][0]
    before = "    return ffi.string(data,36)"
    after = ("    -- LastAccessTime (DWORDs 3/4) can advance from ordinary game/media reads.\n"
             "    -- Keep attributes, creation time, last-write time and size only.\n"
             "    return ffi.string(data,12)..ffi.string(data+5,16)")
    require(old.count(before) == 1 and new.count(after) == 1 and new.replace(after, before) == old, "Only mutable LastAccessTime bytes excluded from cached Win32 stamp")
    require(reports[0]["audioMain"] == reports[1]["audioMain"] and reports[0]["event"] == reports[1]["event"] and reports[0]["visualClosureRows"] == reports[1]["visualClosureRows"] and reports[0]["contextSpec"] == reports[1]["contextSpec"], "Event/audio/visual/version contracts unchanged")
    result = reports[1]["contextFixtures"]["result"]
    require(result["passed"] and all(r["passed"] for r in result["checks"]), "All current native context fixtures pass")
    out.mkdir(parents=True)
    report = {"passed": True, "tool": pin(Path(__file__)), "oldRuntime": pin(paths[0]), "newRuntime": pin(paths[1]), "modules": len(modules[0]), "changedModule": name,
              "allEventPolicyIdentityWorldSoundAndNativeModulesExact": True, "outerAddonExact": True, "onlyLastAccessTimeExcludedFromMetadataStamp": True,
              "creationWriteSizeAndAttributesRemainGuarded": True, "fixtureCount": len(result["checks"]), "allCurrentContextFixturesPass": True,
              "audioAndVisualClosureExact": True, "gameSettingsOrDeploymentChanged": False, "runtimeAccepted": False}
    (out / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return {"passed": True, "report": pin(out / "report.json")}


if __name__ == "__main__":
    print(json.dumps(run()))
