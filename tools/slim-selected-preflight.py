#!/usr/bin/env python3
"""Bounded all-layer layout gate over an actual production selector export.

Uses exact target SHA-named local cache/assets and sizes or a supplied verified
inventory. It does not rehash whole large assets or replace installer hash QA.
Structural checks apply to every layer; companion extent checks apply to winners.
"""
from __future__ import annotations

import argparse
from collections import Counter,defaultdict
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import struct
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
GAME = Path("C:/Program Files (x86)/Steam/steamapps/common/Helldivers 2")
PATCH = re.compile(r"([a-f0-9]{16})\.patch_(\d+)$")


def pin(path:Path)->dict[str,Any]:
    raw = path.read_bytes()
    return {"path":str(path.resolve()),"bytes":len(raw),"sha256":hashlib.sha256(raw).hexdigest()}


def read(path:Path)->Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def main()->int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest",type=Path)
    parser.add_argument("selector",type=Path)
    parser.add_argument("output",type=Path)
    parser.add_argument("--inventory",type=Path,action="append",default=[])
    parser.add_argument("--assets",type=Path,action="append",default=[])
    args = parser.parse_args()
    if args.output.exists() or not args.output.resolve().is_relative_to(ROOT/"dist"):
        raise ValueError("Fresh workspace dist output required")
    selected = read(args.selector)
    if not selected["passed"] or selected["manifestSha256"] != pin(args.manifest)["sha256"]:
        raise ValueError("Actual selector must bind exact manifest")
    module_path = ROOT/"tools/slim-archive-preflight.py"
    spec = importlib.util.spec_from_file_location("selected_layout_guard",module_path)
    if spec is None or spec.loader is None:
        raise ImportError(module_path)
    guard = importlib.util.module_from_spec(spec);sys.modules[spec.name] = guard;spec.loader.exec_module(guard)
    sources:dict[str,list[Path]] = defaultdict(list)
    for path in args.inventory:
        for row in read(path):
            sources[row["Sha"]].append(Path(row["Path"]))
    # Current receipt gives canonical paths for the actually installed selection.
    config = Path("C:/Users/goode/AppData/Roaming/Clonedivers")
    receipt_path = config/"receipts"/(hashlib.sha256(str(GAME).upper().encode()).hexdigest()+".json")
    receipt = read(receipt_path)
    for row in receipt["Files"]:
        item = row["File"]
        for folder in (GAME/"data",GAME/"mods_off"):
            path = folder/item["Name"]
            if path.is_file():
                stat = path.stat()
                if stat.st_size == item["Size"] and stat.st_mtime_ns//100+621355968000000000 == row["LastWriteUtcTicks"]:
                    sources[item["Sha256"]].insert(0,path)
    if "fileSets" in selected:
        sets = selected["fileSets"]
    else:
        sets = {selected.get("mode","selection"):[{"name":row["Name"],"sha256":row["Sha256"],"size":row["Size"]} for row in selected["files"]]}
    directories,checks,state_checks = {},[],[]
    main_failures,winner_faults,shadowed_faults,missing = [],[],[],[]
    read_bytes = 0
    for set_name,files in sets.items():
        indexed = {row["name"]:row for row in files}
        mains = sorted((row for row in files if PATCH.fullmatch(row["name"])),key=lambda row:(PATCH.fullmatch(row["name"])[1],int(PATCH.fullmatch(row["name"])[2])))
        winners,provider_ranges = {},[]
        for main in mains:
            digest = main["sha256"]
            if digest not in directories:
                possible = [folder/digest for folder in args.assets]+sources[digest]+[GAME/"mods_download"/digest]
                path = next((path for path in possible if path.is_file() and path.stat().st_size == main["size"]),None)
                if path is None:
                    missing.append({"set":set_name,"main":main});continue
                with path.open("rb") as handle:
                    header = handle.read(72)
                    if len(header) != 72:
                        raw = header
                    else:
                        _,nt,nf = struct.unpack_from("<III",header)
                        raw = header+handle.read(nt*32+nf*80) if nt <= 100_000 and nf <= 1_000_000 else header
                read_bytes += len(raw)
                check = guard.inspect_directory(raw,main["size"])
                record = {"targetMainSha256":digest,"readPath":str(path.resolve()),"bytes":main["size"],"lastWriteUtcNs":path.stat().st_mtime_ns,"freshDirectorySha256":hashlib.sha256(raw).hexdigest(),**check}
                checks.append(record)
                if not check["passed"]:
                    main_failures.append(record)
                if len(raw) < 72:
                    directories[digest] = []
                else:
                    _,nt,nf = struct.unpack_from("<III",raw)
                    directories[digest] = list(struct.iter_unpack("<7Q6I",raw[72+nt*32:72+nt*32+nf*80])) if len(raw) >= 72+nt*32+nf*80 else []
            for row in directories[digest]:
                winners[row[:2]] = digest
                for suffix,part in ((".stream",1),(".gpu_resources",2)):
                    size = indexed.get(main["name"]+suffix,{"size":0})["size"]
                    offset,length = row[2+part],row[7+part]
                    if length and offset+length > size:
                        provider_ranges.append({"set":set_name,"mainSha256":digest,"key":f"{row[0]:016x}.{row[1]:016x}","keyTuple":row[:2],"part":part,"offset":offset,"length":length,"actualSize":size})
        faults = []
        for value in provider_ranges:
            value = dict(value);key = value.pop("keyTuple")
            (faults if winners.get(key) == value["mainSha256"] else shadowed_faults).append(value)
        winner_faults.extend(faults)
        state_checks.append({"fileSet":set_name,"selectedFiles":len(files),"selectedMains":len(mains),"winningResources":len(winners),"winningCompanionRangeFaults":faults})
    result = {"passed":not main_failures and not winner_faults and not missing,"meaningOfPassed":"All-layer bounded MAIN head/type-grouping/Wwise slot checks and effective winning companion extent checks; source fullhash and runtime acceptance remain separate", "tool":pin(Path(__file__)),"guard":pin(module_path),"manifest":pin(args.manifest),"actualSelector":pin(args.selector),"sourceInventories":[pin(path) for path in args.inventory],"actualReceipt":pin(receipt_path),"fileSets":state_checks,"distinctMainIdentities":len(directories),"freshBoundedBytesRead":read_bytes,"archives":checks,"structuralMainFailures":main_failures,"winningCompanionRangeFaults":winner_faults,"shadowedCompanionRangeFaults":shadowed_faults,"missingSources":missing,"structuralChecksExcludedForShadowedArchives":False,"wholeAssetHashesRescanned":False,"sourceIdentityScope":"Exact target size plus SHA-named cache/assets or recorded inventory/receipt identity; file contents outside fresh bounded directory reads rely independent installer fullhash proof.","engineExecuted":False,"hostWrites":False,"gameplayAccepted":False}
    args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    print(json.dumps({"passed":result["passed"],"distinctMains":len(directories),"structuralFailures":len(main_failures),"winningRangeFaults":len(winner_faults),"shadowedRangeFaults":len(shadowed_faults),"missing":len(missing),"report":pin(args.output)}))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
