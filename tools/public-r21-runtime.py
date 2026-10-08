#!/usr/bin/env python3
"""Promote the two sealed Empire runtime addons to portable public contexts.

The reviewed addon bodies and all native read/write guards remain exact outside
their launcher context modules. New output is immutable and never installed.
"""
from __future__ import annotations

import argparse
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

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "dist/empire-deep-qa-2026-10-08/integration/candidate-v1"
SOURCE_SHA = "4c29b2ca52dc83af8ca3918c8c8a50f5ca2656146b40f3fa3e041bf915fffc8d"
OUTPUT = ROOT / "dist/production-r21-2026-10-08/runtime-v1"
SDK_ROOT = ROOT / "dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d"
VERSION = "2026.10.08-r21"
OFFICIAL_FEED = "https://raw.githubusercontent.com/owendavidgoode/clonedivers/main/manifest-v3-current.json"
SUFFIXES = ("", ".stream", ".gpu_resources")
MODULE = re.compile(r'^package\.preload\[("(?:[^"\\]|\\.)*")\]=function\(\)\s*return assert\(loadstring\(("(?:[^"\\]|\\.)*"),("(?:[^"\\]|\\.)*")\)\)\(\)\s*end$', re.MULTILINE)
OBSERVER_DISABLED = """-- Public packs use baked walker assets; native-code capture stays disabled.
local PUBLIC_CAPTURE_ENABLED=false
if not PUBLIC_CAPTURE_ENABLED then
    return {readOnly=true,status='public_diagnostic_disabled',scaleWrites=0,
        started=false,nativeSpawnScaleEnabled=false}
end
"""

# All these Windows APIs only transform caller-owned strings. Their UTF-16
# conversion also supports non-ASCII account and Steam library paths.
PATH_HELPER = r'''local function absolute_path(value)
    assert(type(value)=='string' and #value>=4 and #value<=32700 and
        not value:find('[%z\1-\31<>"|?*]'),'CONTEXT_REFUSED: unsafe path')
    local path=value:gsub('/','\\')
    assert(path:match('^%a:\\') or path:match('^\\\\[^\\]+\\[^\\]+\\'),
        'CONTEXT_REFUSED: path must be absolute')
    assert(not path:match('^\\\\[%.%?]\\'),'CONTEXT_REFUSED: device path')
    for component in path:gmatch('[^\\]+') do
        assert(component~='.' and component~='..','CONTEXT_REFUSED: path traversal')
    end
    return path:gsub('\\+$','')
end
local function receipt_identity(gamePath)
    local ffi=require('ffi')
    ffi.cdef[[
        int __stdcall MultiByteToWideChar(unsigned int, unsigned long, const char *, int, unsigned short *, int);
        unsigned long __stdcall GetFullPathNameW(const unsigned short *, unsigned long, unsigned short *, unsigned short **);
        int __stdcall LCMapStringEx(const unsigned short *, unsigned long, const unsigned short *, int, unsigned short *, int, void *, void *, intptr_t);
        int __stdcall WideCharToMultiByte(unsigned int, unsigned long, const unsigned short *, int, char *, int, const char *, int *);
    ]]
    local kernel=ffi.load('kernel32')
    local path=absolute_path(gamePath)
    local length=kernel.MultiByteToWideChar(65001,8,path,#path,nil,0)
    assert(length>0 and length<32768,'CONTEXT_REFUSED: path encoding')
    local wide=ffi.new('unsigned short[?]',length+1)
    assert(kernel.MultiByteToWideChar(65001,8,path,#path,wide,length)==length,'CONTEXT_REFUSED: path conversion')
    local full=ffi.new('unsigned short[32768]')
    local count=tonumber(kernel.GetFullPathNameW(wide,32768,full,nil))
    assert(count>0 and count<32768,'CONTEXT_REFUSED: full path')
    local invariant=ffi.new('unsigned short[1]')
    local upperCount=kernel.LCMapStringEx(invariant,0x200,full,count,nil,0,nil,nil,0)
    assert(upperCount>0 and upperCount<32768,'CONTEXT_REFUSED: path case mapping')
    local upper=ffi.new('unsigned short[?]',upperCount)
    assert(kernel.LCMapStringEx(invariant,0x200,full,count,upper,upperCount,nil,nil,0)==upperCount,
        'CONTEXT_REFUSED: path case conversion')
    local bytes=kernel.WideCharToMultiByte(65001,0,upper,upperCount,nil,0,nil,nil)
    assert(bytes>0 and bytes<131072,'CONTEXT_REFUSED: receipt identity size')
    local buffer=ffi.new('char[?]',bytes)
    assert(kernel.WideCharToMultiByte(65001,0,upper,upperCount,buffer,bytes,nil,nil)==bytes,
        'CONTEXT_REFUSED: receipt identity conversion')
    return ffi.string(buffer,bytes)
end
'''


