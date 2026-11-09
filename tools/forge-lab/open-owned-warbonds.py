#!/usr/bin/env python3
"""Open snapshot-owned premium HellForge Warbonds while the installation is idle.

Dry-run by default. Requires Windows and the installed backend's sealed-account verifier.
"""

import argparse
import ctypes
from ctypes import wintypes
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from collections.abc import Iterator, Sequence
from typing import Any


FREE_WARBOND = 1929468580
BACKUPS = Path(r"D:\ClaudeWork\backups")
SNAPSHOT_FILES = ("snapshot.json", "real-inventory.json", "progression.json", "seal.json")


def checked(path: Path) -> Path:
    for part in (path, *path.parents):
        if part.is_symlink() or part.is_junction():
            raise ValueError(f"Refusing linked path: {part}")
    return path


def run_json(args: list[str], root: Path) -> dict[str, Any]:
    result = subprocess.run(args, cwd=root, capture_output=True, text=True,
                            encoding="utf-8", timeout=120, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise ValueError(result.stderr.strip() or f"Command failed with exit {result.returncode}")
    value = json.loads(result.stdout)
    if not isinstance(value, dict):
        raise ValueError("Expected a JSON object from HellForge")
    return value


def require_idle(root: Path) -> None:
    status = run_json([str(checked(root / "app/cli/HellForge.Cli.exe")), "status", "--install", str(root)], root)
    if Path(status["install"]).resolve() != root or status["session"]["running"] is not False:
        raise ValueError("HellForge is running or returned an unexpected installation/session state")
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
                             "$ErrorActionPreference='Stop'; ConvertTo-Json -Compress -InputObject @((Get-Process).ProcessName)"], capture_output=True,
                            text=True, timeout=30, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise ValueError("Could not inspect running processes")
    processes = json.loads(result.stdout)
    if not isinstance(processes, list) or not processes:
        raise ValueError("Process inspection returned no process list")
    names = {name.lower() for name in processes}
    if names & {"helldivers2", "hellforge"}:
        raise ValueError("Close helldivers2.exe and HellForge.exe before opening Warbonds")


@contextmanager
def profile_lease(path: Path) -> Iterator[None]:
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateFileW.argtypes = (wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                                  wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE)
    kernel.CreateFileW.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
    handle = kernel.CreateFileW(str(checked(path)), 0xC0000000, 0, None, 4, 0x80, None)
    if handle == wintypes.HANDLE(-1).value:
        raise OSError(ctypes.get_last_error(), "Profile lease unavailable")
    try:
        yield
    finally:
        kernel.CloseHandle(handle)


def uint(value: Any) -> int:
    if type(value) is not int or not 0 <= value <= 0xFFFFFFFF:
        raise ValueError("Expected an unsigned 32-bit JSON integer")
    return value


def items_by_id(value: Any) -> dict[int, dict[str, Any]]:
    if not isinstance(value, dict) or not isinstance(value.get("items"), list):
        raise ValueError("Inventory must contain an items array")
    items = {}
    for item in value["items"]:
        if not isinstance(item, dict) or type(item.get("isDiscovered")) is not bool:
            raise ValueError("Invalid inventory entry")
        mix = uint(item["mixId"])
        uint(item["amount"])
        if mix in items:
            raise ValueError(f"Duplicate inventory mixId: {mix}")
        items[mix] = item
    return items


def plan(profile: dict[str, Any], inventory: dict[str, Any], data: dict[str, Any]) -> dict[str, Any]:
    if profile.get("schemaVersion") != 1:
        raise ValueError("Unsupported profile schema")
    held = items_by_id(profile["inventory"])
    owned = items_by_id(inventory)
    passes = data["seasonPassDetails"]
    if not isinstance(passes, dict) or not passes:
        raise ValueError("Missing seasonPassDetails")
    report: dict[str, Any] = {"owned": [], "already_open": [], "to_add": [], "added": [], "skipped": []}
    for warbond in passes.values():
        warbond_id = uint(warbond["id32"])
        tokens = [uint(token) for token in warbond["premiumTokenMixIds"]]
        if warbond_id == FREE_WARBOND:
            report["skipped"].append({"id32": warbond_id, "reason": "free"})
            continue
        owned_tokens = [token for token in tokens if token in owned and owned[token]["amount"] > 0]
        if not owned_tokens:
            report["skipped"].append({"id32": warbond_id, "reason": "unowned"})
            continue
        report["owned"].append(warbond_id)
        if any(token in held and held[token]["amount"] > 0 for token in tokens):
            report["already_open"].append(warbond_id)
            continue
        token = owned_tokens[0]
        report["to_add"].append({"id32": warbond_id, "mixId": token})
        # ProfileStore.AddItem preserves discovery on an existing zero-amount entry.
        if token in held:
            held[token]["amount"] = 1
        else:
            entry = {"mixId": token, "amount": 1, "isDiscovered": True}
            profile["inventory"]["items"].append(entry)
            held[token] = entry
    return report


