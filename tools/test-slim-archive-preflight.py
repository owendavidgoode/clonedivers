#!/usr/bin/env python3
"""Real malformed-archive regressions and targeted negative layout fixtures."""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
AREA = ROOT/"dist/empire-launch-crash-2026-10-08/loop-2026-10-09"


def pin(path:Path)->dict[str,Any]:
    data = path.read_bytes()
    return {"path":str(path.resolve()),"bytes":len(data),"sha256":hashlib.sha256(data).hexdigest()}


def main()->None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output",type=Path,default=AREA/"archive-preflight-regressions-v1")
    out = parser.parse_args().output.resolve()
    if not out.is_relative_to(AREA):
        raise ValueError("Scoped regression output")
    if out.exists():
        raise ValueError("Fresh regression output")
    module_path = ROOT/"tools/slim-archive-preflight.py"
    spec = importlib.util.spec_from_file_location("archive_preflight_regression",module_path)
    if spec is None or spec.loader is None:
        raise ImportError(module_path)
    module = importlib.util.module_from_spec(spec);sys.modules[spec.name] = module;spec.loader.exec_module(module)
    old_audio_path = ROOT/"dist/empire-deep-qa-2026-10-08/audio/constitution-prefix-candidate-v1/bundle/9ba626afa44a3aa3.patch_0"
    new_audio_path = AREA/"fixed-constitution-v1/assets/25b90ccff893f69b3d59ebc9057f040c4cccaad54ea44c1822dbaf94d2a7c11b"
    old_ship_path = ROOT/"dist/empire-finish-2026-10-08/ships/alignment-candidate-v1/bundle/9ba626afa44a3aa3.patch_0"
    new_ship_path = AREA/"fixed-isd-order-v1/assets/17e367a3ab6ecbfbd9dc91f7eb6c1b6620703b0cd914407dc6ca306e2d695806"
    old_audio,audio,old_ship,ship = (path.read_bytes() for path in (old_audio_path,new_audio_path,old_ship_path,new_ship_path))
    if hashlib.sha256(old_audio).hexdigest() != "7db519ad43cfd67b3d75c853afa63c82ff8c17fe82fc9b04bc0dd7479cbf4204" or hashlib.sha256(old_ship).hexdigest() != "8672ec27fa2e94a52f2734b93b3015ef2388f1bd6d5795b283902f4dd2fb4b90":
        raise ValueError("Exact malformed public source fixtures")
    cases = []

    def case(name:str,raw:bytes,expected:bool,codes:set[str]|None=None,sizes:tuple[int,int]|None=None)->None:
        report = module.inspect_directory(raw,len(raw),sizes)
        actual_codes = {row["code"] for row in report["issues"]}
        if report["passed"] != expected or codes is not None and not codes <= actual_codes:
            raise ValueError(f"Unexpected fixture {name}: {report}")
        try:
            module.validate_directory(raw,len(raw),sizes)
        except module.ArchivePreflightError:
            if expected:
                raise
        else:
            if not expected:
                raise ValueError("Strict guard accepted:"+name)
        cases.append({"name":name,"expectedPass":expected,"actualPass":report["passed"],"issueCodes":sorted(actual_codes)})

    case("actual-public453-truncated",old_audio,False,{"main-head-underpadded","wwise-stream-metadata-slot-outside-main"})
    case("actual-padded453",audio,True,sizes=(447764,0))
    case("actual-public429-interleaved",old_ship,False,{"type-rows-not-contiguous-in-table-order"},(0,6684610))
    case("actual-corrected429",ship,True,sizes=(0,6684610))
    at = 72+32*struct.unpack_from("<I",ship,4)[0]
    invalid = bytearray(ship);invalid[at:at+80],invalid[at+160:at+240] = invalid[at+160:at+240],invalid[at:at+80]
    case("interleaved-row-regression",bytes(invalid),False,{"type-rows-not-contiguous-in-table-order"})
    invalid = bytearray(ship);struct.pack_into("<I",invalid,72+16,99)
    case("mismatched-declared-type-count",bytes(invalid),False,{"type-count-mismatch"})
    invalid = bytearray(audio);struct.pack_into("<Q",invalid,104+16,252);struct.pack_into("<I",invalid,104+56,4)
    case("wwise16byte-slot-past-eof-with-valid-main-size-and-payload-range",bytes(invalid),False,{"wwise-stream-metadata-slot-outside-main"})
    case("underpadded-head-with-complete16byte-slot",old_audio+bytes(4),False,{"main-head-underpadded"})
    case("truncated-directory",ship[:200],False,{"truncated-directory"})
    invalid = bytearray(audio);struct.pack_into("<Q",invalid,104+16,250)
    case("main-payload-range-past-eof",bytes(invalid),False,{"resource-range-outside-file"})
    case("stream-range-past-companion-eof",audio,False,{"resource-range-outside-file"},(1,0))
    legacy = bytearray(ship)
    legacy[at:at+80],legacy[at+80:at+160] = legacy[at+80:at+160],legacy[at:at+80]
    for i in range(6):
        struct.pack_into("<I",legacy,at+i*80+76,999-i)
    case("within-type-name-disorder-and-legacy-ordinals-remain-accepted",bytes(legacy),True,sizes=(0,6684610))
    # Existing valid authoring can retain a type descriptor with a zero-length
    # range. Add one to the table while moving only the six row descriptors.
    empty_type = bytearray(ship[:72])
    struct.pack_into("<I",empty_type,4,4)
    empty_type.extend(ship[72:168])
    empty_type.extend(struct.pack("<IIQIIII",0,0,0x1234567890ABCDEF,0,0,0,0))
    empty_type.extend(ship[168:648])
    empty_type.extend(ship[680:])
    # This in-memory fixture has the old MAIN payload offsets but the directory
    # end advanced32bytes. The smallest payload starts656; lift those offsets
    # so the test focuses on empty type ranges, with payload bytes unconsumed.
    for index in range(6):
        pos = 200+index*80
        offset = struct.unpack_from("<Q",empty_type,pos+16)[0]
        if offset < 680:
            struct.pack_into("<Q",empty_type,pos+16,680)
    case("declared-zero-resource-type-is-valid",bytes(empty_type),True)
    report = {"passed":True,"tool":pin(Path(__file__)),"guard":pin(module_path),"sourceFixtures":[pin(old_audio_path),pin(old_ship_path)],"correctedFixtures":[pin(new_audio_path),pin(new_ship_path)],"cases":cases,"realMalformedArchivesRejected":True,"correctedArchivesAccepted":True,"engineExecuted":False,"hostWrites":False,"gameplayAccepted":False}
    out.mkdir()
    output = out/"report.json";output.write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
    print(json.dumps({"passed":True,"cases":len(cases),"report":pin(output)}))


if __name__ == "__main__":
    main()
