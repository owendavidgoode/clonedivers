#!/usr/bin/env python3
"""Verify and extract a freshly pinned primary runtime source snapshot."""
from __future__ import annotations

import argparse
from collections.abc import Sequence
import hashlib
import json
import logging
from pathlib import Path, PurePosixPath
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "dist/empire-yoda-death-2026-10-08/runtime/primary-v1"
COMMIT = "41184521ce6021bf15a294d2debdee3da30d931a"


def pin(path: Path) -> dict[str, object]:
    data = path.read_bytes()
    return {"path": str(path.resolve()), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def run() -> dict[str, object]:
    target = OUT / "source"
    if target.exists():
        raise ValueError("Fresh verified source directory required")
    reference = json.loads((OUT / "ref.json").read_text(encoding="utf-8-sig"))
    tree = json.loads((OUT / "tree.json").read_text(encoding="utf-8-sig"))
    if reference["object"]["sha"] != COMMIT or tree["sha"] != COMMIT or tree["truncated"]:
        raise ValueError("Exact primary commit and complete tree required")
    expected = {item["path"]: item for item in tree["tree"] if item["type"] == "blob"}
    prefix = "HD2Runtime-" + COMMIT + "/"
    verified = {}
    with zipfile.ZipFile(OUT / "source.zip") as archive:
        for item in archive.infolist():
            if item.is_dir():
                continue
            if not item.filename.startswith(prefix):
                raise ValueError("Unexpected archive root")
            name = item.filename.removeprefix(prefix)
            path = PurePosixPath(name)
            if path.is_absolute() or ".." in path.parts or name not in expected or item.file_size > 50_000_000:
                raise ValueError("Unexpected source entry: " + name)
            data = archive.read(item)
            digest = hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()
            if digest != expected[name]["sha"] or len(data) != expected[name]["size"]:
                raise ValueError("Primary Git blob mismatch: " + name)
            if name in verified:
                raise ValueError("Duplicate source entry")
            verified[name] = data
    if set(verified) != set(expected):
        raise ValueError("Archive does not cover every primary blob")
    for name, data in verified.items():
        destination = target / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(data)
    result = {"passed": True, "commit": COMMIT, "tool": pin(Path(__file__)),
              "reference": pin(OUT / "ref.json"), "tree": pin(OUT / "tree.json"),
              "archive": pin(OUT / "source.zip"), "verifiedGitBlobCount": len(verified),
              "version": (target / "VERSION").read_text().strip(), "sourceCodeExecuted": False}
    report = OUT / "report.json"
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return pin(report)


def main(argv: Sequence[str] | None = None) -> int:
    argparse.ArgumentParser(description=__doc__).parse_args(argv)
    logging.basicConfig(level=logging.INFO)
    try:
        print(json.dumps(run()))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception("Primary source verification failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
