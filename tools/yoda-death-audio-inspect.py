#!/usr/bin/env python3
"""Inspect current native one-shot Wwise templates without changing their bytes."""
import argparse
import hashlib
import json
import logging
from pathlib import Path
import runpy
import struct
import sys
import types


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bank", type=Path)
    parser.add_argument("out", type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    logger_module = types.ModuleType("log")
    logger_module.logger = logging.getLogger("yoda-inspect")
    sys.modules["log"] = logger_module
    sys.path.insert(0, str(root / "dist/rc-upgrade/hd2-audio-modder"))
    import wwise_hierarchy_154 as P
    A = runpy.run_path(str(root / "tools/audit-live-audio.py"))
    data = args.bank.read_bytes()
    chunks = A["chunks"](data)
    raw = A["hierarchy"](chunks[b"HIRC"])
    hierarchy = P.WwiseHierarchy_154()
    hierarchy.load(chunks[b"HIRC"])
    rows = []
    for key, obj in hierarchy.entries.items():
        kind, body = raw[key]
        if kind not in (2, 3, 4, 7):
            continue
        row = {"id": key, "kind": kind, "bytes": len(body), "bodyHex": body.hex()}
        if obj.get_base_param if hasattr(obj, "get_base_param") else False:
            try:
                bp = obj.get_base_param()
                row["base"] = {name: getattr(bp, name) for name in
                               ("directParentID", "overrideBusId", "byBitVectorA", "uNumFx", "uNumFxMetadata", "uNumCurves")}
                row["base"]["positioningHex"] = bp.positioningParamData.hex()
                row["base"]["props"] = bp.propBundle.__dict__
                row["base"]["ranges"] = bp.rangePropBundle.__dict__
                row["base"]["state"] = bp.stateParams.__dict__
            except (AssertionError, NotImplementedError):
                pass
        if kind == 2:
            row["source"] = obj.sources[0].__dict__
        elif kind == 3:
            row["actionType"] = obj.ulActionType
            row["target"] = obj.idExt
            row["bankId"] = getattr(obj, "bankID", None)
        elif kind == 4:
            row["actions"] = obj.ulActionIDs
        elif kind == 7:
            row["children"] = obj.children.children
        rows.append(row)
    output = {"bank": str(args.bank.resolve()), "sha256": hashlib.sha256(data).hexdigest(),
              "bankHeader": chunks[b"BKHD"].hex(), "wrapper": data[:16].hex(),
              "chunks": {tag.decode(): len(value) for tag, value in chunks.items()}, "rows": rows}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(output, indent=2, default=lambda value:
                                 value.hex() if isinstance(value, (bytes, bytearray)) else value.__dict__) + "\n", encoding="utf-8")
    print(json.dumps({"out": str(args.out), "objects": len(rows), "chunks": output["chunks"]}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
