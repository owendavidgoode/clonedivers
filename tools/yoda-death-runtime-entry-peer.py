#!/usr/bin/env python3
"""Run the packaged entry with the actual reviewed SDK scheduler in sparse memory."""
from __future__ import annotations

import argparse
from collections.abc import Sequence
import importlib.util
import json
from pathlib import Path
import re
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("yoda_fixtures", ROOT / "tools/yoda-death-runtime-fixtures.py")
assert spec and spec.loader
F = importlib.util.module_from_spec(spec)
spec.loader.exec_module(F)
B = F.B


def run(candidate: Path, output: Path) -> dict[str, Any]:
    if output.exists() or not output.resolve().is_relative_to(B.AREA):
        raise ValueError("Fresh scoped output required")
    assert B.pin(candidate / "report.json")["sha256"] == "bb8f255af5294903fe07c042658b8df566715d39b7aba5b67d70025cc2acb015"
    pre, _ = F.prefix(B.AREA / "prepared-v2")
    modules: dict[str, Path] = {}
    def visit(name: str) -> None:
        if name in modules or not name.startswith("hd2runtime/"):
            return
        path = B.OLD / (name.removeprefix("hd2runtime/") + ".lua")
        assert path.is_file(), path
        modules[name] = path
        for dep in re.findall(r"require\s*\(?\s*['\"]([^'\"]+)['\"]", path.read_text()):
            visit(dep)
    visit("hd2runtime/api/events")
    pre += "".join(F.preload(name, path.read_text()) for name, path in sorted(modules.items()))
    tail = r'''
require('hd2runtime/runtime/event_world').set_runtime(W.runtime)
local api=require('hd2runtime/api/events')
api.api_version=1;api.version='0.28.1'
package.preload['mods/skyeshade/hd2runtime']=function()return api end
package.preload['hd2runtime/runtime/windows_readonly']=function()return function()return W.runtime end end
CowboyBingusModLoader={api=1,version=16}
local function docs()
 local s=assert(loadstring(SPEC_SOURCE))()
 local config={InstalledPackVersion=s.version,InstalledGameBuild=s.build,InstalledGamePath='E:\\Steam\\Helldivers 2',
 TextureProfile='full',InstalledTextureProfile='full',Options={empire=true},LastVerifiedUtc='test'}
 local receipt={PackVersion=s.version,GameBuild=s.build,GameDir=config.InstalledGamePath,TextureProfile='full',
 Format=1,TargetActive=true,VerifiedUtc='test',Options={empire=true},Files={}}
 local i=0
 for sha,size in pairs(s.assetSizes)do i=i+1;receipt.Files[i]={File={Name='9ba626afa44a3aa3.patch_'..i,Sha256=sha,Size=size}}end
 return config,receipt,false
end
W.runtime.comparison_context=docs
local entry=assert(loadstring(ADDON_SOURCE))()
for i=1,35 do update(0.1)end
assert(entry.status=='armed',entry.status)
assert(entry.lastObservation=='observed',entry.lastObservation)
W.set(100,{life=2,health=0});update(0.1)
assert(#posted==1 and entry.lastDeath.status=='PLAYED')
for i=1,5 do update(0.1)end
assert(#posted==1,'entry duplicated death')
W.state(3);update(0.1)
assert(#posted==1,'ship transition played')
return 'passed'
'''
    addon = (candidate / "addon.lua").read_text()
    tail = tail.replace("SPEC_SOURCE", B.lua((candidate / "context_spec.lua").read_text())).replace("ADDON_SOURCE", B.lua(addon))
    program = pre + F.SETUP + tail
    assert B.runner().execute(program.encode()) == b"passed"
    output.mkdir(parents=True)
    (output / "fixture.lua").write_text(program, encoding="utf-8")
    result = {"passed": True, "tool": B.pin(Path(__file__)), "candidate": B.pin(candidate / "report.json"),
              "addon": B.pin(candidate / "addon.lua"), "program": B.pin(output / "fixture.lua"),
              "sdkExtraInputs": [B.pin(path) for path in modules.values()],
              "actualSdkModAndScheduler": True, "startupContextSynthetic": True,
              "confirmedLocalDeadStatePostedOnce": True, "shipTransitionNoPost": True,
              "nativeEngineCalled": False, "liveChanges": False}
    path = output / "report.json"
    path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return B.pin(path)


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", type=Path, default=B.AREA / "candidate-v3")
    parser.add_argument("--out", type=Path, default=B.AREA / "entry-peer-v1")
    args = parser.parse_args(argv)
    print(json.dumps(run(args.candidate, args.out)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
