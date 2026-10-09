#!/usr/bin/env python3
"""Package the bounded LEGO-only death addon and qualify its portable context."""
from __future__ import annotations

import argparse
from collections.abc import Sequence
import copy
import importlib.util
import inspect
import json
import logging
from pathlib import Path
import re
import struct
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]


def module(name: str, path: Path) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec and spec.loader
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


B = module("yoda_runtime_build", ROOT / "tools/yoda-death-runtime-build.py")
OLD_CONTEXT = ROOT / "dist/production-r22-2026-10-08/runtime-v1/eagle/context.lua"
OLD_ADDON = OLD_CONTEXT.with_name("addon.lua")
INVENTORY = ROOT / "dist/empire-yoda-death-2026-10-08/armor/inventory-v1/report.json"
AUDIO = ROOT / "dist/empire-yoda-death-2026-10-08/audio/candidate-v1/report.json"
AUDIO_MAIN = AUDIO.parent / "bundle/9ba626afa44a3aa3.patch_0"
VERSION = "2026.10.08-r23"
RESOURCE = "mods/codex/empire_lego_yoda_death"
MAIN_SHA = "f7c777c65d8b6b343e8b8f89d5c6f20ac3c156a7f5be981134a4f000fc984816"


def one(source: str, before: str, after: str) -> str:
    assert source.count(before) == 1, before[:150]
    return source.replace(before, after)


def context_source() -> str:
    assert B.pin(OLD_CONTEXT)["sha256"] == "58ad20ba61af65fdb2b3acc4eb41fa2e70340956fed0e21cdf166795854715cb"
    source = OLD_CONTEXT.read_text().replace("codex_empire_vehicles/", "codex_lego_yoda/")
    source = source.replace("aircraft", "LEGO/audio")
    source = one(source, "function M.require(runtime)", "local function read_context(runtime)")
    source = one(source, "    return observed\nend\nreturn M\n", """    local files={root..'/config.json',root..'/receipts/'..identity..'.json'}
    for sha in pairs(spec.assetSizes)do files[#files+1]=gamePath..'/data/'..observed[sha]end
    return observed,{files=files,pending=root..'/apply-plan.json'}
end
""" + (ROOT / "tools/yoda-death-runtime-context-tail-v4.lua").read_text())
    return source


def closure() -> tuple[dict[str, Any], list[dict[str, Any]]]:
    assert B.pin(INVENTORY)["sha256"] == "22967e062cd86601ef9ad54afa9534518699b4bac89140ddfe2498231369e7b5"
    assert B.pin(AUDIO)["sha256"] == "994b87c9c7f78d5841b10f6dbec1d65c82ffb1f1ec818523a12c53e0051981f5"
    assert B.pin(AUDIO_MAIN)["sha256"] == MAIN_SHA
    inv = json.loads(INVENTORY.read_text())
    manifest = json.loads(Path(inv["publicManifest"]["path"]).read_text())
    by_sha = {row["sha256"]: row for row in manifest["pack"]["files"]}
    visuals = {unit["unit"] for unit in inv["units"]}
    main_shas = {state["winningUnitRows"][key]["sha256"] for state in inv["profileWinners"].values() for key in visuals}
    assert len(main_shas) == 2
    rows = []
    for sha in sorted(main_shas):
        row = by_sha[sha]
        assert row.get("modes") == ["empire"] and row.get("textureProfiles") is None
        for suffix in ("", ".gpu_resources"):
            matches = [item for item in manifest["pack"]["files"] if item["name"] == row["name"] + suffix]
            assert len(matches) == 1
            rows.append(matches[0])
    sizes = {item["sha256"]: item["size"] for item in rows}
    sizes[MAIN_SHA] = B.pin(AUDIO_MAIN)["bytes"]
    return {"version": VERSION, "manifestUrl": "https://raw.githubusercontent.com/owendavidgoode/clonedivers/main/manifest-v3-current.json",
            "build": "25480438", "assetSizes": sizes, "mainSha256": MAIN_SHA}, rows


