#!/usr/bin/env python3
"""Qualify the r22 Eagle runtime for matching Full and Lighter installations.

Only the reviewed Eagle context profile policy and pack version change. The
public observer remains byte exact and inert. No game or settings are modified.
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
SOURCE = ROOT / "dist/production-r21-2026-10-08/release-ready-v1"
SOURCE_SHA = "55d5297988c0b134eef4738edf81026ebbbe688c917ad97c6170ff7ef1b59c2e"
R21_RUNTIME = ROOT / "dist/production-r21-2026-10-08/runtime-v1/report.json"
R21_RUNTIME_SHA = "96170a1aff3a125aa498ab8514731918ae4387a76dd5bd31996e81f9ff5f522c"
CLOSURE = ROOT / "dist/empire-offline-next-2026-10-08/lighter/closure-v1.json"
CLOSURE_SHA = "78185f5941b6830c7f4965e56318d7d82d262841268d6dc8e1a3d1ef7db9301c"
OUTPUT = ROOT / "dist/production-r22-2026-10-08/runtime-v1"
SDK_ROOT = ROOT / "dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d"
VERSION = "2026.10.08-r22"
OFFICIAL_FEED = "https://raw.githubusercontent.com/owendavidgoode/clonedivers/main/manifest-v3-current.json"
SUFFIXES = ("", ".stream", ".gpu_resources")
MODULE = re.compile(r'^package\.preload\[("(?:[^"\\]|\\.)*")\]=function\(\)\s*return assert\(loadstring\(("(?:[^"\\]|\\.)*"),("(?:[^"\\]|\\.)*")\)\)\(\)\s*end$', re.MULTILINE)


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


def matching_profile_context(original: str) -> str:
    return replace_once(original,
        "    assert(config.TextureProfile=='full' and config.InstalledTextureProfile=='full' and receipt.TextureProfile=='full',\n"
        "        'CONTEXT_REFUSED: only independently checked Full closure is enabled')",
        "    local profile=config.TextureProfile\n"
        "    assert((profile=='full' or profile=='lighter') and config.InstalledTextureProfile==profile and\n"
        "        receipt.TextureProfile==profile,'CONTEXT_REFUSED: reviewed texture profile agreement required')")


def portable_spec(original: str) -> str:
    return replace_once(original, '"2026.10.08-r21"', json.dumps(VERSION))


def promote(label: str, key: str, writer: Any, runner: Any) -> dict[str, Any]:
    require(label == "eagle" and key == "6d133274694b50a9.a14e8dfa2cd117e2", "Only Eagle changes in r22")
    source = next(item for item in load(R21_RUNTIME)["sources"] if item["typedKey"] == key)
    old_sha = "f34d73c5d3e111bc935cc8f690b9b92a20fc910e8997558584560f3addc3e270"
    old_path = Path(source["path"])
    require(pin(old_path)["sha256"] == source["newSha256"] == old_sha, "Sealed public Eagle addon hash")
    logical_rows = [row for row in load(SOURCE / "manifest.json")["pack"]["files"] if row["sha256"] == old_sha]
    require(len(logical_rows) == 1 and logical_rows[0]["size"] == source["newSize"], "Exact public Eagle row")
    row, raw = unpack(old_path.read_bytes())
    original = raw.decode("utf-8")
    original_modules = modules(original)
    prefix = "codex_empire_vehicles"
    edits = {prefix + "/context": matching_profile_context(original_modules[prefix + "/context"][0]),
             prefix + "/context_spec": portable_spec(original_modules[prefix + "/context_spec"][0])}
    body = original
    changed = []
    for name, content in edits.items():
        before, match = original_modules[name]
        after = "package.preload[" + json.dumps(name) + "]=function()return assert(loadstring(" + json.dumps(content, ensure_ascii=False) + "," + json.dumps(name) + "))()end"
        body = replace_once(body, match.group(0), after)
        changed.append({"name": name, "oldSourceSha256": hashlib.sha256(before.encode()).hexdigest(), "newSourceSha256": hashlib.sha256(content.encode()).hexdigest(), "oldStatement": match.group(0), "newStatement": after})
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
    restrictions = []
    for name, (text, _) in current_modules.items():
        for number, line in enumerate(text.splitlines(), 1):
            if re.search(r"TextureProfile|textureProfile|['\"](?:full|lighter)['\"]", line):
                restrictions.append({"module": name, "line": number, "text": line})
    require({row["module"] for row in restrictions} == {prefix + "/context"}, "No other Full-only profile gates")
    return {"label": label, "typedKey": key, "oldSha256": old_sha, "oldSize": source["newSize"], "newSha256": files[0]["sha256"], "newSize": files[0]["bytes"], "path": files[0]["path"], "files": files, "oldArchive": pin(old_path), "sourceLogicalRow": logical_rows[0], "addon": pin(folder / "addon.lua"), "context": pin(folder / "context.lua"), "contextSpec": pin(folder / "context_spec.lua"), "changes": changed, "otherEmbeddedModulesExact": True, "allBytesOutsideExplicitChangesExact": True, "gates": source["gates"], "profilePolicyReferences": restrictions, "onlyProfileAgreementAndVersionChanged": True}


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
check('lighter-textures',function()config.TextureProfile='lighter';config.InstalledTextureProfile='lighter';receipt.TextureProfile='lighter'end,true)
check('receipt-profile-mismatch',function()receipt.TextureProfile='lighter'end,false)
for _,requested in ipairs({'full','lighter'}) do
 for _,installed in ipairs({'full','lighter'}) do
  for _,recorded in ipairs({'full','lighter'}) do
   if not(requested==installed and requested==recorded) and
      not(requested=='full' and installed=='full' and recorded=='lighter') then
    check('crossed-profiles-'..requested..'-'..installed..'-'..recorded,function()
     config.TextureProfile=requested;config.InstalledTextureProfile=installed;receipt.TextureProfile=recorded
    end,false)
   end
  end
 end
end
for _,field in ipairs({'requested','installed','receipt'}) do
 for _,kind in ipairs({'missing','empty','unknown','case'}) do
  check(kind..'-'..field..'-profile',function()
   local value=kind=='missing' and nil or kind=='empty' and '' or kind=='unknown' and 'other' or 'Full'
   if kind=='missing' then value=nil end
   if field=='requested' then config.TextureProfile=value
   elseif field=='installed' then config.InstalledTextureProfile=value
   else receipt.TextureProfile=value end
  end,false)
 end
end
for _,kind in ipairs({'empty-feed','official-feed','unicode-path','forward-slash-path'}) do
 check('lighter-'..kind,function()
  config.TextureProfile='lighter';config.InstalledTextureProfile='lighter';receipt.TextureProfile='lighter'
  if kind=='empty-feed' then config.ManifestUrl=''
  elseif kind=='official-feed' then config.ManifestUrl=spec.manifestUrl
  elseif kind=='unicode-path' then
   appData='D:\Users\Zoë\AppData\Roaming'
   config.InstalledGamePath='F:\Steam\ÄLPHA\Helldivers 2';receipt.GameDir=config.InstalledGamePath
   expectedIdentity=runtime.sha256('F:\STEAM\ÄLPHA\HELLDIVERS 2'):lower()
  else
   config.InstalledGamePath='F:/SteamLibrary/common/Helldivers 2';receipt.GameDir=config.InstalledGamePath
   expectedIdentity=runtime.sha256('F:\STEAMLIBRARY\COMMON\HELLDIVERS 2'):lower()
  end
 end,true)
end
check('old-public-r21-version',function()config.InstalledPackVersion='2026.10.08-r21';receipt.PackVersion='2026.10.08-r21'end,false)
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
    require(result["passed"] and len(result["checks"]) == 56 and result["filesystemWrites"] == 0, "All portable context fixtures passed")
    return {"program": pin(path), "mainFixture": pin(main_file), "result": result}


def unchanged_observer(runner: Any) -> dict[str, Any]:
    source = next(item for item in load(R21_RUNTIME)["sources"] if item["label"] == "observer")
    archive = Path(source["path"])
    archive_pin = pin(archive)
    require(archive_pin["sha256"] == source["newSha256"] ==
            "39ed19f63a36c14dbc135cc63a5d4c1ab29b6328976466a12330430ff715a2e3", "Exact inert public observer")
    row, raw = unpack(archive.read_bytes())
    require(row[:2] == (0xea6b75393ee0b75d, 0xa14e8dfa2cd117e2), "Exact observer typed identity")
    body = raw.decode("utf-8")
    require(body.startswith("-- HD2-Addon:") and "local PUBLIC_CAPTURE_ENABLED=false" in body.split("package.preload", 1)[0],
            "Observer returns before any module registration")
    inert = "require=function()error('disabled observer may not require')end\nio.open=function()error('disabled observer may not read/write files')end\nos.getenv=function()error('disabled observer may not read env')end\nlocal value=(function()\n" + body + "\nend)();assert(value.status=='public_diagnostic_disabled' and value.scaleWrites==0 and value.started==false and value.nativeSpawnScaleEnabled==false);return 'PASS'"
    inert_path = OUTPUT / "observer-disabled-fixture.lua"
    inert_path.write_text(inert, encoding="utf-8")
    require(runner.execute(inert.encode()) == b"PASS", "Public observer executes without requires, env or file I/O")
    return {"archive": archive_pin, "typedKey": "ea6b75393ee0b75d.a14e8dfa2cd117e2", "gates": source["gates"],
            "program": pin(inert_path), "executed": True, "requiresDenied": True, "fileIoDenied": True,
            "environmentReadsDenied": True, "started": False, "scaleWrites": 0, "retainedByteExact": True,
            "unreachableContextSpecVersion": "2026.10.08-r21"}


def run() -> None:
    require(not OUTPUT.exists(), "Fresh runtime output")
    require(pin(SOURCE / "manifest.json")["sha256"] == SOURCE_SHA, "Sealed reviewed candidate")
    require(pin(R21_RUNTIME)["sha256"] == R21_RUNTIME_SHA, "Sealed public runtime derivation")
    require(pin(CLOSURE)["sha256"] == CLOSURE_SHA and load(CLOSURE)["passed"], "Qualified exact Lighter geometry closure")
    writer = module("public_r22_writer", SDK_ROOT / "sdk/tools/hd2_archive.py")
    runner = module("public_r22_runner", SDK_ROOT / "sdk/tools/lua_runner.py")
    OUTPUT.mkdir(parents=True)
    sources = [promote("eagle", "6d133274694b50a9.a14e8dfa2cd117e2", writer, runner)]
    proofs = {item["label"]: fixtures(item, runner) for item in sources}
    observer = unchanged_observer(runner)
    report = {"passed": True, "meaningOfPassed": "Offline context and exact native guard preservation only; gameplay remains unaccepted",
              "sourceManifest": pin(SOURCE / "manifest.json"), "sourceRuntime": pin(R21_RUNTIME), "lighterClosure": pin(CLOSURE),
              "tool": pin(Path(__file__)), "sources": sources, "fixtures": proofs, "publicVersion": VERSION, "officialFeed": OFFICIAL_FEED,
              "profilePolicy": {"accepted": ["full", "lighter"], "allThreeMustMatchExactly": ["requested", "installed", "receipt"], "defaultsAllowed": False},
              "nativeWriterLogicExact": True, "buildNativeFingerprintSoloShipReceiptOptionsAssetClosureMainHashWriteGuardsExact": True,
              "publicObserverAutomaticCaptureDisabled": True, "unchangedObserver": observer,
              "primaryArchiveWriter": pin(SDK_ROOT / "sdk/tools/hd2_archive.py"), "primaryLuaRunner": pin(SDK_ROOT / "sdk/tools/lua_runner.py"),
              "offlineLuaDll": pin(Path(runner.default_dll())), "gameOrSettingsChanged": False, "runtimeAccepted": False}
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
