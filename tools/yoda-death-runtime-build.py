#!/usr/bin/env python3
"""Build a minimal offline LEGO-only death-routing prototype and native proof."""
from __future__ import annotations

import argparse
from collections.abc import Sequence
import hashlib
import importlib.util
import json
import logging
from pathlib import Path
import re
import struct
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
AREA = ROOT / "dist/empire-yoda-death-2026-10-08/runtime"
PRIMARY = AREA / "primary-v1/source"
OLD = ROOT / "dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d"
GAME = Path("C:/Program Files (x86)/Steam/steamapps/common/Helldivers 2")


def pin(path: Path) -> dict[str, Any]:
    with path.open("rb") as handle:
        sha = hashlib.file_digest(handle, "sha256").hexdigest()
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": sha}


def lua(value: Any) -> str:
    if value is None:
        return "nil"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, str):
        return json.dumps(value, ensure_ascii=True)
    if isinstance(value, (int, float)):
        return str(value)
    if isinstance(value, list):
        return "{" + ",".join(map(lua, value)) + "}"
    return "{" + ",".join("[" + lua(k) + "]=" + lua(v) for k, v in value.items()) + "}"


def runner() -> Any:
    spec = importlib.util.spec_from_file_location("yoda_primary_lua_runner", OLD / "sdk/tools/lua_runner.py")
    if spec is None or spec.loader is None:
        raise ImportError("Pinned primary offline Lua runner")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def world_source() -> str:
    """Copy only required read paths from the pinned, already shipped event reader."""
    source = (OLD / "runtime/event_world.lua").read_text()
    def span(start: str, end: str) -> str:
        assert source.count(start) == 1 and source.count(end) == 1
        return source[source.index(start):source.index(end)]
    result = (
        "-- Uses code / research from HD2Runtime by SkyeShade.\n"
        "-- https://github.com/SkyeShade/HD2Runtime (96ab2d25, LICENSE sections 2/3).\n"
        "-- Selected read paths only, isolated read-only adapter; no global SDK reconfiguration.\n"
        "local natives=require('hd2runtime/domains/event_natives')\n"
        "local native_view=require('hd2runtime/runtime/native_view')\n"
        "local b=require('hd2runtime/core/bytes')\n"
        "local metrics=require('hd2runtime/runtime/metrics')\n"
        "local M={}\n"
        "local H,P,A,S=natives.health,natives.players,natives.playerAvatars,natives.state\n"
        "local IMAGE_SIZE,EXE_IMAGE_SIZE=natives.source.imageSize,natives.source.exeImageSize\n"
        "local opened,adapter_override\n"
        "function M.set_runtime(runtime)adapter_override=runtime;opened=nil end\n"
        "local function adapter()\n"
        " if adapter_override then return adapter_override end\n"
        " return require('hd2runtime/runtime/windows_readonly')()\nend\n"
        + span("local function module_base(", "-- Catalog facts about an entity type")
        + span("function M.local_peer(", "------------------------------------------------------------------------------------------- engine identity")
        + span("function M.network_entity(", "------------------------------------------------------------------------------------------------------ stats")
        + "return M\n"
    )
    assert "windows_write" not in result and "native_heal" not in result
    return result


def profile(vm: Any) -> dict[str, Any]:
    paths = ["domains/player_passives.lua", "domains/stratagem_selector.lua", "domains/wwise_plugin.lua"]
    pattern = re.compile(r'^package\.preload\[("(?:[^"\\]|\\.)*")\]=function\(\)return assert\(loadstring\(("(?:[^"\\]|\\.)*"),("(?:[^"\\]|\\.)*")\)\)\(\)end$', re.MULTILINE)
    modules = {json.loads(m[1]): json.loads(m[2]) for m in pattern.finditer((ROOT / "dist/production-r22-2026-10-08/runtime-v1/eagle/addon.lua").read_text())}
    prefix = "local j=(function()\n" + modules["codex_empire_vehicles/json"] + "\nend)()\n"
    # Data-only generated primary domains, without loading runtime/API modules.
    for index, name in enumerate(paths):
        prefix += f"local d{index}=(function()\n" + (PRIMARY / name).read_text() + "\nend)()\n"
    prefix += "return j.encode({identity={source=d0.source,manager=d0.manager,record=d0.record,kit=d0.kit,pins=d0.pins},sound={source=d1.source,selectorPins=d1.pins,uiSound=d1.uiSound,worldList=d1.slotOverlay,plugin=d2,eventName='clonedivers_lego_yoda_death'}})"
    return json.loads(vm.execute(prefix.encode()))