def require(ok: Any, message: str) -> None:
    if not ok:
        raise ValueError(message)


def pin(path: Path) -> dict[str, Any]:
    with path.open("rb") as handle:
        digest = hashlib.file_digest(handle, "sha256").hexdigest()
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": digest}


def load(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path: Path, value: Any) -> None:
    require(not path.exists(), "Fresh output required: " + str(path))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def module(name: str, path: Path) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    require(spec is not None and spec.loader is not None, "Primary tooling available")
    value = importlib.util.module_from_spec(spec)
    sys.modules[name] = value
    spec.loader.exec_module(value)
    return value


def unpack(data: bytes) -> tuple[tuple[int, ...], bytes]:
    require(struct.unpack_from("<3I", data) == (0xf0000011, 1, 1), "Exact singleton Lua archive")
    row = struct.unpack_from("<7Q6I", data, 104)
    require(row[1] == 0xa14e8dfa2cd117e2 and row[12] == 0 and row[8:10] == (0, 0), "Typed Lua key/companions")
    payload = data[row[2]:row[2] + row[7]]
    require(struct.unpack_from("<2I", payload) == (len(payload) - 8, 2), "Source-text Lua wrapper")
    return row, payload[8:]


def modules(body: str) -> dict[str, tuple[str, re.Match[str]]]:
    rows: dict[str, tuple[str, re.Match[str]]] = {}
    for found in MODULE.finditer(body):
        name, source, chunk = map(json.loads, found.groups())
        require(name == chunk and name not in rows, "Unique named embedded module")
        rows[name] = source, found
    return rows


def replace_once(body: str, before: str, after: str) -> str:
    require(body.count(before) == 1, "Exact context anchor: " + before[:100])
    return body.replace(before, after)


def portable_context(original: str) -> str:
    body = replace_once(original, "local M={}\n", "local M={}\n" + PATH_HELPER)
    body = replace_once(body, "    assert(config.ManifestUrl==spec.manifestUrl,'CONTEXT_REFUSED: unreviewed feed')", "    assert(config.ManifestUrl==nil or config.ManifestUrl=='' or config.ManifestUrl==spec.manifestUrl,\n        'CONTEXT_REFUSED: unreviewed feed')")
    body = replace_once(body, "    assert(config.InstalledGamePath==spec.gamePath and receipt.GameDir==spec.gamePath,\n        'CONTEXT_REFUSED: game path')", "    absolute_path(config.InstalledGamePath)\n    assert(config.InstalledGamePath==receipt.GameDir,'CONTEXT_REFUSED: game path')")
    body = replace_once(body, "    local config=document(spec.configPath,1024*1024)\n    local receipt=document(spec.receiptPath,8*1024*1024)\n    local plan=io.open(spec.pendingPath,'rb')", "    local appData=absolute_path(assert(os.getenv('APPDATA'),'CONTEXT_UNAVAILABLE: APPDATA'))\n    local root=appData..'/Clonedivers'\n    local config=document(root..'/config.json',1024*1024)\n    local gamePath=absolute_path(config.InstalledGamePath)\n    local identity=runtime.sha256(receipt_identity(gamePath)):lower()\n    assert(#identity==64 and not identity:find('[^%x]'),'CONTEXT_REFUSED: receipt identity')\n    local receipt=document(root..'/receipts/'..identity..'.json',8*1024*1024)\n    local plan=io.open(root..'/apply-plan.json','rb')")
    body = replace_once(body, "io.open(spec.gamePath..'/data/'..observed[main],'rb')", "io.open(gamePath..'/data/'..observed[main],'rb')")
    return body


def portable_spec(original: str) -> str:
    body = original.replace("2026.10.08-deepqa-private", VERSION).replace("http://127.0.0.1:18188/manifest.json", OFFICIAL_FEED)
    for name in ("gamePath", "configPath", "receiptPath", "pendingPath"):
        pattern = re.compile(r'\["' + name + r'"\]="(?:[^"\\]|\\.)*",')
        require(len(pattern.findall(body)) == 1, "One machine path in spec: " + name)
        body = pattern.sub("", body)
    return body


