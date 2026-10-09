#!/usr/bin/env python3
"""Prepare isolated profile exports and a concrete, unpublished r24 release plan."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "dist/production-r24-2026-10-09"
OUT = BASE / "release-inputs-v2"
CANDIDATE = BASE / "candidate-v1/manifest.json"
SOURCE_MATRIX = ROOT / "dist/production-r23-2026-10-08/inputs-v2/production96.json"
SOURCE_DIRS = ROOT / "dist/production-r23-2026-10-08/inputs-v2/profile-directories.json"
BASELINE = ROOT / "dist/empire-launch-crash-2026-10-08/loop-2026-10-09/fixed-public-r23-minimal-v1/manifest.json"
PADDED = "25b90ccff893f69b3d59ebc9057f040c4cccaad54ea44c1822dbaf94d2a7c11b"
ORIGINAL = "7db519ad43cfd67b3d75c853afa63c82ff8c17fe82fc9b04bc0dd7479cbf4204"


def require(ok: Any, message: str) -> None:
    if not ok:
        raise ValueError(message)


def read(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def pin(path: Path) -> dict[str, Any]:
    raw = path.read_bytes()
    return {"path": str(path.resolve()), "bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}


def save(path: Path, value: Any) -> None:
    require(not path.exists(), "Fresh output required")
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def main() -> int:
    require(not OUT.exists(), "Fresh release input directory required")
    require(pin(CANDIDATE)["sha256"] == "52ce94fc76141535aa094c46de69f447eb5d04623787426d3c9f06c4f874da9c", "Frozen r24 candidate")
    require(pin(BASELINE)["sha256"] == "af51fa33b59141c2db938e2a67ee255c859e86e92beb444ecdb894d39176d380", "Intentional repaired baseline")
    matrix = read(SOURCE_MATRIX)
    require(matrix["passed"] and matrix["requestedStates"] == 96, "Retained complete actual-selector matrix")
    directories = read(SOURCE_DIRS)
    padded = BASE / "candidate-v1/assets" / PADDED
    require(pin(padded)["sha256"] == PADDED and padded.stat().st_size == 256, "Exact padded native audio archive")
    OUT.mkdir(parents=True)
    profiles, results = [], []
    for profile in directories:
        profile_id, old_folder = profile["id"], Path(profile["directory"])
        sets = {state["fileSet"] for state in matrix["selections"] if state["profile"] == profile_id and state["mode"] == "Clonedivers" and state["effectiveOptions"].get("droids") is True and state["effectiveOptions"].get("covenant") is False}
        require(len(sets) == 1, "Unique retained default Clone/Commando profile export")
        files = matrix["fileSets"][sets.pop()]
        require({path.name for path in old_folder.iterdir()} == {row["name"] for row in files}, "Exact retained source membership")
        matches = [row for row in files if row["sha256"] == ORIGINAL]
        require(len(matches) == 1 and matches[0]["size"] == 204, "Exactly one Constitution MAIN in each profile")
        folder = OUT / "profiles" / profile_id
        folder.mkdir(parents=True)
        links, copies = 0, 0
        for row in files:
            source = old_folder / row["name"]
            require(source.is_file() and source.stat().st_size == row["size"], "Retained source metadata identity")
            target = folder / row["name"]
            if row["sha256"] == ORIGINAL:
                shutil.copyfile(padded, target)
                require(pin(target)["sha256"] == PADDED, "Only new source asset copied")
                copies += 1
            else:
                try:
                    os.link(source, target)
                    links += 1
                except OSError:
                    shutil.copyfile(source, target)
                    copies += 1
        profiles.append({"id": profile_id, "directory": str(folder.resolve())})
        results.append({"profile": profile_id, "files": len(files), "hardLinks": links, "copies": copies, "changedPhysicalName": matches[0]["name"], "beforeSha256": ORIGINAL, "afterSha256": PADDED, "sourceDirectory": str(old_folder), "allOtherRowsRetained": True, "fullFreshHashDeferredToReleaseStage": True})
    save(OUT / "profile-directories.json", profiles)
    notes = OUT / "notes.md"
    notes.write_text("Clonedivers / EmpireDivers pack 2026.10.09-r24\n\nCorrects the Constitution audio archive alignment and Star Destroyer archive resource ordering that caused startup failures. Full Star Destroyers, Imperial armor, shared audio and existing runtime features are retained. Eagle and LEGO Yoda contexts match the new pack version.\n\nLauncher 1.7.3 is unchanged. Update the pack to adopt these repairs; subsequent Check/Repair preserves the corrected assets. In-mission audio, vehicles, physics and LEGO death playback still need gameplay testing.\n", encoding="utf-8")
    plan = {"passed": True, "tool": pin(Path(__file__)), "candidate": pin(CANDIDATE), "baseline": pin(BASELINE), "profileDirectories": pin(OUT / "profile-directories.json"), "sourceDirectories": pin(SOURCE_DIRS), "sourceActualSelectorMatrix": pin(SOURCE_MATRIX), "profiles": results, "notes": pin(notes), "releaseWorktree": "C:/Users/goode/.codex/worktrees/empire-yoda-r23/clonedivers", "releaseBranch": "codex/empire-yoda-r23", "packVersion": "2026.10.09-r24", "releaseTag": "pack-2026.10.09-r24-files", "app": {"path": str(ROOT / "dist/local-current/Clonedivers.exe"), "sha256": "2effe777eb2673d7237b5fc9aed1cbe85434ba86f3c31fbae9f73129a06cd51e", "version": "1.7.3", "unchanged": True}, "publishPrerequisites": ["Parent verifies actual r24 full Empire startup/solo ship and unchanged receipt with no new crash report; context fixture peer passed", "Commit only reviewed authoring fixes, new archive preflight, release authoring tools and release notes in the current release worktree", "Run coordinated tools/release.ps1 Stage against a fresh release-ready directory with this candidate, repaired baseline, exact app, four assets and these profile directories", "Stage reruns native tests and hashes every profile export; independently inspect three generated feeds and exact four new assets", "Use coordinated release.ps1 Publish only after actual acceptance and immutable source/release review"], "stageArguments": {"Action": "Stage", "Directory": str(BASE / "release-ready-v1"), "CandidateManifest": str(CANDIDATE), "BaselineManifest": str(BASELINE), "AppExe": str(ROOT / "dist/local-current/Clonedivers.exe"), "AssetDirectory": str(BASE / "candidate-v1/assets"), "NotesPath": str(notes), "Dotnet": str(ROOT / "dist/dotnet-sdk/dotnet.exe"), "ProfileDirectories": str(OUT / "profile-directories.json")}, "publication": {"script": "tools/release.ps1", "sourceCommitBeforeStage": True, "normalPushOnly": True, "tag": "pack-2026.10.09-r24-files", "newAssetCount": 4, "oldAssetsPreserved": True, "feeds": ["manifest.json", "manifest-v3.json", "manifest-v3-current.json"], "legacyPackJsonUnchanged": True, "readback": "Fetch the four actual hosted payloads and three public feeds, verify hashes and source/feed commit ancestry before local public-feed update"}, "localAdoption": {"requiredAction": "Refresh the public current feed, Update pack r23 to r24, and save a production r24 receipt using hosted GitHub URLs", "sameVersionRepairWouldRestoreOldReceipt": True, "afterUpdate": ["Run actual Check/Repair and prove r24 receipt keeps corrected429/453 plus rebound contexts", "Switch Clone then Empire using the public r24 manifest, checking exact selectors and receipt, then verify Empire startup again", "Preserve saved options/profile, default manifest feed, camera preference, native DLL and desktop shortcut"]}, "hostWrites": 0, "published": False, "r24RuntimeAcceptancePending": True}
    save(OUT / "report.json", plan)
    print(json.dumps({"report": pin(OUT / "report.json"), "profileDirectories": pin(OUT / "profile-directories.json"), "notes": pin(notes), "profiles": results}))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"r24 release inputs failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