def native_check(path: Path, pins: list[dict[str, Any]]) -> dict[str, Any]:
    data = path.read_bytes()
    pe = struct.unpack_from("<I", data, 60)[0]
    count, optional = struct.unpack_from("<H", data, pe + 6)[0], struct.unpack_from("<H", data, pe + 20)[0]
    sections = [struct.unpack_from("<8s6I2HI", data, pe + 24 + optional + 40 * i) for i in range(count)]
    def at(rva: int, size: int) -> bytes | None:
        for row in sections:
            if row[2] <= rva and rva + size <= row[2] + row[3]:
                offset = row[4] + rva - row[2]
                return data[offset:offset + size]
        return None
    failures = []
    for item in pins:
        expected = bytes.fromhex(item["hex"])
        actual = at(item["rva"], len(expected))
        if actual != expected:
            failures.append({"label": item["label"], "rva": item["rva"], "expected": expected.hex(),
                             "actual": actual.hex() if actual is not None else None,
                             "fileBacked": actual is not None})
    return {"file": pin(path), "pinCount": len(pins), "matched": len(pins) - len(failures),
            "mismatches": failures, "loadedImageRead": False}


def run(output: Path) -> dict[str, Any]:
    if output.exists() or not output.resolve().is_relative_to(AREA):
        raise ValueError("Fresh scoped runtime output required")
    primary_report = AREA / "primary-v1/report.json"
    if pin(primary_report)["sha256"] != "68af5c3067afd3c9c99939fd89068f0082403152080b12fdf93a59fe0823759d":
        raise ValueError("Primary source seal changed")
    vm = runner()
    data = profile(vm)
    native = []
    selector = data["sound"]["selectorPins"]
    native.append(native_check(GAME / "data/game/game.dll", data["identity"]["pins"] + [p for p in selector if p["module"] == "game"]))
    native.append(native_check(GAME / "bin/helldivers2.exe", [p for p in selector if p["module"] == "exe"]))
    native.append(native_check(GAME / "bin/plugins/wwise_pluginw64_release.dll", data["sound"]["plugin"]["pins"]))
    expected = data["identity"]["source"]["gameDllSha256"].lower()
    if native[0]["file"]["sha256"] != expected:
        raise ValueError("Actual native build fingerprint differs")
    if native[1]["file"]["sha256"] != data["sound"]["source"]["exeSha256"].lower():
        raise ValueError("Actual executable fingerprint differs")
    output.mkdir(parents=True)
    (output / "world.lua").write_text(world_source(), encoding="utf-8")
    (output / "profile.lua").write_text("return " + lua(data) + "\n", encoding="utf-8")
    modules = []
    for label in ("identity", "routing", "sound"):
        source = ROOT / ("tools/yoda-death-runtime-" + label + ".lua")
        destination = output / (label + ".lua")
        destination.write_bytes(source.read_bytes())
        modules.append(pin(destination))
    modules.append(pin(output / "world.lua"))
    (output / "profile.json").write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    result = {"passed": True, "meaningOfPassed": "Source, current native fingerprint and bounded prototype preparation only",
              "tool": pin(Path(__file__)), "primary": pin(primary_report), "modules": modules,
              "profile": pin(output / "profile.lua"), "nativeFiles": native,
              "identityScope": "Local applied body armor; normal and LEGO bikini; every helmet alone rejects",
              "remoteRoutingAuthored": False, "avatarRemovedTriggersAllowed": False,
              "runtimeAccepted": False, "candidatePackaged": False, "liveChanges": False,
              "nativeStaticPinsAllMatched": all(not item["mismatches"] for item in native),
              "limits": ["Loaded game.dll instruction pins must prove at runtime; disk bytes may be encrypted.",
                         "A reviewed installed LEGO-model closure and additive resident event-bank closure are required.",
                         "No whole HD2Runtime upgrade is applied; only three bounded prototype modules authored."]}
    report = output / "report.json"
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return pin(report)


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=AREA / "candidate-v1")
    logging.basicConfig(level=logging.INFO)
    try:
        print(json.dumps(run(parser.parse_args(argv).out)))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception("Runtime prototype build failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