def disable_capture_profile(original: str) -> str:
    pattern = re.compile(r',\["outputRoot"\]="(?:[^"\\]|\\.)*"')
    require(len(pattern.findall(original)) == 1, "Exact developer capture output path")
    return pattern.sub("", original)


def disable_capture_guard(original: str) -> str:
    pattern = re.compile(r"    assert\(p\.outputRoot:match\('[^']+'\),\n        '[^']+'\)")
    require(len(pattern.findall(original)) == 1, "Exact capture output guard")
    return pattern.sub("    assert(false,'OBSERVATION_REFUSED: native-code capture disabled in public releases')", original)


def promote(label: str, key: str, writer: Any, runner: Any) -> dict[str, Any]:
    recipe = load(SOURCE / "recipe.json")
    source = next(item for item in recipe["sources"] if key in item["resources"])
    old = source["files"][0]
    old_path = SOURCE / "assets" / old["sha256"]
    require(pin(old_path)["sha256"] == old["sha256"], "Sealed addon hash")
    row, raw = unpack(old_path.read_bytes())
    original = raw.decode("utf-8")
    original_modules = modules(original)
    prefix = "codex_empire_vehicles" if label == "eagle" else "codex_empire_scale_observer"
    edits = {prefix + "/context": portable_context(original_modules[prefix + "/context"][0]),
             prefix + "/context_spec": portable_spec(original_modules[prefix + "/context_spec"][0])}
    if label == "observer":
        edits[prefix + "/capture_profile"] = disable_capture_profile(original_modules[prefix + "/capture_profile"][0])
        edits[prefix + "/observer"] = disable_capture_guard(original_modules[prefix + "/observer"][0])
    body = original
    changed = []
    for name, content in edits.items():
        before, match = original_modules[name]
        after = "package.preload[" + json.dumps(name) + "]=function()return assert(loadstring(" + json.dumps(content, ensure_ascii=False) + "," + json.dumps(name) + "))()end"
        body = replace_once(body, match.group(0), after)
        changed.append({"name": name, "oldSourceSha256": hashlib.sha256(before.encode()).hexdigest(), "newSourceSha256": hashlib.sha256(content.encode()).hexdigest(), "oldStatement": match.group(0), "newStatement": after})
    if label == "observer":
        first_line = original.splitlines(keepends=True)[0]
        body = replace_once(body, first_line, first_line + OBSERVER_DISABLED)
        changed.append({"name": "public-observer-disabled-entrypoint", "oldStatement": first_line, "newStatement": first_line + OBSERVER_DISABLED})
    reverse = body
    for change in reversed(changed):
        reverse = replace_once(reverse, change["newStatement"], change["oldStatement"])
    require(reverse == original, "Every byte outside portable context modules exact")
    current_modules = modules(body)
    require(current_modules.keys() == original_modules.keys(), "Embedded module set exact")
    require(all(current_modules[name][0] == value[0] for name, value in original_modules.items() if name not in edits), "Gateway/observer/nativewrite logic exact")
    for forbidden in ("C:/Users/goode", "C:\\\\Users\\\\goode", "127.0.0.1", "deepqa-private", "c0aed7986f61bbef8226eba430050eb5cb461139240c54593c2b875abab943a6"):
        require(forbidden not in body, "No host context: " + forbidden)
    require(runner.execute(("assert(loadstring(" + json.dumps(body, ensure_ascii=False) + "));return 'PASS'").encode()) == b"PASS", "Whole addon syntax")
    folder = OUTPUT / label
    folder.mkdir(parents=True)
    (folder / "addon.lua").write_text(body, encoding="utf-8")
    for name, content in edits.items():
        (folder / (name.rsplit("/", 1)[-1] + ".lua")).write_text(content, encoding="utf-8")
    archive = writer.make_archive({row[0]: writer.lua_resource(body.encode())})
    new_row, new_raw = unpack(archive)
    require(new_row[:2] == row[:2] and new_raw == body.encode(), "Round trip archive")
    files = []
    for suffix, data in zip(SUFFIXES, (archive, b"", b""), strict=True):
        file = folder / ("9ba626afa44a3aa3.patch_0" + suffix)
        file.write_bytes(data)
        files.append(pin(file))
    return {"label": label, "typedKey": key, "oldSha256": old["sha256"], "oldSize": old["size"], "newSha256": files[0]["sha256"], "newSize": files[0]["bytes"], "path": files[0]["path"], "files": files, "oldArchive": pin(old_path), "addon": pin(folder / "addon.lua"), "context": pin(folder / "context.lua"), "contextSpec": pin(folder / "context_spec.lua"), "changes": changed, "otherEmbeddedModulesExact": True, "allBytesOutsideExplicitChangesExact": True, "gates": source["actualGates"], "publicAutomaticCaptureDisabled": label == "observer"}


