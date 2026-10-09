#!/usr/bin/env python3
"""Read-only installed Yoda context proof without running the addon or engine API."""
from __future__ import annotations

import argparse
from collections.abc import Sequence
import hashlib
import importlib.util
import json
import logging
import os
from pathlib import Path
import re
import struct
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
AREA = ROOT / "dist/empire-yoda-death-2026-10-08/runtime"
CANDIDATE = AREA / "candidate-v4/report.json"
CANDIDATE_SHA = "204e10bfbdca5085b7518d051fc6b0242c8c07e348bc9a32f11861506f355cf3"
SDK = ROOT / "dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d"
CAMERA = Path("C:/Users/goode/AppData/Local/CowboyBingus/Helldivers2/Logs/ModOptionsMenu.values")
MODULE = re.compile(r'^package\.preload\[("(?:[^"\\]|\\.)*")\]=function\(\)return assert\(loadstring\(("(?:[^"\\]|\\.)*"),("(?:[^"\\]|\\.)*")\)\)\(\)end$', re.MULTILINE)


def sha(path: Path) -> str:
    with path.open("rb") as handle:
        return hashlib.file_digest(handle, "sha256").hexdigest()


def pin(path: Path) -> dict[str, Any]:
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": sha(path)}


def load(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def require(ok: Any, message: str) -> None:
    if not ok:
        raise ValueError(message)


def checked(expected: dict[str, Any]) -> Path:
    path = Path(expected["path"])
    require(pin(path) == expected, "Frozen input changed: " + str(path))
    return path


def unpack(path: Path, key: str) -> str:
    data = path.read_bytes()
    identity, kind = (int(item, 16) for item in key.split("."))
    require(struct.unpack_from("<III", data) == (0xF0000011, 1, 1), "Singleton source addon required")
    require(struct.unpack_from("<Q", data, 32)[0] == len(data), "Complete archive extent")
    row = struct.unpack_from("<7Q6I", data, 104)
    require(row[:2] == (identity, kind) and row[2] % 16 == 0 and not row[8] and not row[9], "Typed directory/alignment/companions")
    payload = data[row[2]:row[2] + row[7]]
    require(struct.unpack_from("<II", payload) == (len(payload) - 8, 2), "Source Lua wrapper")
    return payload[8:].decode("utf-8")


def run(manifest: Path, manifest_sha: str, output: Path) -> dict[str, Any]:
    output = output.resolve()
    require(output.is_relative_to((ROOT / "dist").resolve()) and not output.exists(), "Fresh workspace dist output required")
    require(sha(CANDIDATE) == CANDIDATE_SHA, "Qualified candidate changed")
    proof = load(CANDIDATE)
    require(proof["passed"] and not proof["runtimeAccepted"] and proof["startupHashOnly"], "Qualified offline candidate required")
    require(re.fullmatch(r"[a-f0-9]{64}", manifest_sha) and sha(manifest) == manifest_sha, "Sealed target manifest pin required")
    target = load(manifest)
    require(target["pack"]["version"] == "2026.10.08-r23", "Expected release version")
    appdata = Path(os.environ["APPDATA"])
    settings_path = appdata / "Clonedivers/config.json"
    settings = load(settings_path)
    require(settings["InstalledPackVersion"] == "2026.10.08-r23" and settings["InstalledGameBuild"] == "25480438", "Current release/build not installed")
    require(settings.get("ManifestUrl") in (None, "", proof["contextSpec"]["manifestUrl"]), "Official feed required")
    require(settings["TextureProfile"] in ("full", "lighter") and settings["TextureProfile"] == settings["InstalledTextureProfile"], "Profile agreement required")
    game = Path(settings["InstalledGamePath"])
    require(game.is_absolute() and game.is_dir(), "Actual game directory required")
    # This PC's ASCII Steam path has exactly the .NET invariant-uppercase identity.
    require(str(game).isascii(), "This local probe's Python receipt lookup is deliberately ASCII-only; Lua remains Unicode-capable")
    identity = hashlib.sha256(str(game).replace("/", "\\").upper().encode()).hexdigest()
    receipt_path = settings_path.parent / "receipts" / (identity + ".json")
    receipt = load(receipt_path)
    main_pin = proof["files"][0]
    matches = [row["File"] for row in receipt["Files"] if row["File"]["Sha256"] == main_pin["sha256"]]
    require(len(matches) == 1 and re.fullmatch(r"[a-f0-9]{16}\.patch_\d+", matches[0]["Name"]), "Exact installed Yoda addon row required")
    installed = game / "data" / matches[0]["Name"]
    require(installed.stat().st_size == main_pin["bytes"] and sha(installed) == main_pin["sha256"], "Installed addon bytes changed")
    source = unpack(installed, proof["typedKeys"][0])
    require(source == checked(proof["addon"]).read_text(), "Installed addon differs from qualified source")
    embedded = {json.loads(m[1]): json.loads(m[2]) for m in MODULE.finditer(source)}
    require(len(embedded) == 8, "Expected exact eight private modules")
    selected = ("json", "context_spec", "context")
    preloads = []
    for name in selected:
        key = "codex_lego_yoda/" + name
        require(embedded[key] == checked(proof["modules"][name]).read_text(), "Installed private context source differs")
        preloads.append(f"package.preload[{json.dumps(key)}]=function()return assert(loadstring({json.dumps(embedded[key])}))()end")
    primary_inputs = []
    for name in ("metrics", "windows_ffi", "windows_readonly"):
        path = SDK / "runtime" / (name + ".lua")
        primary_inputs.append(pin(path))
        key = "hd2runtime/runtime/" + name
        preloads.append(f"package.preload[{json.dumps(key)}]=function()return assert(loadstring({json.dumps(path.read_text())}))()end")
    watched = [settings_path, receipt_path, manifest, CANDIDATE, installed]
    if CAMERA.is_file():
        watched.append(CAMERA)
    before = {str(path.resolve()): sha(path) for path in watched}
    script = "\n".join(preloads) + r'''
local json=require('codex_lego_yoda/json')
local Context=require('codex_lego_yoda/context')
local readonly=require('hd2runtime/runtime/windows_readonly')()
local runtime={sha256=readonly.sha256}
local reads={}
local originalOpen=io.open
io.open=function(path,mode)
 assert(mode=='rb','Installed context probe refuses writes')
 reads[#reads+1]=path;return originalOpen(path,mode)
end
io.output=function()error('No output files permitted')end
io.write=function()error('No writes permitted')end
io.popen=function()error('No processes permitted')end
os.execute=function()error('No processes permitted')end
os.remove=function()error('No removal permitted')end
os.rename=function()error('No moves permitted')end
local observed=Context.require(runtime)
local count=0;for _ in pairs(observed)do count=count+1 end
assert(count==5 and #reads==4,'Expected five receipt closure assets and startup config/receipt/pending/audio reads')
local startupReads=#reads
assert(Context.recheck(runtime))
assert(#reads==startupReads+1,'Metadata recheck may open pending plan only')
return json.encode({passed=true,closureAssets=count,startupReadOnlyFileOpens=startupReads,
 cachedRecheckReadOnlyFileOpens=#reads-startupReads,actualInstalledContext=true,
 syntheticContext=false,realWindowsReceiptIdentity=true,realWindowsBcryptSha256=true,
 realWindowsFileMetadata=true,lastAccessTimeExcluded=true,engineEntryPointRun=false,
 nativeEngineOpened=false,nativeProcessMemoryRead=false,nativeWrites=0})
'''
    runner_path = SDK / "sdk/tools/lua_runner.py"
    spec = importlib.util.spec_from_file_location("yoda_installed_lua_runner", runner_path)
    require(spec and spec.loader, "Reviewed standalone Lua runner required")
    runner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runner)
    dll = game / "bin/lua51.dll"
    result = json.loads(runner.execute(script.encode(), dll=dll))
    require(result["passed"], "Actual installed context rejected")
    require(before == {str(path.resolve()): sha(path) for path in watched}, "Read-only watched inputs changed")
    output.mkdir(parents=True)
    program = output / "installed-context.lua"
    program.write_text(script, encoding="utf-8")
    report = {"passed": True, "tool": pin(Path(__file__)), "candidate": pin(CANDIDATE), "targetManifest": pin(manifest),
              "installedAddon": pin(installed), "settings": pin(settings_path), "receipt": pin(receipt_path),
              "program": pin(program), "primaryReadOnlyInputs": primary_inputs, "luaRunner": pin(runner_path), "standaloneLuaDll": pin(dll),
              "result": result, "beforeSha256": before, "watchedInputsUnchanged": True,
              "gameplayTested": False, "audiblePlaybackTested": False, "liveChanges": False}
    path = output / "report.json"
    path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return pin(path)


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--manifest-sha", required=True)
    parser.add_argument("--out", type=Path, required=True)
    logging.basicConfig(level=logging.INFO)
    try:
        args = parser.parse_args(argv)
        print(json.dumps(run(args.manifest, args.manifest_sha, args.out)))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception("Read-only installed context proof failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