def replace_profile(path: Path, payload: bytes) -> None:
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.ReplaceFileW.argtypes = (wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.LPCWSTR,
                                   wintypes.DWORD, wintypes.LPVOID, wintypes.LPVOID)
    kernel.ReplaceFileW.restype = wintypes.BOOL
    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(dir=path.parent, prefix="profile.json.", suffix=".tmp", delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        if not kernel.ReplaceFileW(str(path), str(temporary), str(checked(Path(str(path) + ".bak"))), 2, None, None):
            raise ctypes.WinError(ctypes.get_last_error())
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def open_warbonds(root: Path, name: str, apply: bool) -> dict[str, Any]:
    require_idle(root)
    profile_path = checked(root / "profiles" / name / "profile.json")
    snapshot_dir = checked(root / "account-snapshot" / name)
    with profile_lease(profile_path.parent / "profile.lock"):
        original = profile_path.read_bytes()
        snapshots = {name: checked(snapshot_dir / name).read_bytes() for name in SNAPSHOT_FILES}
        metadata = json.loads(snapshots["snapshot.json"])
        if metadata.get("progression") is not True:
            raise ValueError("Snapshot has no captured progression")
        seal = json.loads(snapshots["seal.json"])
        for filename, key in (("real-inventory.json", "inventorySha256"), ("progression.json", "progressionSha256")):
            if hashlib.sha256(snapshots[filename]).hexdigest().upper() != seal.get(key):
                raise ValueError(f"Corrupt snapshot: {filename}")
        preview = run_json([str(checked(root / "app/backend/HellForge.LocalBackend.exe")),
                            "profile", "import-preview", str(profile_path.parent), "starter"], root)
        if preview["categories"]["level"]["vanilla"] in (None, "not captured"):
            raise ValueError("HellForge did not verify a complete account snapshot")
        progression_path = checked(root / "app/progression-data.json")
        progression = progression_path.read_bytes()
        profile = json.loads(original)
        report = plan(profile, json.loads(snapshots["real-inventory.json"]), json.loads(progression))
        report.update({"profile": name, "apply": apply, "backup": None, "backup_sha256": None})
        if apply and report["to_add"]:
            require_idle(root)
            if profile_path.read_bytes() != original or progression_path.read_bytes() != progression or any(
                    (snapshot_dir / name).read_bytes() != content for name, content in snapshots.items()):
                raise ValueError("Profile, progression data or snapshot changed during verification")
            folder = checked(BACKUPS / ("warbonds-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S.%fZ")))
            folder.mkdir(parents=True)
            backup = folder / "profile.json"
            backup.write_bytes(original)
            digest = hashlib.sha256(original).hexdigest()
            (folder / "profile.json.sha256").write_text(digest + "  profile.json\n", encoding="ascii")
            if hashlib.sha256(backup.read_bytes()).hexdigest() != digest:
                raise ValueError("Backup hash verification failed")
            payload = json.dumps(profile, indent=2, ensure_ascii=False).encode("utf-8")
            replace_profile(profile_path, payload)
            if profile_path.read_bytes() != payload or json.loads(profile_path.read_bytes()) != profile:
                raise ValueError(f"Saved profile verification failed. Backup: {backup}")
            report["added"] = report["to_add"]
            report["backup"] = str(backup)
            report["backup_sha256"] = digest
        report["counts"] = {key: len(report[key]) for key in ("owned", "already_open", "to_add", "added", "skipped")}
        return report


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--install", required=True, type=Path)
    parser.add_argument("--profile", default="default")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args(argv)
    try:
        if sys.platform != "win32":
            raise ValueError("This tool requires Windows profile leases and process inspection")
        if not re.fullmatch(r"[a-z0-9-]{1,32}", args.profile):
            raise ValueError("Invalid profile name")
        root = checked(args.install.absolute())
        print(json.dumps(open_warbonds(root, args.profile, args.apply), indent=2))
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        print(json.dumps({"error": str(error), "profile": args.profile, "apply": args.apply}))
        return 1


if __name__ == "__main__":
    sys.exit(main())