def context_fixtures(output: Path, addon: Path, spec: dict[str, Any], runner: Any) -> dict[str, Any]:
    """Adapt the sealed r22 actual-Win32 portable fixture to the five-asset closure."""
    legacy = module("yoda_previous_context_fixtures", ROOT / "tools/public-r22-runtime.py")
    source = inspect.getsource(legacy.fixtures)
    source = source.replace("codex_empire_vehicles", "codex_lego_yoda")
    source = source.replace("fb7b316e34c8466e025b1ce9d18eb13a5b5b079dc57a06e0c6d7dac21d7bc8c2", MAIN_SHA)
    start, end = source.index("    all_assets = "), source.index("    config = ")
    source = source[:start] + "    main_file = AUDIO_MAIN\n" + source[end:]
    start, end = source.index("    receipt = "), source.index("    preloads = ")
    source = source[:start] + "    receipt = RECEIPT_FACTORY(config)\n" + source[end:]
    source = one(source, "local runtime=require('hd2runtime/runtime/windows_readonly')()", """local runtime=require('hd2runtime/runtime/windows_readonly')()
local metadata_generation=0
runtime.context_file_stamp=function(path)return path..':'..metadata_generation end""")
    source = one(source, "return json.encode({passed=true,checks=checks,filesystemWrites=writes", """reset();metadata_generation=0;Context.require(runtime);reads={}
assert(Context.recheck(runtime));assert(#reads==1,'cached guard must read pending plan only')
checks[#checks+1]={name='cached-context-no-bank-rehash',passed=true,accepted=true,fileReads=#reads}
metadata_generation=1;assert(not pcall(Context.recheck,runtime))
checks[#checks+1]={name='cached-metadata-change-disarms',passed=true,accepted=false}
metadata_generation=0;pending=true;assert(not pcall(Context.recheck,runtime))
checks[#checks+1]={name='cached-pending-plan-disarms',passed=true,accepted=false}
pending=false
local nohook={}
local stamp=Context.file_stamp_for_tests(nohook,MAIN_FILE)
assert(type(stamp)=='string'and#stamp==28 and stamp==Context.file_stamp_for_tests(nohook,MAIN_FILE))
assert(not pcall(Context.file_stamp_for_tests,nohook,MAIN_FILE..'.missing'))
checks[#checks+1]={name='real-win32-file-metadata',passed=true,accepted=true}
local ffi=require('ffi')
local realLoad=ffi.load
local realKernel=realLoad('kernel32')
local words={32,101,102,201,202,301,302,0,4372192}
local proxy=setmetatable({GetFileAttributesExW=function(_,_,data)
 for i=1,9 do data[i-1]=words[i]end;return 1
end},{__index=realKernel})
ffi.load=function(name)if name=='kernel32'then return proxy end;return realLoad(name)end
runtime.context_file_stamp=function(path)return Context.file_stamp_for_tests(nohook,path)end
reset();Context.require(runtime);words[4]=211;words[5]=212
assert(Context.recheck(runtime))
checks[#checks+1]={name='last-access-only-change-accepted',passed=true,accepted=true}
words[6]=311;assert(not pcall(Context.recheck,runtime))
checks[#checks+1]={name='last-write-change-disarms',passed=true,accepted=false}
words[6]=301;words[9]=4372193;assert(not pcall(Context.recheck,runtime))
checks[#checks+1]={name='file-size-change-disarms',passed=true,accepted=false}
ffi.load=realLoad
return json.encode({passed=true,checks=checks,filesystemWrites=writes""")
    source = source.replace('len(result["checks"]) == 56', 'len(result["checks"]) == 63')
    def receipt_factory(config: dict[str, Any]) -> dict[str, Any]:
        identities = [MAIN_SHA] + sorted(set(spec["assetSizes"]) - {MAIN_SHA})
        files = [{"File": {"Name": "9ba626afa44a3aa3.patch_380" if sha == MAIN_SHA else f"9ba626afa44a3aa3.patch_{400+i}",
                           "Sha256": sha, "Size": spec["assetSizes"][sha]}} for i, sha in enumerate(identities)]
        return {"Format": 1, "PackVersion": VERSION, "GameBuild": "25480438", "GameDir": config["InstalledGamePath"],
                "TextureProfile": "full", "TargetActive": True, "VerifiedUtc": config["LastVerifiedUtc"],
                "Options": copy.deepcopy(config["Options"]), "Files": files}
    scope = dict(vars(legacy))
    scope.update({"OUTPUT": output, "VERSION": VERSION, "AUDIO_MAIN": AUDIO_MAIN, "RECEIPT_FACTORY": receipt_factory})
    exec(compile(source, "<reviewed-r22-context-fixture-adaptation>", "exec"), scope)
    (output / "eagle").mkdir()
    result = scope["fixtures"]({"addon": B.pin(addon), "label": "eagle"}, runner)
    return {"adaptationSource": B.pin(ROOT / "tools/public-r22-runtime.py"), "exactPrimaryTemplateAdapted": True, **result}


