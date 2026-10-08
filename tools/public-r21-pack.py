#!/usr/bin/env python3
"""Promote the sealed deep-QA pack to immutable public release inputs.

Offline only. Reuses the current public launcher and hosted asset identities,
preserves every selector gate, and permits only reviewed portable Lua changes.
"""
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
import sys
from typing import Any
from urllib.parse import urlsplit
from xml.sax.saxutils import escape

LOG = logging.getLogger(__name__)
VERSION = "2026.10.08-r21"
PREFIX = "https://github.com/owendavidgoode/clonedivers/releases/download/"
SOURCE_SHA = "4c29b2ca52dc83af8ca3918c8c8a50f5ca2656146b40f3fa3e041bf915fffc8d"
APP_SHA = "2effe777eb2673d7237b5fc9aed1cbe85434ba86f3c31fbae9f73129a06cd51e"
PUBLIC_PINS = {
    "manifest.json": "922c5b33d91aa2dd5e5d7ec8b3f24cfd9c82b90e4f3b40444e16e0ee3c140bba",
    "manifest-v3.json": "742ff67226d65f22cd805926ad646d4a378afff6acc3de428d98b7597a570458",
    "manifest-v3-current.json": "383f6cfd04be47639d4921ee974782e49e2b432a3ace89c26014195b3389d635",
}
EMPTY = hashlib.sha256(b"").hexdigest()
PATCH = re.compile(r"[a-f0-9]{16}\.patch_\d+(?:\.stream|\.gpu_resources)?", re.I)
FIELDS = ("name", "size", "sha256", "option", "unlessOption", "modes", "textureProfiles")


def require(ok: Any, message: str) -> None:
    if not ok:
        raise ValueError(message)


