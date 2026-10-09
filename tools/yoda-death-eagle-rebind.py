#!/usr/bin/env python3
"""Rebind the reviewed Eagle context to r23 without changing native logic."""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import logging
from pathlib import Path
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "dist/production-r23-2026-10-08/eagle-v1"
VERSION = "2026.10.08-r23"
SOURCE_SHA = "e7810a0dc435d9cef588813a6c8b9d6a648ff9584d2130117f281449b8aac108"
ARCHIVE_SHA = "18c3f428677e0cb31b86959dfa5db27fc406abb2d602f7a93eaafc1c13b721d2"


def load_module(name: str, path: Path) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ImportError(str(path))
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def run() -> None:
    api = load_module("yoda_eagle_previous", ROOT / "tools/public-r22-runtime.py")
    source_report = ROOT / "dist/production-r22-2026-10-08/runtime-v1/report.json"
    api.require(api.pin(source_report)["sha256"] == SOURCE_SHA, "Frozen r22 runtime input")
    source = api.load(source_report)["sources"][0]
    archive_path = Path(source["path"])
    api.require(api.pin(archive_path)["sha256"] == ARCHIVE_SHA, "Frozen r22 Eagle input")
    row, raw = api.unpack(archive_path.read_bytes())
    original = raw.decode("utf-8")
    modules = api.modules(original)
    name = "codex_empire_vehicles/context_spec"
    spec_source, match = modules[name]
    changed = api.replace_once(spec_source, '"2026.10.08-r22"', json.dumps(VERSION))
    statement = "package.preload[" + json.dumps(name) + "]=function()return assert(loadstring(" + json.dumps(changed, ensure_ascii=False) + "," + json.dumps(name) + "))()end"
    body = api.replace_once(original, match.group(0), statement)
    api.require(api.replace_once(body, statement, match.group(0)) == original, "Only version changed")
    after = api.modules(body)
    api.require(after.keys() == modules.keys() and all(after[k][0] == v[0] for k, v in modules.items() if k != name), "All other modules byte exact")
    writer = load_module("yoda_eagle_writer", api.SDK_ROOT / "sdk/tools/hd2_archive.py")
    runner = load_module("yoda_eagle_runner", api.SDK_ROOT / "sdk/tools/lua_runner.py")
    api.require(not OUT.exists(), "Fresh output required")
    OUT.mkdir(parents=True)
    folder = OUT / "eagle"
    folder.mkdir()
    addon = folder / "addon.lua"
    addon.write_text(body, encoding="utf-8")
    api.require(runner.execute(("assert(loadstring(" + json.dumps(body, ensure_ascii=False) + "));return 'PASS'").encode()) == b"PASS", "Whole addon syntax")
    archive = writer.make_archive({row[0]: writer.lua_resource(body.encode())})
    restored, restored_body = api.unpack(archive)
    api.require(restored[:2] == row[:2] and restored_body == body.encode(), "Typed archive roundtrip")
    files = []
    for suffix, data in zip(api.SUFFIXES, (archive, b"", b""), strict=True):
        target = folder / ("9ba626afa44a3aa3.patch_0" + suffix)
        target.write_bytes(data)
        files.append(api.pin(target))
    derived = {"label": "eagle", "typedKey": "6d133274694b50a9.a14e8dfa2cd117e2", "path": files[0]["path"], "files": files, "addon": api.pin(addon), "oldArchive": api.pin(archive_path)}
    api.OUTPUT, api.VERSION = OUT, VERSION
    fixtures = api.fixtures(derived, runner)
    api.save(OUT / "report.json", {"passed": True, "tool": api.pin(Path(__file__)), "source": api.pin(source_report), "sources": [derived], "version": VERSION, "fixtures": fixtures, "allBytesOutsideVersionExact": True, "allNativeWriterGuardsExact": True, "observerUnchanged": True, "gameOrSettingsChanged": False, "runtimeAccepted": False})
    print(json.dumps(api.pin(OUT / "report.json")))


def main() -> int:
    argparse.ArgumentParser(description=__doc__).parse_args()
    logging.basicConfig(level=logging.INFO)
    try:
        run()
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, RuntimeError):
        logging.exception("Eagle version rebind failed")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
