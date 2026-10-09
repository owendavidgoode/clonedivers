#!/usr/bin/env python3
"""Verify raw final Lua archive, extracted modules and independent body policy."""
from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import re
import struct
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
AREA = ROOT / "dist/empire-yoda-death-2026-10-08"
OLD = ROOT / "dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d"


def need(ok: Any, message: str) -> None:
    if not ok:
        raise ValueError(message)


def pin(path: Path) -> dict[str, Any]:
    with path.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": digest}


def checked(item: dict[str, Any]) -> Path:
    path = Path(item["path"])
    need(pin(path) == item, "Frozen source changed: " + str(path))
    return path


def load(name: str, path: Path) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ImportError(name)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def decode(value: bytes | str) -> str:
    return value.decode() if isinstance(value, bytes) else value


def main() -> None:
    output = AREA / "armor/package-peer-v1"
    need(not output.exists(), "Fresh peer output required")
    candidate = AREA / "runtime/candidate-v3"
    report_path = candidate / "report.json"
    need(pin(report_path)["sha256"] == "bb8f255af5294903fe07c042658b8df566715d39b7aba5b67d70025cc2acb015", "Frozen candidate-v3 report")
    author = json.loads(report_path.read_text())
    raw = checked(author["files"][0]).read_bytes()
    need(raw[:4] == bytes.fromhex("110000f0") and struct.unpack_from("<II", raw, 4) == (1, 1), "One native archive type and resource")
    row = struct.unpack_from("<7Q6I", raw, 104)
    need(row[1] == 0xA14E8DFA2CD117E2 and row[8] == row[9] == 0, "Only native LUA resource, no companions")
    need(row[2] % 16 == 0 and row[2] + row[7] <= len(raw), "Bounded aligned LUA MAIN")
    payload = raw[row[2]:row[2] + row[7]]
    need(struct.unpack_from("<II", payload) == (len(payload) - 8, 2), "Native source-Lua wrapper")
    addon = payload[8:].decode()
    need(addon == checked(author["addon"]).read_text(), "Actual serialized addon exact source")
    for item in author["files"][1:]:
        need(checked(item).stat().st_size == 0, "Empty runtime companions")
    pattern = re.compile(r'^package\.preload\[("(?:[^"\\]|\\.)*")\]=function\(\)return assert\(loadstring\(("(?:[^"\\]|\\.)*"),("(?:[^"\\]|\\.)*")\)\)\(\)end$', re.MULTILINE)
    matches = list(pattern.finditer(addon))
    modules = {}
    for match in matches:
        name, source, chunk_name = (json.loads(match[i]) for i in range(1, 4))
        need(name not in modules and name == chunk_name and name.startswith("codex_lego_yoda/"), "Unique scoped module/chunk name")
        modules[name.removeprefix("codex_lego_yoda/")] = source
    need(set(modules) == set(author["modules"]) == {"profile", "world", "identity", "routing", "sound", "json", "context", "context_spec"}, "Exact eight serialized private modules")
    module_proofs = []
    for name, text in modules.items():
        actual_source = checked(author["modules"][name])
        need(text == actual_source.read_text(), "Serialized module changed: " + name)
        if name in {"profile", "world", "identity", "routing", "sound"}:
            need(text == (AREA / "runtime/prepared-v2" / (name + ".lua")).read_text(), "Reviewed prepared module changed: " + name)
        module_proofs.append({"name": name, "source": pin(actual_source), "serializedSha256": hashlib.sha256(text.encode()).hexdigest()})
    body_peer_path = AREA / "armor/runtime-peer-v2/report.json"
    need(pin(body_peer_path)["sha256"] == "cd2f30ae8c1606a2bf3effc45822edc327f698f9ae824809ff44c09edcd1c5d6", "Independent81fixture seal")
    body_peer = json.loads(body_peer_path.read_text())
    original = load("yoda_package_independent_fixtures", ROOT / "tools/yoda-death-armor-runtime-peer.py")
    identity_fixtures = original.IDENTITY.replace("name=='hd2runtime/runtime/event_world'", "name=='codex_lego_yoda/world'")
    identity_fixtures = identity_fixtures.replace("local validneg={", "local validneg={\n {'cached-category-changed',function(s,p,players,e,f)f.u32(f.kit1+f.kitType,1)end,'APPLIED_ARMOR_CHANGED'},")
    routing_fixtures = original.ROUTING.replace("local negatives={", "local negatives={\n {'non-table-cause',function(s,r,e)e.cause=123 end,'NOT_NATIVE_DEATH'},\n {'non-table-position',function(s,r,e)e.position=123 end,'NO_DEATH_POSITION'},")
    bytes_source = (AREA / "runtime/primary-v1/source/core/bytes.lua").read_text()
    routing_code = "local M=(function()\n" + modules["routing"] + "\nend)()\n" + routing_fixtures
    identity_code = "local P=(function()\n" + modules["profile"] + "\nend)()\nlocal B=(function()\n" + bytes_source + "\nend)()\nlocal function load_identity()\n" + modules["identity"] + "\nend\n" + identity_fixtures
    need(routing_code == checked(body_peer["fixtures"][0]).read_text(), "Extracted routing exactly same reviewed fixture program")
    need(identity_code == checked(body_peer["fixtures"][1]).read_text(), "Extracted identity exactly same reviewed fixture program")
    vm = load("yoda_package_offline_lua", OLD / "sdk/tools/lua_runner.py")
    routing_result = decode(vm.execute(routing_code.encode())).splitlines()
    identity_result = decode(vm.execute(identity_code.encode())).splitlines()
    need(routing_result == body_peer["routingCases"] and identity_result == body_peer["identityCases"], "Fresh81fixture results exact")
    context_program = checked(author["contextFixtures"]["program"])
    context_result = json.loads(decode(vm.execute(context_program.read_bytes())))
    need(context_result["passed"] and context_result["filesystemWrites"] == 0 and context_result["realWindowsPathApis"] and len(context_result["checks"]) == 60, "Fresh60 actual Win32/Bcrypt context fixtures")
    need(all(check["passed"] for check in context_result["checks"]), "All source context/negative controls passed")
    need("if event.local_player~=true then return end" in addon and "pcall(Context.recheck,runtime)" in addon and "route.on_death(event)" in addon, "Packaged entry local/death/context integration present")
    need("hd2.version=='0.28.1'" in addon and "loader.version>=16" in addon, "Packaged exact existing SDK and loader requirements")
    output.mkdir(parents=True)
    for name, source in modules.items():
        (output / (name + ".lua")).write_text(source)
    report = {"status": "passed", "candidateReport": pin(report_path), "archive": pin(checked(author["files"][0])),
              "serializedResource": f"{row[0]:016x}.{row[1]:016x}", "modules": module_proofs,
              "independentBodyPeer": pin(body_peer_path), "actualExtractedBodyFixtureCount": len(routing_result) + len(identity_result),
              "freshContextProgram": pin(context_program), "freshContextFixtures": context_result,
              "contextReview": "Full installed receipt/version/build/profile/options and both LEGO visual archives + additiveBANK closure. Startup currentBANK hash; per-death file metadata and pending transaction guard. Local-only native dead state routed after context recheck. Existing SDK retained.",
              "limits": ["Loaded game native pins and Wwise event residency/audibility not proven offline.",
                         "Fresh model/GPU contents are bound by installer receipt and metadata; context hashes current audioMAIN at startup. Metadata guard is not adversarial tamper protection.",
                         "Generic death VO retained; remote players and between-poll avatar removals unsupported."],
              "runtimeAccepted": False, "liveChanges": False, "tool": pin(Path(__file__))}
    destination = output / "report.json"
    destination.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(pin(destination)))


if __name__ == "__main__":
    main()
