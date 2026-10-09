#!/usr/bin/env python3
"""Check engine-facing SLIM MAIN layout before installing authored archives.

The aligned head and 16-byte Wwise slot rules are conservative deployment
policies based on the startup investigation and working archive conventions.
Passing them does not establish runtime loadability or gameplay acceptance.
Reads only the header/directory; payload bytes and assets are never rewritten.
"""
from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path
import struct
from typing import Any

HEADER_BYTES = 72
TYPE = struct.Struct("<IIQIIII")
ROW = struct.Struct("<7Q6I")
WWISE_STREAM = 0x504B55235D21440E


class ArchivePreflightError(ValueError):
    """An archive violates one or more deployment layout checks."""

    def __init__(self, issues: list[dict[str, Any]]) -> None:
        self.issues = issues
        super().__init__("; ".join(str(issue["code"]) for issue in issues))


def inspect_directory(data: bytes, total_size: int, companion_sizes: tuple[int, int] | None = None) -> dict[str, Any]:
    """Inspect a complete header/directory with the actual full MAIN size."""
    issues: list[dict[str, Any]] = []

    def issue(code: str, **details: Any) -> None:
        issues.append({"code": code, **details})

    if len(data) < HEADER_BYTES:
        issue("truncated-header", actual=len(data), required=HEADER_BYTES)
        return {"passed": False, "issues": issues}
    magic, nt, nf = struct.unpack_from("<III", data)
    if magic != 0xF0000011:
        issue("wrong-magic", actual=f"{magic:08x}")
    if nt > 100_000 or nf > 1_000_000:
        issue("unbounded-directory-counts", types=nt, resources=nf)
        return {"passed": False, "issues": issues}
    directory_end = HEADER_BYTES + nt*TYPE.size + nf*ROW.size
    minimum_main_bytes = (directory_end+8+255)//256*256
    if total_size < minimum_main_bytes:
        issue("main-head-underpadded", actual=total_size, required=minimum_main_bytes, directoryEnd=directory_end)
    if directory_end > len(data) or directory_end > total_size:
        issue("truncated-directory", directoryEnd=directory_end, captured=len(data), actualMain=total_size)
        return {"passed": False, "issues": issues}
    types = list(TYPE.iter_unpack(data[HEADER_BYTES:HEADER_BYTES+nt*TYPE.size]))
    rows = list(ROW.iter_unpack(data[HEADER_BYTES+nt*TYPE.size:directory_end]))
    type_ids = [row[2] for row in types]
    if len(set(type_ids)) != nt:
        issue("duplicate-type-descriptor")
    keys = [row[:2] for row in rows]
    if len(set(keys)) != nf:
        issue("duplicate-typed-resource")
    actual_counts = Counter(row[1] for row in rows)
    declared_counts = {row[2]: row[3] for row in types}
    # A declared empty type range is valid. Counter omits zero entries, so compare
    # its values with implicit zeros rather than rejecting that representation.
    if any(actual_counts.get(kind, 0) != count for kind,count in declared_counts.items()) or not set(actual_counts) <= set(declared_counts):
        issue("type-count-mismatch", declared={f"{key:016x}":value for key,value in declared_counts.items()}, actual={f"{key:016x}":value for key,value in actual_counts.items()})
    expected_order = [row[2] for row in types for _ in range(row[3])]
    if [row[1] for row in rows] != expected_order:
        issue("type-rows-not-contiguous-in-table-order", declaredOrder=[f"{key:016x}" for key in type_ids], actualRowTypes=[f"{row[1]:016x}" for row in rows])
    sizes = (total_size,*companion_sizes) if companion_sizes is not None else (total_size,)
    for index,row in enumerate(rows):
        key = f"{row[0]:016x}.{row[1]:016x}"
        for part,size in enumerate(sizes):
            offset,length = row[2+part],row[7+part]
            if length and offset+length > size:
                issue("resource-range-outside-file", row=index, key=key, part=part, offset=offset, length=length, actualSize=size)
            if part == 0 and length and offset < directory_end:
                issue("main-resource-overlaps-directory", row=index, key=key, offset=offset, directoryEnd=directory_end)
        if row[1] == WWISE_STREAM and row[2]+16 > total_size:
            issue("wwise-stream-metadata-slot-outside-main", row=index, key=key, offset=row[2], requiredEnd=row[2]+16, actualMain=total_size)
    return {"passed":not issues,"mainBytes":total_size,"directoryEnd":directory_end,"minimumMainBytes":minimum_main_bytes,"typeDescriptors":nt,"resources":nf,"issues":issues,"ordinalsRewrittenOrRequiredDense":False,"withinTypeNameSortingRequired":False}


def validate_directory(data: bytes, total_size: int, companion_sizes: tuple[int, int] | None = None) -> dict[str, Any]:
    report = inspect_directory(data,total_size,companion_sizes)
    if not report["passed"]:
        raise ArchivePreflightError(report["issues"])
    return report


def inspect_path(path: Path, companion_sizes: tuple[int, int] | None = None) -> dict[str, Any]:
    with path.open("rb") as handle:
        header = handle.read(HEADER_BYTES)
        if len(header) < HEADER_BYTES:
            return inspect_directory(header,path.stat().st_size,companion_sizes)
        _,nt,nf = struct.unpack_from("<III",header)
        if nt > 100_000 or nf > 1_000_000:
            return inspect_directory(header,path.stat().st_size,companion_sizes)
        data = header+handle.read(nt*TYPE.size+nf*ROW.size)
    return inspect_directory(data,path.stat().st_size,companion_sizes)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("main",type=Path)
    parser.add_argument("--stream-size",type=int)
    parser.add_argument("--gpu-size",type=int)
    args = parser.parse_args()
    if (args.stream_size is None) != (args.gpu_size is None):
        parser.error("Pass both companion sizes, or neither for MAIN-only checks")
    sizes = None if args.stream_size is None else (args.stream_size,args.gpu_size)
    result = inspect_path(args.main,sizes)
    print(json.dumps({"path":str(args.main.resolve()),**result}))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