def fixtures(source: dict[str, Any], runner: Any) -> dict[str, Any]:
    """Exercise actual packaged context with synthetic files and real path APIs."""
    body = Path(source["addon"]["path"]).read_text(encoding="utf-8")
    embedded = modules(body)
    prefix = "codex_empire_vehicles" if source["label"] == "eagle" else "codex_empire_scale_observer"
    main_sha = "fb7b316e34c8466e025b1ce9d18eb13a5b5b079dc57a06e0c6d7dac21d7bc8c2"
    all_assets = load(ROOT / "dist/empire-deep-qa-2026-10-08/qa/all-assets-v1.json")["assets"]
    main_pin = all_assets[main_sha] if isinstance(all_assets, dict) else next(item for item in all_assets if item["sha256"] == main_sha)
    main_file = Path(main_pin["path"])
    require(pin(main_file)["sha256"] == main_sha, "Fresh exact aircraft main fixture")
    config = {"InstalledPackVersion": VERSION, "ManifestUrl": None, "InstalledGameBuild": "25480438", "InstalledGamePath": "E:\\Steam Library\\steamapps\\common\\Helldivers 2", "TextureProfile": "full", "InstalledTextureProfile": "full", "Options": {"empire": True, "commandos": True, "droids": True, "aimpoints": True, "covenant": False}, "LastVerifiedUtc": "2026-10-08T22:42:37Z"}
    receipt = {"Format": 1, "PackVersion": VERSION, "GameBuild": "25480438", "GameDir": config["InstalledGamePath"], "TextureProfile": "full", "TargetActive": True, "VerifiedUtc": config["LastVerifiedUtc"], "Options": copy.deepcopy(config["Options"]), "Files": [{"File": {"Name": "9ba626afa44a3aa3.patch_380", "Sha256": main_sha, "Size": 152400}}, {"File": {"Name": "9ba626afa44a3aa3.patch_380.gpu_resources", "Sha256": "18096c55af1459c5508b689f2223895267fc5473ee892bc12abac3253fca4456", "Size": 290643200}}]}
    preloads = []
    for name in (prefix + "/json", prefix + "/context_spec", prefix + "/context"):
        preloads.append("package.preload[" + json.dumps(name) + "]=function()return assert(loadstring(" + json.dumps(embedded[name][0], ensure_ascii=False) + "))()end")
    for name in ("metrics", "windows_ffi", "windows_readonly"):
        content = (SDK_ROOT / "runtime" / (name + ".lua")).read_text(encoding="utf-8")
        preloads.append("package.preload[" + json.dumps("hd2runtime/runtime/" + name) + "]=function()return assert(loadstring(" + json.dumps(content) + "))()end")
    fixture = r'''
local json=require(PREFIX..'/json')
local Context=require(PREFIX..'/context')
local spec=require(PREFIX..'/context_spec')
local runtime=require('hd2runtime/runtime/windows_readonly')()
-- Native process/image reads and directory creation are forbidden in fixtures.
runtime.read=function()error('unexpected native read')end
runtime.module_hash=function()error('unexpected native image read')end
runtime.mkdir=function()error('unexpected directory creation')end
local realOpen=io.open
local file=assert(realOpen(MAIN_FILE,'rb'));local mainBytes=file:read('*a');file:close()
local baseConfig=json.decode(CONFIG_JSON)
local baseReceipt=json.decode(RECEIPT_JSON)
local appData='D:\Users\Jules\AppData\Roaming'
local config,receipt,pending,corruptMain,missingReceipt,missingConfig
local reads,writes,checks={},0,{}
local function norm(path)return path:gsub('/','\'):gsub('\+$','')end
local expectedIdentity=runtime.sha256(baseConfig.InstalledGamePath:upper()):lower()
local function reset()
 config=json.decode(CONFIG_JSON);receipt=json.decode(RECEIPT_JSON)
 pending=false;corruptMain=false;missingReceipt=false;missingConfig=false;reads={}
 appData='D:\Users\Jules\AppData\Roaming'
 expectedIdentity=runtime.sha256(baseConfig.InstalledGamePath:upper()):lower()
end
os.getenv=function(name)assert(name=='APPDATA');return appData end
io.open=function(path,mode)
 assert(mode=='rb','write refused');reads[#reads+1]=path
 local content
 local root=norm(appData)..'\Clonedivers'
 local normalized=norm(path)
 if normalized==root..'\config.json' then
  if missingConfig then return nil end
  content=json.encode(config)
 elseif normalized==root..'\receipts\'..expectedIdentity..'.json' then
  if missingReceipt then return nil end
  content=json.encode(receipt)
 elseif normalized==root..'\apply-plan.json' then
  if not pending then return nil end
  content='{}'
 elseif normalized==norm(config.InstalledGamePath)..'\data\9ba626afa44a3aa3.patch_380' then
  content=corruptMain and ('X'..mainBytes:sub(2))or mainBytes
 else error('unexpected fixture path: '..path)end
 return {read=function(_,n)return n=='*a' and content or content:sub(1,n)end,close=function()end}
end
local function check(name,mutation,allowed)
 reset();if mutation then mutation()end
 local ok,value=pcall(Context.require,runtime)
 assert(ok==allowed,name..': '..tostring(value))
 if allowed then
  assert(value[spec.mainSha256]=='9ba626afa44a3aa3.patch_380')
  assert(#reads==4,'public context must read only config, receipt, pending and main')
 end
 checks[#checks+1]={name=name,passed=true,accepted=ok,fileReads=#reads}
end
check('different-user-and-steam-library-public-null-feed',nil,true)
check('public-empty-feed',function()config.ManifestUrl=''end,true)
check('official-feed',function()config.ManifestUrl=spec.manifestUrl end,true)
check('unicode-user-and-game-path',function()
 appData='D:\Users\Zoë\AppData\Roaming'
 config.InstalledGamePath='F:\Steam\ÄLPHA\Helldivers 2';receipt.GameDir=config.InstalledGamePath
 expectedIdentity=runtime.sha256('F:\STEAM\ÄLPHA\HELLDIVERS 2'):lower()
end,true)
check('forward-slash-game-path',function()
 config.InstalledGamePath='F:/SteamLibrary/common/Helldivers 2';receipt.GameDir=config.InstalledGamePath
 expectedIdentity=runtime.sha256('F:\STEAMLIBRARY\COMMON\HELLDIVERS 2'):lower()
end,true)
check('foreign-feed',function()config.ManifestUrl='https://example.org/manifest.json'end,false)
check('private-local-feed',function()config.ManifestUrl='http://127.0.0.1:18188/manifest.json'end,false)
check('wrong-pack-version',function()config.InstalledPackVersion='wrong'end,false)
check('wrong-receipt-version',function()receipt.PackVersion='wrong'end,false)
check('wrong-game-build',function()config.InstalledGameBuild='other'end,false)
check('wrong-receipt-build',function()receipt.GameBuild='other'end,false)
check('mismatched-game-path',function()receipt.GameDir='F:\Other'end,false)
check('relative-game-path',function()config.InstalledGamePath='relative';receipt.GameDir='relative'end,false)
check('traversal-game-path',function()config.InstalledGamePath='E:\Steam\..\other';receipt.GameDir=config.InstalledGamePath end,false)
check('nul-game-path',function()config.InstalledGamePath='E:\Steam\'..string.char(0)..'other';receipt.GameDir=config.InstalledGamePath end,false)
check('device-game-path',function()config.InstalledGamePath='\\?\E:\Steam';receipt.GameDir=config.InstalledGamePath end,false)
check('relative-appdata',function()appData='relative'end,false)
check('traversal-appdata',function()appData='D:\Users\..\Other'end,false)
check('not-empire',function()config.Options.empire=false;receipt.Options.empire=false end,false)
check('mismatched-options',function()receipt.Options.commandos=false end,false)
check('nonboolean-options',function()config.Options.commandos='true';receipt.Options.commandos='true'end,false)
check('lighter-textures',function()config.TextureProfile='lighter';config.InstalledTextureProfile='lighter';receipt.TextureProfile='lighter'end,false)
check('receipt-profile-mismatch',function()receipt.TextureProfile='lighter'end,false)
check('stale-receipt',function()receipt.VerifiedUtc='old'end,false)
check('inactive-receipt',function()receipt.TargetActive=false end,false)
check('invalid-receipt-format',function()receipt.Format=2 end,false)
check('missing-receipt',function()missingReceipt=true end,false)
check('missing-config',function()missingConfig=true end,false)
check('pending-installation',function()pending=true end,false)
check('missing-closure-asset',function()table.remove(receipt.Files,2)end,false)
check('duplicate-closure-asset',function()receipt.Files[3]=receipt.Files[1]end,false)
check('wrong-closure-size',function()receipt.Files[1].File.Size=42 end,false)
check('unsafe-receipt-filename',function()receipt.Files[1].File.Name='../outside'end,false)
check('changed-package-main',function()corruptMain=true end,false)
return json.encode({passed=true,checks=checks,filesystemWrites=writes,realWindowsPathApis=true,
 realWindowsBcryptSha256=true,syntheticContextOnly=true,realEngineOpened=false})
'''
    # The template uses readable Windows separators; quote them for Lua source.
    program = "\n".join(preloads) + "\n" + fixture.replace("\\", "\\\\")
    for before, value in (("PREFIX", prefix), ("MAIN_FILE", str(main_file.resolve())), ("CONFIG_JSON", json.dumps(config)), ("RECEIPT_JSON", json.dumps(receipt))):
        program = program.replace(before, json.dumps(value, ensure_ascii=False))
    path = OUTPUT / source["label"] / "context-fixtures.lua"
    path.write_text(program, encoding="utf-8")
    result = json.loads(runner.execute(program.encode("utf-8")))
    require(result["passed"] and len(result["checks"]) == 34 and result["filesystemWrites"] == 0, "All portable context fixtures passed")
    if source["label"] == "observer":
        inert = "require=function()error('disabled observer may not require')end\nio.open=function()error('disabled observer may not read/write files')end\nos.getenv=function()error('disabled observer may not read env')end\nlocal value=(function()\n" + body + "\nend)();assert(value.status=='public_diagnostic_disabled' and value.scaleWrites==0 and value.started==false and value.nativeSpawnScaleEnabled==false);return 'PASS'"
        inert_path = OUTPUT / "observer/disabled-fixture.lua"
        inert_path.write_text(inert, encoding="utf-8")
        require(runner.execute(inert.encode()) == b"PASS", "Public observer executes without requires, env or file I/O")
        result["disabledObserver"] = {"program": pin(inert_path), "executed": True, "requiresDenied": True, "fileIoDenied": True, "environmentReadsDenied": True, "started": False, "scaleWrites": 0}
    return {"program": pin(path), "mainFixture": pin(main_file), "result": result}


