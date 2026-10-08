#!/usr/bin/env python3
"""Independently audit the r21 public promotion without importing its builder.

Compare all logical rows and actual 96-state native selections with the sealed
private checkpoint, validate public content URLs against the current public
checkout, and independently unpack the two permitted portable-context addons.
This tool never accesses the game, settings, Git index or network.
"""

from __future__ import annotations

import argparse
from collections.abc import Sequence
import copy
import hashlib
import importlib.util
import json
import logging
from pathlib import Path
import re
import struct
import sys
from typing import Any

LOGGER = logging.getLogger(__name__)
ROOT = Path(__file__).resolve().parents[1]
TASK = ROOT / "dist/production-r21-2026-10-08"
BASE = ROOT / "dist/empire-deep-qa-2026-10-08/integration/candidate-v1"
BASE_SHA = "4c29b2ca52dc83af8ca3918c8c8a50f5ca2656146b40f3fa3e041bf915fffc8d"
VERSION = "2026.10.08-r21"
APP_SHA = "2effe777eb2673d7237b5fc9aed1cbe85434ba86f3c31fbae9f73129a06cd51e"
PREFIX = "https://github.com/owendavidgoode/clonedivers/releases/download/"
EMPTY = hashlib.sha256(b"").hexdigest()
KEYS = {"eagle": "6d133274694b50a9.a14e8dfa2cd117e2",
        "observer": "ea6b75393ee0b75d.a14e8dfa2cd117e2"}
MODULE = re.compile(r'^(package\.preload\[("(?:[^"\\]|\\.)*")\]=function\(\)return assert\(loadstring\(("(?:[^"\\]|\\.)*"),("(?:[^"\\]|\\.)*")\)\)\(\)end)$', re.M)
SDK = ROOT / "dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d/sdk/tools"
OBSERVER_DISABLED = """-- Public packs use baked walker assets; native-code capture stays disabled.
local PUBLIC_CAPTURE_ENABLED=false
if not PUBLIC_CAPTURE_ENABLED then
    return {readOnly=true,status='public_diagnostic_disabled',scaleWrites=0,
        started=false,nativeSpawnScaleEnabled=false}
end
"""


def require(value: bool, message: str) -> None:
    if not value:
        raise ValueError(message)


def pin(path: Path) -> dict[str, Any]:
    before = path.stat()
    require(path.is_file() and not path.is_symlink(), "Regular source file required: " + str(path))
    with path.open("rb") as handle:
        digest = hashlib.file_digest(handle, "sha256").hexdigest()
    after = path.stat()
    require((before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns), "Source changed while hashing")
    return {"path": str(path.resolve()), "bytes": after.st_size, "sha256": digest}