def read(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(path: Path, value: Any) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def pin(path: Path) -> dict[str, Any]:
    before = path.stat()
    require(path.is_file() and not path.is_symlink(), "Regular immutable source required: " + str(path))
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    after = path.stat()
    require((before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns), "File changed: " + str(path))
    return {"path": str(path.resolve()), "bytes": after.st_size, "sha256": digest}


def link(source: Path, target: Path, row: dict[str, Any]) -> dict[str, Any]:
    before = pin(source)
    require(before["sha256"] == row["sha256"] and before["bytes"] == row["size"], "Wrong source: " + str(source))
    require(not target.exists(), "Fresh target required")
    try:
        os.link(source, target)
        method = "hardlink"
    except OSError:
        shutil.copyfile(source, target)
        method = "copy"
    after = pin(target)
    require((after["sha256"], after["bytes"]) == (before["sha256"], before["bytes"]), "Staged identity changed")
    return {"source": before, "destination": after, "method": method}


def source_paths(workspace: Path) -> dict[str, Path]:
    audit = read(workspace / "dist/empire-deep-qa-2026-10-08/qa/all-assets-v1.json")
    require(audit["passed"] and audit["manifest"]["sha256"] == SOURCE_SHA, "Whole-pack QA source pin")
    return {item["sha256"]: Path(item["path"]) for item in audit["assets"]}


def native_project(workspace: Path, checkout: Path, out: Path) -> None:
    folder = out / "native-selector"
    folder.mkdir()
    shutil.copyfile(workspace / "tools/PublicR21.Select/Check.cs", folder / "Check.cs")
    includes = escape(str(checkout.resolve() / "Clonedivers/*.cs"), {'"': "&quot;"})
    (folder / "NativeSelector.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms>'
        '<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
        '<StartupObject>EmpireHolisticCheck</StartupObject></PropertyGroup>'
        f'<ItemGroup><Compile Include="Check.cs" /><Compile Include="{includes}" Link="%(Filename)%(Extension)" />'
        '</ItemGroup></Project>\n', encoding="utf-8")
    (folder / "NuGet.Config").write_text('<configuration><packageSources><clear /></packageSources></configuration>\n', encoding="utf-8")
    write(folder / "source-pins.json", {"checkout": str(checkout), "exporter": pin(folder / "Check.cs"),
                                       "sources": [pin(path) for path in sorted((checkout / "Clonedivers").glob("*.cs"))]})


def prepare(args: argparse.Namespace) -> None:
    workspace, checkout, out = args.workspace.resolve(), args.checkout.resolve(), args.out.resolve()
    require(out.is_relative_to(workspace / "dist/production-r21-2026-10-08") and not out.exists(), "Fresh scoped output")
    source_path = workspace / "dist/empire-deep-qa-2026-10-08/integration/candidate-v1/manifest.json"
    source_pin = pin(source_path)
    require(source_pin["sha256"] == SOURCE_SHA, "Sealed deep-QA source changed")
    source = read(source_path)
    require(source["format"] == 3 and len(source["pack"]["files"]) == 1794 and source["pack"]["combinedRoster"], "Whole combined pack required")
    paths = source_paths(workspace)
    public: dict[str, list[dict[str, Any]]] = {}
    witnesses = []
    for name, digest in PUBLIC_PINS.items():
        witness = pin(checkout / name)
        require(witness["sha256"] == digest, "Public baseline changed: " + name)
        witnesses.append(witness)
        feed = read(checkout / name)
        require(feed["app"]["version"] == "1.7.3" and feed["app"]["sha256"] == APP_SHA, "Preserve public launcher1.7.3")
        for row in feed["pack"]["files"]:
            if row["size"]:
                require(row["url"].startswith(PREFIX), "Public baseline URL invalid")
                public.setdefault(row["sha256"], []).append(row)
    runtime_path = workspace / "dist/production-r21-2026-10-08/runtime-v1/report.json"
    runtime = read(runtime_path)
    require(runtime["passed"] and len(runtime["sources"]) == 2, "Reviewed portable runtime required")
    replacements = {item["oldSha256"]: item for item in runtime["sources"]}
    require(len(replacements) == 2, "Two unique runtime providers only")
    candidate = copy.deepcopy(source)
    changed, uploads, replacement_rows = [], {}, []
    for index, (row, original) in enumerate(zip(candidate["pack"]["files"], source["pack"]["files"], strict=True)):
        require(PATCH.fullmatch(row["name"]), "Invalid logical patch name")
        if row["sha256"] in replacements:
            replacement = replacements[row["sha256"]]
            require(row["size"] == replacement["oldSize"], "Old runtime size pin")
            row["sha256"], row["size"] = replacement["newSha256"], replacement["newSize"]
            paths[row["sha256"]] = Path(replacement["path"])
            replacement_rows.append({"index": index, "before": original, "after": copy.deepcopy(row), "label": replacement["label"]})
        if not row["size"]:
            require(row["sha256"] == EMPTY and not row["url"], "Empty companion identity differs")
            continue
        if row["sha256"] in public:
            known = public[row["sha256"]]
            require(all(item["size"] == row["size"] for item in known), "Existing public size conflict")
            urls = {item["url"] for item in known}
            row["url"] = original["url"] if original["url"] in urls else sorted(urls)[0]
        else:
            row["url"] = PREFIX + "pack-" + VERSION + "-files/" + row["sha256"]
            uploads.setdefault(row["sha256"], row)
        parsed = urlsplit(row["url"])
        require(parsed.scheme == "https" and parsed.hostname == "github.com" and not parsed.query and not parsed.fragment,
                "Only immutable public release URLs")
        if row != original:
            changed.append({"index": index, "name": row["name"], "oldSha256": original["sha256"], "newSha256": row["sha256"],
                            "oldUrl": original["url"], "newUrl": row["url"]})
    require(len(replacement_rows) == 2, "Both runtime providers replaced exactly once")
    candidate["app"] = read(checkout / "manifest-v3-current.json")["app"]
    candidate["pack"].update(version=VERSION, isPublished=True,
        totalSize=sum(row["size"] for row in candidate["pack"]["files"]),
        notes="Expanded Imperial armor, film-scale AT-ST, aircraft, weapons and voices; deep-QA audio and visibility repairs.",
        statusNotes="Experimental Empire update. Star Destroyer visibility, mission physics, armor damage pieces and audio mix still require gameplay testing. Launcher1.7.3 camera procedure retained.")
    out.mkdir(parents=True)
    (out / "assets").mkdir()
    copies = [link(paths[digest], out / "assets" / digest, row) for digest, row in sorted(uploads.items())]
    write(out / "manifest.json", candidate)
    shutil.copyfile(source_path, out / "tested-baseline.json")
    native_project(workspace, checkout, out)
    write(out / "source-promotion-report.json", {
        "passed": True, "source": source_pin, "manifest": pin(out / "manifest.json"), "runtime": pin(runtime_path),
        "publicFeedPins": witnesses, "rows": 1794, "replacementRows": replacement_rows, "changedRows": changed,
        "newUniqueHashes": len(uploads), "newAssetBytes": sum(row["size"] for row in uploads.values()),
        "assets": copies, "allOtherPayloadHashesAndSizesExact": True, "allRowsGatesAndOrderExact": True,
        "launcher1_7_3Preserved": True, "gameOrSettingsChanged": False, "published": False})
    LOG.warning("Prepared %s: %d new assets / %d bytes", VERSION, len(uploads), sum(row["size"] for row in uploads.values()))


def identity(row: dict[str, Any]) -> dict[str, Any]:
    return {key: row.get(key) for key in FIELDS}


def state_key(row: dict[str, Any]) -> str:
    return json.dumps({key: row[key] for key in ("profile", "mode", "requestedOptions")}, sort_keys=True)


def profiles(args: argparse.Namespace) -> None:
    workspace, out = args.workspace.resolve(), args.out.resolve()
    require(out.is_relative_to(workspace / "dist/production-r21-2026-10-08") and not (out / "profiles").exists(), "Fresh scoped profiles")
    report = read(out / "source-promotion-report.json")
    require(pin(out / "manifest.json") == report["manifest"], "Candidate changed")
    current, old = read(out / "production96.json"), read(workspace / "dist/empire-deep-qa-2026-10-08/integration/candidate-v1/production96.json")
    require(current["passed"] and current["manifestSha256"] == report["manifest"]["sha256"] and current["requestedStates"] == 96 and current["uniqueFileSets"] == 16, "Actual complete production selector required")
    require(old["passed"] and old["manifestSha256"] == SOURCE_SHA, "Original selector pin")
    replacements = {item["before"]["sha256"]: item["after"] for item in report["replacementRows"]}
    old_states = {state_key(row): row for row in old["selections"]}
    for state in current["selections"]:
        prior = old_states[state_key(state)]
        require(state["effectiveOptions"] == prior["effectiveOptions"], "Mode flags changed")
        expected = copy.deepcopy(old["fileSets"][prior["fileSet"]])
        for row in expected:
            if row["sha256"] in replacements:
                replacement = replacements[row["sha256"]]
                row["sha256"], row["size"] = replacement["sha256"], replacement["size"]
        require([identity(row) for row in expected] == [identity(row) for row in current["fileSets"][state["fileSet"]]], "Physical payload/gates/order changed")
    paths = source_paths(workspace)
    for item in report["assets"]:
        paths[item["destination"]["sha256"]] = Path(item["destination"]["path"])
    records = []
    (out / "profiles").mkdir()
    for profile in ("full", "lighter"):
        states = [row for row in current["selections"] if row["profile"] == profile and row["mode"] == "CommandoDivers" and row["requestedOptions"] == {"droids": True, "covenant": False, "commandos": True, "aimpoints": True}]
        require(len(states) == 1, "Unique default profile state")
        folder = out / "profiles" / profile
        folder.mkdir()
        rows = current["fileSets"][states[0]["fileSet"]]
        for row in rows:
            target = folder / row["name"]
            if row["size"]:
                link(paths[row["sha256"]], target, row)
            else:
                require(row["sha256"] == EMPTY, "Empty profile identity")
                target.write_bytes(b"")
        records.append({"id": profile, "directory": str(folder.resolve())})
        LOG.warning("Verified %s profile: %d files", profile, len(rows))
    write(out / "profile-directories.json", records)
    write(out / "profile-sources-report.json", {"passed": True, "all96PhysicalSelectionsEquivalentExceptTwoReviewedLua": True,
        "candidate": pin(out / "manifest.json"), "selector": pin(out / "production96.json"), "directories": records,
        "testedBaseline": pin(out / "tested-baseline.json"), "everyProfileSourceAndDestinationHashed": True,
        "gameOrSettingsChanged": False})


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("command", choices=("prepare", "profiles"))
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--checkout", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    logging.basicConfig(level=logging.WARNING, format="%(levelname)s: %(message)s")
    try:
        (prepare if args.command == "prepare" else profiles)(args)
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, TypeError) as error:
        LOG.error("%s", error)
        return 1


if __name__ == "__main__":
    sys.exit(main())