def run() -> None:
    require(not OUTPUT.exists(), "Fresh runtime output")
    require(pin(SOURCE / "manifest.json")["sha256"] == SOURCE_SHA, "Sealed reviewed candidate")
    writer = module("public_r21_writer", SDK_ROOT / "sdk/tools/hd2_archive.py")
    runner = module("public_r21_runner", SDK_ROOT / "sdk/tools/lua_runner.py")
    OUTPUT.mkdir(parents=True)
    sources = [promote(label, key, writer, runner) for label, key in (("eagle", "6d133274694b50a9.a14e8dfa2cd117e2"), ("observer", "ea6b75393ee0b75d.a14e8dfa2cd117e2"))]
    proofs = {item["label"]: fixtures(item, runner) for item in sources}
    report = {"passed": True, "sourceManifest": pin(SOURCE / "manifest.json"), "tool": pin(Path(__file__)), "sources": sources, "fixtures": proofs, "publicVersion": VERSION, "officialFeed": OFFICIAL_FEED, "nativeWriterLogicExact": True, "publicObserverAutomaticCaptureDisabled": True, "primaryArchiveWriter": pin(SDK_ROOT / "sdk/tools/hd2_archive.py"), "primaryLuaRunner": pin(SDK_ROOT / "sdk/tools/lua_runner.py"), "offlineLuaDll": pin(Path(runner.default_dll())), "gameOrSettingsChanged": False, "runtimeAccepted": False}
    save(OUTPUT / "report.json", report)
    print(json.dumps(pin(OUTPUT / "report.json")))


def main() -> int:
    argparse.ArgumentParser(description=__doc__.splitlines()[0]).parse_args()
    logging.basicConfig(level=logging.INFO, format="%(levelname)s: %(message)s")
    try:
        run()
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, RuntimeError):
        logging.exception("Public runtime promotion failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