def read(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def normalize(value: Any) -> Any:
    if isinstance(value, dict):
        return {key[:1].lower() + key[1:]: normalize(item) for key, item in value.items()}
    if isinstance(value, list):
        return [normalize(item) for item in value]
    return value


def state_key(state: dict[str, Any]) -> str:
    return json.dumps({key: state[key] for key in ("profile", "mode", "requestedOptions")}, sort_keys=True)


def unpack_lua(raw: bytes, key: str) -> tuple[tuple[int, ...], str]:
    require(struct.unpack_from("<3I", raw) == (0xf0000011, 1, 1), "Singleton Lua archive header")
    require(struct.unpack_from("<Q", raw, 32)[0] == len(raw), "Lua archive complete declared length")
    row = struct.unpack_from("<7Q6I", raw, 104)
    require(f"{row[0]:016x}.{row[1]:016x}" == key and row[12] == 0, "Lua resource identity/ordinal")
    require(row[2] == 192 and row[8:10] == (0, 0) and row[10:12] == (16, 16), "Reviewed Lua framing")
    require(row[2] + row[7] <= len(raw) and not any(raw[184:192]) and not any(raw[row[2] + row[7]:]), "Lua range/zero padding")
    payload = raw[row[2]:row[2] + row[7]]
    require(struct.unpack_from("<2I", payload) == (len(payload) - 8, 2), "Native source Lua wrapper")
    return row, payload[8:].decode("utf-8")


def modules(body: str) -> tuple[dict[str, tuple[str, str]], str]:
    found: dict[str, tuple[str, str]] = {}
    for match in MODULE.finditer(body):
        name, source, debug_name = (json.loads(match[index]) for index in (2, 3, 4))
        require(name == debug_name and name not in found, "Unique named Lua module")
        found[name] = (source, match[0])
    require(found, "Expected bundled reviewed modules")
    return found, MODULE.sub("<BUNDLED_MODULE>", body)


def source_row(recipe: dict[str, Any], key: str) -> dict[str, Any]:
    sources = [item for item in recipe["sources"] if key in item["resources"]]
    require(len(sources) == 1, "Latest private Lua provider must be unique")
    return sources[0]


def context_fixtures(label: str, new_modules: dict[str, tuple[str, str]], vectors: list[dict[str, Any]]) -> dict[str, Any]:
    """Exercise independent contexts in offline LuaJIT with all file I/O mocked."""
    prefix = "codex_empire_vehicles" if label == "eagle" else "codex_empire_scale_observer"
    runner_path = SDK / "lua_runner.py"
    module_spec = importlib.util.spec_from_file_location("public_r21_independent_lua_runner", runner_path)
    require(module_spec is not None and module_spec.loader is not None, "Primary offline Lua runner required")
    runner = importlib.util.module_from_spec(module_spec)
    sys.modules[module_spec.name] = runner
    module_spec.loader.exec_module(runner)
    context = new_modules[prefix + "/context"][0]
    require(context.count("\nreturn M\n") == 1, "One context export for test-only instrumentation")
    instrumented = context.replace("\nreturn M\n", "\nM.peer_identity=receipt_identity\nM.peer_absolute=absolute_path\nreturn M\n")
    declarations = []
    for name in (prefix + "/json", prefix + "/context_spec"):
        source = new_modules[name][0]
        declarations.append("package.preload[" + json.dumps(name) + "]=function()return assert(loadstring(" + json.dumps(source, ensure_ascii=False) + "))()end")
    declarations.append("local context=assert(loadstring(" + json.dumps(instrumented, ensure_ascii=False) + "))()")
    declarations.append("local json=require(" + json.dumps(prefix + "/json") + ");local spec=require(" + json.dumps(prefix + "/context_spec") + ")")
    declarations.append("local vectors=json.decode(" + json.dumps(json.dumps(vectors, ensure_ascii=False), ensure_ascii=False) + ")")
    fixture = r'''
local calls={};local names={}
local originalOpen=io.open
io.open=function(path,mode) calls[#calls+1]={path=path,mode=mode};error('Unexpected real file access',0) end
local function documents()
 local config={InstalledPackVersion=spec.version,InstalledGameBuild=spec.build,
  InstalledGamePath='D:/SteamLibrary/steamapps/common/Helldivers 2',TextureProfile='full',InstalledTextureProfile='full',
  Options={empire=true,commandos=true,droids=true,covenant=false,aimpoints=true,skinny=false},LastVerifiedUtc='fixture-verified'}
 local receipt={PackVersion=spec.version,GameBuild=spec.build,GameDir=config.InstalledGamePath,TextureProfile='full',
  Options={empire=true,commandos=true,droids=true,covenant=false,aimpoints=true,skinny=false},
  Format=1,TargetActive=true,VerifiedUtc=config.LastVerifiedUtc,Files={}}
 local count=0
 for sha,size in pairs(spec.assetSizes) do
  count=count+1
  receipt.Files[#receipt.Files+1]={File={Name='9ba626afa44a3aa3.patch_'..count,Size=size,Sha256=sha}}
 end
 return config,receipt
end
local function accepted(name,mutation)
 local config,receipt=documents();if mutation then mutation(config,receipt) end
 assert(pcall(context.check,config,receipt,false),'Independent accepted case '..name)
 names[#names+1]={name=name,accepted=true}
end
local function rejected(name,mutation,pending)
 local config,receipt=documents();mutation(config,receipt)
 assert(not pcall(context.check,config,receipt,pending or false),'Independent rejected case '..name)
 names[#names+1]={name=name,accepted=false}
end
accepted('official-default-null-feed')
accepted('official-default-empty-feed',function(c)c.ManifestUrl=''end)
accepted('explicit-official-feed',function(c)c.ManifestUrl=spec.manifestUrl end)
accepted('custom-library-unicode',function(c,r)c.InstalledGamePath=vectors[2].gamePath;r.GameDir=c.InstalledGamePath end)
rejected('pending-install',function()end,true)
rejected('foreign-feed',function(c)c.ManifestUrl='https://foreign.invalid/manifest.json'end)
rejected('old-pack-version',function(c)c.InstalledPackVersion='old'end)
rejected('receipt-pack-version',function(c,r)r.PackVersion='old'end)
rejected('config-build',function(c)c.InstalledGameBuild='bad'end)
rejected('receipt-build',function(c,r)r.GameBuild='bad'end)
rejected('relative-game-path',function(c,r)c.InstalledGamePath='relative/path';r.GameDir=c.InstalledGamePath end)
rejected('game-path-mismatch',function(c,r)r.GameDir='E:/Other/Game'end)
rejected('lighter-request',function(c)c.TextureProfile='lighter'end)
rejected('lighter-installed',function(c)c.InstalledTextureProfile='lighter'end)
rejected('lighter-receipt',function(c,r)r.TextureProfile='lighter'end)
rejected('clone-mode',function(c)c.Options.empire=false end)
rejected('different-option-receipt',function(c,r)r.Options.droids=false end)
rejected('nonboolean-options',function(c,r)c.Options.empire='true';r.Options.empire='true'end)
rejected('unknown-receipt-format',function(c,r)r.Format=2 end)
rejected('inactive-receipt',function(c,r)r.TargetActive=false end)
rejected('stale-receipt',function(c,r)r.VerifiedUtc='stale'end)
rejected('missing-files',function(c,r)r.Files=nil end)
rejected('missing-closure-asset',function(c,r)table.remove(r.Files,1)end)
rejected('wrong-closure-size',function(c,r)r.Files[1].File.Size=r.Files[1].File.Size+1 end)
rejected('duplicate-closure-asset',function(c,r)r.Files[#r.Files+1]=r.Files[1]end)
rejected('unsafe-receipt-filename',function(c,r)r.Files[1].File.Name='../outside'end)
assert(#calls==0,'Pure context check touched files')
local actualVectors={}
for _,v in ipairs(vectors) do
 local canonical=context.peer_identity(v.gamePath)
 assert(canonical==v.canonical,'Actual public launcher/Lua Unicode canonical path differs: '..v.gamePath)
 actualVectors[#actualVectors+1]={gamePath=v.gamePath,canonical=canonical,sha256=v.sha256,actualLauncherReceiptFile=v.receiptFile}
end
local appData='Z:/Profiles/Another User/AppData/Roaming'
local root=context.peer_absolute(appData)..'/Clonedivers'
local config,receipt=documents()
config.InstalledGamePath=vectors[2].gamePath;receipt.GameDir=config.InstalledGamePath
local gamePath=context.peer_absolute(config.InstalledGamePath)
local main=string.rep('x',spec.assetSizes[spec.mainSha256])
local identity=string.rep('a',64)
local files={[root..'/config.json']=json.encode(config),[root..'/receipts/'..identity..'.json']=json.encode(receipt)}
local mainName
for _,entry in ipairs(receipt.Files) do if entry.File.Sha256==spec.mainSha256 then mainName=entry.File.Name end end
files[gamePath..'/data/'..mainName]=main
local writes=0;calls={}
io.open=function(path,mode)
 calls[#calls+1]={path=path,mode=mode}
 assert(mode=='rb','Fixture attempted file write')
 local value=files[path];if value==nil then return nil end
 return {read=function(_,limit)return value:sub(1,limit)end,close=function()return true end,
         write=function()writes=writes+1;error('Forbidden write',0)end}
end
local originalGetenv=os.getenv;os.getenv=function(key)assert(key=='APPDATA');return appData end
local canonical=context.peer_identity(gamePath)
local runtime={sha256=function(value)
 if value==main then return spec.mainSha256 end
 assert(value==canonical,'Receipt key hashed another path');return identity
end}
assert(pcall(context.require,runtime),'Portable public full require rejected custom library/user')
assert(writes==0 and #calls==4,'Portable require read-only four expected files')
local firstCalls=calls
files[root..'/apply-plan.json']='pending';calls={}
assert(not pcall(context.require,runtime),'Pending apply plan accepted by full require')
files[root..'/apply-plan.json']=nil
files[gamePath..'/data/'..mainName]=string.rep('z',#main);calls={}
assert(not pcall(context.require,runtime),'Changed main accepted by full require')
io.open=originalOpen;os.getenv=originalGetenv
return json.encode({passed=true,checkTests=names,checkCount=#names,actualLauncherLuaPathVectors=actualVectors,
 fullRequireCustomUserLibraryAccepted=true,fullRequirePendingRejected=true,fullRequireChangedMainRejected=true,
 fullRequireCalls=firstCalls,fileWrites=writes,realGameOrSettingsRead=false})
'''
    raw = runner.execute(("\n".join(declarations) + "\n" + fixture).encode("utf-8"))
    result = json.loads(raw.decode("utf-8"))
    require(result["passed"] and result["checkCount"] == 26 and result["fileWrites"] == 0, "Independent portable guard fixture coverage differs")
    result["runner"] = pin(runner_path)
    result["offlineLuaDll"] = pin(Path(runner.default_dll()))
    return result


def reverse_context(old: str, new: str) -> dict[str, Any]:
    """Independently reverse the five specific launcher-discovery changes."""
    matches = list(re.finditer(r"(?<=local M=\{\}\n)(local function absolute_path\(value\).*?)(?=local function document\()", new, re.S))
    require(len(matches) == 1, "Exactly one portable Windows path helper")
    helper = matches[0][1]
    require(set(re.findall(r"kernel\.([A-Za-z0-9_]+)", helper)) ==
            {"MultiByteToWideChar", "GetFullPathNameW", "LCMapStringEx", "WideCharToMultiByte"}, "Only string-conversion Windows calls allowed")
    require(not any(token in helper for token in ("io.", "os.", "runtime.", ":write", "Virtual", "ProcessMemory", "CreateFile")),
            "Portable path helper unexpectedly accesses process/files")
    require(helper.count("require('ffi')") == 1 and helper.count("ffi.load('kernel32')") == 1, "Only reviewed FFI library")
    restored = new[:matches[0].start()] + new[matches[0].end():]
    replacements = [
        ("    assert(config.ManifestUrl==nil or config.ManifestUrl=='' or config.ManifestUrl==spec.manifestUrl,\n        'CONTEXT_REFUSED: unreviewed feed')",
         "    assert(config.ManifestUrl==spec.manifestUrl,'CONTEXT_REFUSED: unreviewed feed')"),
        ("    absolute_path(config.InstalledGamePath)\n    assert(config.InstalledGamePath==receipt.GameDir,'CONTEXT_REFUSED: game path')",
         "    assert(config.InstalledGamePath==spec.gamePath and receipt.GameDir==spec.gamePath,\n        'CONTEXT_REFUSED: game path')"),
        ("    local appData=absolute_path(assert(os.getenv('APPDATA'),'CONTEXT_UNAVAILABLE: APPDATA'))\n    local root=appData..'/Clonedivers'\n    local config=document(root..'/config.json',1024*1024)\n    local gamePath=absolute_path(config.InstalledGamePath)\n    local identity=runtime.sha256(receipt_identity(gamePath)):lower()\n    assert(#identity==64 and not identity:find('[^%x]'),'CONTEXT_REFUSED: receipt identity')\n    local receipt=document(root..'/receipts/'..identity..'.json',8*1024*1024)\n    local plan=io.open(root..'/apply-plan.json','rb')",
         "    local config=document(spec.configPath,1024*1024)\n    local receipt=document(spec.receiptPath,8*1024*1024)\n    local plan=io.open(spec.pendingPath,'rb')"),
        ("io.open(gamePath..'/data/'..observed[main],'rb')", "io.open(spec.gamePath..'/data/'..observed[main],'rb')"),
    ]
    for before, after in replacements:
        require(restored.count(before) == 1, "Exactly one declared portable context block")
        restored = restored.replace(before, after)
    require(restored == old, "Context code changed beyond five reviewed portability blocks")
    return {"allOtherContextCodeReversedExactly": True, "pathHelperSha256": hashlib.sha256(helper.encode()).hexdigest(),
            "pathHelperWindowsCalls": sorted(set(re.findall(r"kernel\.([A-Za-z0-9_]+)", helper))),
            "nativeProcessAndFileAccessInPathHelper": False}


def audit_runtime(candidate: Path, recipe: dict[str, Any], new: dict[str, Any], vectors: list[dict[str, Any]]) -> tuple[dict[str, dict[str, Any]], list[dict[str, Any]]]:
    permitted: dict[str, dict[str, Any]] = {}
    proofs = []
    for label, key in KEYS.items():
        source = source_row(recipe, key)
        old_file = source["files"][0]
        rows = [item for item in new["pack"]["files"] if item["name"] == old_file["name"]]
        require(len(rows) == 1, "Public logical Lua provider must be unique")
        new_file = rows[0]
        old_path, new_path = BASE / "assets" / old_file["sha256"], candidate / "assets" / new_file["sha256"]
        require(pin(old_path)["sha256"] == old_file["sha256"] and pin(new_path)["sha256"] == new_file["sha256"], "Fresh raw addon hashes")
        old_raw, new_raw = old_path.read_bytes(), new_path.read_bytes()
        old_row, old_body = unpack_lua(old_raw, key)
        new_row, new_body = unpack_lua(new_raw, key)
        require(all(old_row[index] == new_row[index] for index in range(13) if index != 7), "Lua row metadata changed beyond source length")
        prefix = bytearray(new_raw[:192])
        prefix[32:40] = old_raw[32:40]
        prefix[160:164] = old_raw[160:164]
        require(bytes(prefix) == old_raw[:192], "Archive prefix changed beyond source/archive lengths")
        old_modules, old_rest = modules(old_body)
        new_modules, new_rest = modules(new_body)
        entrypoint_restored = new_rest
        if label == "observer":
            require(entrypoint_restored.count(OBSERVER_DISABLED) == 1, "Exactly one public observer early-disable block")
            entrypoint_restored = entrypoint_restored.replace(OBSERVER_DISABLED, "")
        require(old_rest == entrypoint_restored and old_modules.keys() == new_modules.keys(), "Addon entrypoint/module roster changed")
        module_prefix = "codex_empire_vehicles" if label == "eagle" else "codex_empire_scale_observer"
        allowed = {module_prefix + "/context", module_prefix + "/context_spec"}
        if label == "observer":
            allowed.update({module_prefix + "/capture_profile", module_prefix + "/observer"})
        changed = {name for name in old_modules if old_modules[name][0] != new_modules[name][0]}
        require(changed == allowed, "Only context and context_spec may change in addon code")
        # Reverse the two explicitly permitted module blocks to compare every
        # other source byte, including loader checks and native writer guards.
        reversed_body = new_body
        for name in sorted(allowed):
            reversed_body = reversed_body.replace(new_modules[name][1], old_modules[name][1])
        if label == "observer":
            reversed_body = reversed_body.replace(OBSERVER_DISABLED, "")
        require(reversed_body == old_body, "Full raw Lua source reversal differs")
        new_context = new_modules[module_prefix + "/context"][0]
        old_context = old_modules[module_prefix + "/context"][0]
        new_spec = new_modules[module_prefix + "/context_spec"][0]
        context_reverse = reverse_context(old_context, new_context)
        require(VERSION in new_spec and "2026.10.08-deepqa-private" not in new_spec, "Public runtime version differs")
        require(not any(value in new_body for value in ("C:/Users/goode", "C:\\\\Program Files", "127.0.0.1:18188")), "Owner-specific installation literal remains")
        # The port changes launcher context discovery. Native fingerprints,
        # package/asset identities and solo-ship authority gates remain in the
        # unchanged bundled modules and main entrypoint, proved above.
        guards = ("pending==false", "config.InstalledPackVersion==spec.version", "receipt.PackVersion==spec.version",
                  "config.InstalledGameBuild==spec.build", "receipt.GameBuild==spec.build",
                  "same_options(config.Options,receipt.Options)", "receipt.Format==1", "receipt.TargetActive==true",
                  "config.LastVerifiedUtc==receipt.VerifiedUtc", "observed[identity]", "runtime.sha256(bytes):lower()==main")
        for guard in guards:
            require(guard in old_context and guard in new_context, "A required context safety guard changed: " + guard)
        # Parse the narrow context-spec expression independently from the
        # public builder: four private paths are removed, all remaining
        # identity/asset metadata is retained with public version/feed.
        expected_spec = old_modules[module_prefix + "/context_spec"][0]
        expected_spec = expected_spec.replace("2026.10.08-deepqa-private", VERSION)
        expected_spec = expected_spec.replace("http://127.0.0.1:18188/manifest.json", "https://raw.githubusercontent.com/owendavidgoode/clonedivers/main/manifest-v3-current.json")
        for field in ("gamePath", "configPath", "receiptPath", "pendingPath"):
            pattern = re.compile(r'\["' + field + r'"\]="(?:[^"\\]|\\.)*",')
            require(len(pattern.findall(expected_spec)) == 1, "Exactly one original host-specific path")
            expected_spec = pattern.sub("", expected_spec)
        require(expected_spec == new_spec, "Portable context spec changed beyond version/feed/four machine paths")
        fixtures = context_fixtures(label, new_modules, vectors)
        observer_proof: dict[str, Any] | None = None
        if label == "observer":
            old_capture = old_modules[module_prefix + "/capture_profile"][0]
            new_capture = new_modules[module_prefix + "/capture_profile"][0]
            output_pattern = re.compile(r',\["outputRoot"\]="(?:[^"\\]|\\.)*"')
            require(len(output_pattern.findall(old_capture)) == 1 and output_pattern.sub("", old_capture) == new_capture,
                    "Observer capture profile changed beyond private output path removal")
            old_observer = old_modules[module_prefix + "/observer"][0]
            new_observer = new_modules[module_prefix + "/observer"][0]
            guard_pattern = re.compile(r"    assert\(p\.outputRoot:match\('[^']+'\),\n        '[^']+'\)")
            require(len(guard_pattern.findall(old_observer)) == 1 and guard_pattern.sub(
                "    assert(false,'OBSERVATION_REFUSED: native-code capture disabled in public releases')", old_observer) == new_observer,
                "Observer body changed beyond explicit capture prohibition")
            runner_spec = importlib.util.spec_from_file_location("public_r21_observer_inert_runner", SDK / "lua_runner.py")
            require(runner_spec is not None and runner_spec.loader is not None, "Offline observer execution runner")
            runner = importlib.util.module_from_spec(runner_spec)
            sys.modules[runner_spec.name] = runner
            runner_spec.loader.exec_module(runner)
            inert_test = "local calls=0;require=function()calls=calls+1;error('No public observer imports allowed')end;io.open=function()calls=calls+1;error('No public observer I/O allowed')end;local result=assert(loadstring(" + json.dumps(new_body, ensure_ascii=False) + "))();assert(calls==0 and result.readOnly==true and result.started==false and result.scaleWrites==0 and result.nativeSpawnScaleEnabled==false and result.status=='public_diagnostic_disabled');return 'PASS'"
            require(runner.execute(inert_test.encode("utf-8")) == b"PASS", "Public observer touched imports/I/O or enabled native writes")
            observer_proof = {"defaultEntrypointInvokedOffline": True, "imports": 0, "fileReadsOrWrites": 0,
                              "timersOrCallbacks": 0, "nativeWrites": 0, "allOtherCaptureGuardsExact": True,
                              "privateOutputRootRemoved": True, "captureBodyExplicitlyRefusesPublicExecution": True}
        for suffix, old_companion in zip((".stream", ".gpu_resources"), source["files"][1:], strict=True):
            companion = next(item for item in new["pack"]["files"] if item["name"] == old_file["name"] + suffix)
            require(companion == old_companion and companion["size"] == 0 and companion["sha256"] == EMPTY, "Lua companions changed")
        permitted[old_file["sha256"]] = {"sha256": new_file["sha256"], "size": new_file["size"], "label": label}
        proofs.append({"label": label, "typedKey": key, "logicalName": old_file["name"],
                       "source": pin(old_path), "candidate": pin(new_path), "gates": source["actualGates"],
                       "onlyChangedModules": sorted(changed), "sourceReversalExactOutsideExplicitContextModules": True,
                       "nativeWriterFingerprintScopeAssetAndLoaderModulesExact": True,
                       "nativeArchivePrefixOnlyLengthFieldsChanged": True,
                       "portableContextReversal": context_reverse, "observerPublicDisable": observer_proof,
                       "independentOfflineGuardFixtures": fixtures,
                       "oldContext": old_context, "publicContext": new_context,
                       "oldContextSpec": old_modules[module_prefix + "/context_spec"][0], "publicContextSpec": new_spec})
    require(len(permitted) == 2, "Exactly two independent portable Lua identities")
    return permitted, proofs


def audit(args: argparse.Namespace) -> dict[str, Any]:
    candidate, output, public = args.candidate.resolve(), args.out.resolve(), args.public_checkout.resolve()
    require(candidate.is_relative_to(TASK.resolve()) and output.is_relative_to((TASK / "qa").resolve()), "Output/input outside assigned release bounds")
    require(not output.exists(), "Fresh peer output required")
    require(pin(BASE / "manifest.json")["sha256"] == BASE_SHA, "Sealed private checkpoint drift")
    old, new, recipe = read(BASE / "manifest.json"), read(candidate / "manifest.json"), read(BASE / "recipe.json")
    require(old["format"] == new["format"] == 3 and len(old["pack"]["files"]) == len(new["pack"]["files"]) == 1794,
            "Promotion format/complete roster count differs")
    require(new["pack"]["version"] == VERSION and new["pack"].get("isPublished") is True, "Public release metadata differs")
    require(new["pack"]["totalSize"] == sum(item["size"] for item in new["pack"]["files"]), "Candidate total size differs")
    require(new["app"]["version"] == "1.7.3" and new["app"]["sha256"] == APP_SHA and new["app"]["size"] == 66408567,
            "Public launcher regressed or bytes changed")
    require(new["app"]["url"] == PREFIX + "v1.7.3/Clonedivers.exe", "Public app URL differs")
    binary = read(args.binary_report)
    require(binary["passed"] and binary["launcher"]["sha256"] == APP_SHA and
            binary["manifest"]["sha256"] == pin(candidate / "manifest.json")["sha256"] and
            binary["all96BinarySelectionsEqualCurrentSource"], "Actual public1.7.3 binary parity required")
    vectors = binary["receiptPathParityVectors"]
    require(len(vectors) == 7, "Actual public launcher receipt-path vector coverage")
    portable, runtime = audit_runtime(candidate, recipe, new, vectors)
    known: dict[str, tuple[str, int]] = {}
    feed_pins = []
    for name in ("manifest.json", "manifest-v3.json", "manifest-v3-current.json"):
        feed_pins.append(pin(public / name))
        for row in read(public / name)["pack"]["files"]:
            if row["size"]:
                identity = (row["sha256"], row["size"])
                require(row["url"] not in known or known[row["url"]] == identity, "Public catalog conflict")
                known[row["url"]] = identity
    restored = copy.deepcopy(new)
    restored["app"] = old["app"]
    for name in ("version", "notes", "statusNotes", "isPublished", "totalSize"):
        if name in old["pack"]:
            restored["pack"][name] = old["pack"][name]
        else:
            restored["pack"].pop(name, None)
    changed_rows, fresh_assets = [], {}
    for index, (previous, current, original) in enumerate(zip(old["pack"]["files"], new["pack"]["files"], restored["pack"]["files"], strict=True)):
        original["url"] = previous["url"]
        if previous["sha256"] in portable:
            expected = portable[previous["sha256"]]
            require(current["sha256"] == expected["sha256"] and current["size"] == expected["size"], "Portable Lua provider differs")
            original["sha256"], original["size"] = previous["sha256"], previous["size"]
            changed_rows.append({"rowIndex": index, "name": previous["name"], **expected})
        if not current["size"]:
            require(current["url"] == "" and current["sha256"] == EMPTY, "Empty companion URL/hash")
        else:
            require(re.fullmatch(re.escape(PREFIX) + r"pack-[A-Za-z0-9.\-]+-files(?:-\d+)?/" + current["sha256"], current["url"]) is not None,
                    "Non-public or non-content-addressed asset URL")
            if current["url"] in known:
                require(known[current["url"]] == (current["sha256"], current["size"]), "Hosted content identity changed")
            else:
                require(current["url"] == PREFIX + "pack-" + VERSION + "-files/" + current["sha256"], "Unknown old release URL")
                if current["sha256"] not in fresh_assets:
                    proof = pin(candidate / "assets" / current["sha256"])
                    require(proof["sha256"] == current["sha256"] and proof["bytes"] == current["size"], "New asset identity differs")
                    fresh_assets[current["sha256"]] = proof
    require(restored == old and len(changed_rows) == 2, "Promotion changed unapproved payloads, metadata, predicates or row order")
    before, after = normalize(read(BASE / "production96.json")), normalize(read(candidate / "production96.json"))
    require(before["passed"] and after["passed"] and before["manifestSha256"] == BASE_SHA and
            after["manifestSha256"] == pin(candidate / "manifest.json")["sha256"], "Native production selector pin differs")
    require(len(before["selections"]) == len(after["selections"]) == 96 and after["uniqueFileSets"] == 16, "Native selection coverage differs")
    previous_states = {state_key(item): item for item in before["selections"]}
    require(len(previous_states) == 96 and len({state_key(item) for item in after["selections"]}) == 96, "Unique production states required")
    state_proofs, coverage = [], {label: 0 for label in KEYS}
    for state in after["selections"]:
        previous_state = previous_states[state_key(state)]
        require(state["effectiveOptions"] == previous_state["effectiveOptions"], "Effective launcher options changed")
        previous_rows = before["fileSets"][previous_state["fileSet"]]
        current_rows = after["fileSets"][state["fileSet"]]
        require(len(previous_rows) == len(current_rows), "Actual physical selection count changed")
        restored_rows = copy.deepcopy(current_rows)
        changed = []
        for index, (previous, current, original) in enumerate(zip(previous_rows, current_rows, restored_rows, strict=True)):
            original["url"] = previous["url"]
            if previous["sha256"] in portable:
                expected = portable[previous["sha256"]]
                require(current["sha256"] == expected["sha256"] and current["size"] == expected["size"], "Selected public Lua hash differs")
                original["sha256"], original["size"] = previous["sha256"], previous["size"]
                coverage[expected["label"]] += 1
                changed.append({"index": index, "physicalName": previous["name"], "label": expected["label"]})
        require(restored_rows == previous_rows, "Actual physical names, selectors, ordering or payloads changed")
        expected_labels = set()
        if state["effectiveOptions"]["empire"]:
            expected_labels.add("eagle")
            if state["profile"] == "full":
                expected_labels.add("observer")
        require({item["label"] for item in changed} == expected_labels, "Portable runtime mode/profile coverage differs")
        state_proofs.append({"state": json.loads(state_key(state)), "effectiveOptions": state["effectiveOptions"],
                             "physicalTargets": len(current_rows), "changedPortableProviders": changed,
                             "allOtherPhysicalRowsExactAfterUrlReversal": True})
    require(coverage == {"eagle": 32, "observer": 16}, "Exact portable state coverage differs")
    result = {"passed": True, "method": "Independent logical/native-physical/raw-addon audit without promotion-builder imports",
              "sourceManifest": pin(BASE / "manifest.json"), "manifest": pin(candidate / "manifest.json"),
              "sourceProduction96": pin(BASE / "production96.json"), "production96": pin(candidate / "production96.json"),
              "sourcePromotionReport": pin(candidate / "source-promotion-report.json"), "tool": pin(Path(__file__)),
              "actualPublicLauncherBinaryParity": pin(args.binary_report),
              "publicFeedBaselines": feed_pins, "all1794RowsRestoredExactlyAfterDeclaredMetadataUrlAndTwoLuaChanges": True,
              "all96PhysicalSelectionsNamesOrderingOptionsAndOtherPayloadsExact": True, "portableProviderRows": changed_rows,
              "portableSelectedStates": coverage, "runtime": runtime, "newUniqueAssetsFreshlyHashed": len(fresh_assets),
              "newAssetBytes": sum(item["bytes"] for item in fresh_assets.values()), "newAssets": list(fresh_assets.values()),
              "stateProofs": state_proofs, "gameOrSettingsChanged": False, "networkUsed": False,
              "indexOrCommitChanged": False, "gameplayAccepted": False}
    output.mkdir(parents=True)
    (output / "report.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return {"passed": True, "receipt": pin(output / "report.json"), "rows": 1794, "states": 96,
            "newAssets": len(fresh_assets), "portableCoverage": coverage}


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--candidate", type=Path, default=TASK / "inputs-v1")
    parser.add_argument("--out", type=Path, default=TASK / "qa/peer-v1")
    parser.add_argument("--public-checkout", type=Path, default=Path("C:/Users/goode/.codex/worktrees/empire-public-r21/clonedivers"))
    parser.add_argument("--binary-report", type=Path, default=TASK / "qa/binary-v1/report.json")
    parser.add_argument("-v", "--verbose", action="count", default=0)
    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.INFO if args.verbose else logging.WARNING, format="%(levelname)s: %(message)s")
    try:
        print(json.dumps(audit(args)))
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, TypeError, struct.error):
        LOGGER.exception("Independent public r21 promotion peer failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