def run(prepared: Path, output: Path) -> dict[str, Any]:
    if output.exists() or not output.resolve().is_relative_to(B.AREA):
        raise ValueError("Fresh scoped output required")
    assert B.pin(prepared / "report.json")["sha256"] == "79b44ab91b923e4b0486572cd3b532f04dcfe1178c3685ea9d852b6cfc63fa72"
    fixture_report = B.AREA / "fixtures-v1/report.json"
    assert B.pin(fixture_report)["sha256"] == "2ade34aed0e13ee4e93b4d7c026eb6a976a0aaaf5001dd5cbdd8d08be53c6175"
    spec, visual_rows = closure()
    output.mkdir(parents=True)
    pattern = re.compile(r'^package\.preload\[("(?:[^"\\]|\\.)*")\]=function\(\)return assert\(loadstring\(("(?:[^"\\]|\\.)*"),("(?:[^"\\]|\\.)*")\)\)\(\)end$', re.MULTILINE)
    old_modules = {json.loads(m[1]): json.loads(m[2]) for m in pattern.finditer(OLD_ADDON.read_text())}
    sources = {name: (prepared / (name + ".lua")).read_text() for name in ("profile", "world", "identity", "routing", "sound")}
    sources["json"] = old_modules["codex_empire_vehicles/json"]
    sources["context"] = context_source()
    sources["context_spec"] = "return " + B.lua(spec) + "\n"
    for name, source in sources.items():
        (output / (name + ".lua")).write_text(source, encoding="utf-8")
    entry = (ROOT / "tools/yoda-death-runtime-entry.lua").read_text()
    header = """-- HD2-Addon: mods/codex/empire_lego_yoda_death
-- Uses code / research from HD2Runtime by SkyeShade.
-- https://github.com/SkyeShade/HD2Runtime (LICENSE sections 2/3).
local loader=rawget(_G,'CowboyBingusModLoader')
assert(loader and loader.api==1 and type(loader.version)=='number'and loader.version>=16,'Requires Bingus Shared Loader v16+ / API1')
local hd2=require('mods/skyeshade/hd2runtime')
assert(hd2.api_version==1 and hd2.version=='0.28.1','Requires reviewed HD2Runtime0.28.1')
"""
    addon = header + "".join(f"package.preload[{B.lua('codex_lego_yoda/'+name)}]=function()return assert(loadstring({B.lua(source)},{B.lua('codex_lego_yoda/'+name)}))()end\n" for name, source in sources.items()) + entry
    addon_path = output / "addon.lua"
    addon_path.write_text(addon, encoding="utf-8")
    runner = B.runner()
    assert runner.execute(("assert(loadstring(" + B.lua(addon) + "));return 'compiled'").encode()) == b"compiled"
    writer = module("yoda_primary_archive_writer", B.OLD / "sdk/tools/hd2_archive.py")
    name_hash = writer.resource_hash(RESOURCE)
    archive = writer.make_archive({name_hash: writer.lua_resource(addon.encode())})
    # Independently read the one typed directory row and Lua length/kind wrapper.
    assert struct.unpack_from("<III", archive) == (0xF0000011, 1, 1)
    row = struct.unpack_from("<7Q6I", archive, 104)
    assert row[:2] == (name_hash, writer.LUA_TYPE) and row[2] % 16 == 0
    payload = archive[row[2]:row[2] + row[7]]
    assert struct.unpack_from("<II", payload) == (len(addon.encode()), 2) and payload[8:] == addon.encode()
    bundle = output / "bundle"
    bundle.mkdir()
    main = bundle / "9ba626afa44a3aa3.patch_0"
    main.write_bytes(archive)
    files = [B.pin(main)]
    for suffix in (".stream", ".gpu_resources"):
        path = bundle / (main.name + suffix)
        path.write_bytes(b"")
        files.append(B.pin(path))
    context = context_fixtures(output, addon_path, spec, runner)
    report = {"passed": True, "meaningOfPassed": "Packaged offline native/identity/policy/context guards; real game and audible playback still pending",
              "tool": B.pin(Path(__file__)), "prepared": B.pin(prepared / "report.json"),
              "runtimeFixtures": B.pin(fixture_report), "contextFixtures": context,
              "files": files, "typedKeys": [f"{name_hash:016x}.{writer.LUA_TYPE:016x}"], "resource": RESOURCE,
              "addon": B.pin(addon_path), "modules": {name: B.pin(output / (name + ".lua")) for name in sources},
              "entry": B.pin(ROOT / "tools/yoda-death-runtime-entry.lua"),
              "nativePreparation": json.loads((prepared / "report.json").read_text())["nativeFiles"],
              "sourceIdentity": B.pin(INVENTORY), "audioSource": B.pin(AUDIO), "audioMain": B.pin(AUDIO_MAIN),
              "visualClosureRows": visual_rows, "contextSpec": spec,
              "sdkVersionRequired": "0.28.1", "wholeSdkUpgrade": False, "scope": "local player's applied category-0 body armor B513FD54/E9ADD047; every helmet alone rejected",
              "event": {"name": "clonedivers_lego_yoda_death", "fnv1": 4055635130},
              "startupHashOnly": True, "perTickFilesystemReads": False, "perDeathCachedWin32FileMetadataGuard": True,
              "remoteRoutingAuthored": False, "avatarRemovedAllowed": False, "sharedVoiceCueReplacements": False,
              "nativeMemoryWrites": False, "liveChanges": False, "runtimeAccepted": False,
              "limits": ["Actual loaded game native pins and Wwise has_event/play acceptance require playtest.",
                         "Only native dead_state is accepted; gib/removal between polls may miss the sound.",
                         "Only local diver is supported; no network replication or remote sound routing.",
                         "Metadata snapshots detect ordinary installed-file changes after startup; this is not an adversarial tamper detector.",
                         "A successful positional API call alone does not prove audible output or emitter behavior."]}
    path = output / "report.json"
    path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return B.pin(path)


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prepared", type=Path, default=B.AREA / "prepared-v2")
    parser.add_argument("--out", type=Path, default=B.AREA / "candidate-v4")
    logging.basicConfig(level=logging.INFO)
    try:
        args = parser.parse_args(argv)
        print(json.dumps(run(args.prepared, args.out)))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception("Runtime packaging failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
