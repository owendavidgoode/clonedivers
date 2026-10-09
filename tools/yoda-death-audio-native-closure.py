#!/usr/bin/env python3
"""Read only native BANK headers/HIRC for Yoda ID collision and bus closure proof."""
import argparse
from collections import Counter
from functools import lru_cache
import hashlib
import json
import logging
from pathlib import Path
import runpy
import struct
import sys
from typing import Any

INDEX_SHA = "5b410b532751539875bd21f18635de966d2c18e70e7f381c331141d4b934b7c2"
NATIVE_INDEX_SHA = "6e3a4e8c89be8d376f34b8118fafe3048756b96b7bdf3f3110306193b7b0945f"
BUS = 2467607253


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def pin(path: Path) -> dict[str, Any]:
    with path.open("rb") as source:
        value = hashlib.file_digest(source, "sha256").hexdigest()
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": value}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("workspace", type=Path)
    parser.add_argument("out", type=Path)
    args = parser.parse_args()
    root, out = args.workspace.resolve(), args.out.resolve()
    require(out.is_relative_to(root / "dist/empire-yoda-death-2026-10-08/audio") and not out.exists(), "Fresh scoped output")
    index_path = root / "dist/empire-voice-next-2026-10-08/dispatch/native-fingerprints-v3/index.json"
    require(pin(index_path)["sha256"] == INDEX_SHA, "Retained complete native BANK provider index pin")
    game_data = Path("C:/Program Files (x86)/Steam/steamapps/common/Helldivers 2/data")
    native_index = game_data / "bundles.nxa"
    require(pin(native_index)["sha256"] == NATIVE_INDEX_SHA, "Native build DSAA index pin")
    reader_path = root / "tools/walker-next-native.py"
    NativeSlim = runpy.run_path(str(reader_path))["NativeSlim"]
    native = NativeSlim(game_data)
    native.chunk = lru_cache(maxsize=16)(native.chunk)
    records = json.loads(index_path.read_text(encoding="utf-8"))["banks"]
    objects, media, caches, seen, proofs, buses = set(), set(), set(), {}, [], []
    counts = Counter()
    logical_bytes = 0
    for number, record in enumerate(records):
        archive, row = record["archive"], record["row"]
        read = lambda at, length: native.read(archive, row[2] + at, length)
        head = read(0, 16)
        require(struct.unpack_from("<I", head, 4)[0] + 16 == row[7], "Fresh BANK wrapper extent")
        at, chunks = 16, {}
        while at < row[7]:
            tag, length = struct.unpack("<4sI", read(at, 8))
            require(tag not in chunks and at + 8 + length <= row[7], "Fresh unique bounded BANK chunks")
            chunks[tag] = [at + 8, length]
            at += 8 + length
        require(at == row[7] and {tag.decode(): part for tag, part in chunks.items()} == record["chunks"], "Retained index positions match fresh BANK headers")
        hirc = read(*chunks[b"HIRC"]) if b"HIRC" in chunks else bytes(4)
        logical_bytes += 16 + len(chunks) * 8 + len(hirc)
        checksum = hashlib.sha256(hirc).hexdigest()
        if checksum not in seen:
            cursor, identities = 4, set()
            for _ in range(struct.unpack_from("<I", hirc)[0]):
                kind, size, identity = struct.unpack_from("<BII", hirc, cursor)
                require(size >= 4 and cursor + 5 + size <= len(hirc),
                        f"Complete native HIRC object boundaries: {record['key']} {archive} {identity} {cursor}")
                identities.add(identity)
                objects.add(identity)
                counts[kind] += 1
                body = hirc[cursor + 9:cursor + 5 + size]
                if kind == 2 and len(body) >= 18:
                    codec, mode, source, cache, memory, flags = struct.unpack_from("<IBIIIB", body)
                    media.add(source)
                    caches.add(cache)
                if identity == BUS:
                    require(kind == 8, "Existing standard-voice bus is native AudioBus")
                    buses.append({"bank": record["key"], "archive": archive, "type": kind,
                                  "id": identity, "bodyHex": body.hex(), "bodySha256": hashlib.sha256(body).hexdigest()})
                cursor += 5 + size
            require(cursor == len(hirc), "Fresh native HIRC complete tail")
            seen[checksum] = len(identities)
        proofs.append({"bank": record["key"], "archive": archive, "hircBytes": len(hirc),
                       "hircSha256": checksum, "objects": seen[checksum], "wrapperHex": head.hex()})
        if number % 100 == 0:
            print(json.dumps({"nativeBankProviders": number, "uniqueHirc": len(seen)}), flush=True)
    require(len(records) == 696 and len({row["bank"] for row in proofs}) == 479, "Complete retained native inventory, including unnamed BANKs")
    require(any(bus["bank"] == "065cfa3b2c82a13d" for bus in buses), "Standard voice output bus is resident Init BANK")
    out.mkdir(parents=True)
    ids_path = out / "all-native-ids.json"
    ids_path.write_text(json.dumps({"objects": sorted(objects), "media": sorted(media), "caches": sorted(caches)}, separators=(",", ":")) + "\n", encoding="utf-8")
    proof_path = out / "bank-provider-hirc-pins.json"
    proof_path.write_text(json.dumps(proofs, indent=2) + "\n", encoding="utf-8")
    report = {"passed": True, "tool": pin(Path(__file__)), "reader": pin(reader_path), "nativeProviderIndex": pin(index_path),
              "nativeDsaaIndex": pin(native_index), "nativeBankProviders": len(records), "uniqueNativeBanks": 479,
              "uniqueNativeHirc": len(seen), "objectKinds": dict(counts), "uniqueObjectIds": len(objects),
              "uniqueMediaIds": len(media), "uniqueCacheIds": len(caches), "logicalBytesRead": logical_bytes,
              "allIds": pin(ids_path), "freshHircProofs": pin(proof_path), "standardVoiceBus": buses,
              "scope": "Fresh read-only BANK chunk headers and complete HIRC across all retained 479 native BANK identities / 696 providers; no DATA/STREAM/GPU waveform scan",
              "gameOrSettingsChanged": False}
    report_path = out / "report.json"
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(pin(report_path)))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        sys.exit(130)
    except (OSError, ValueError, KeyError, struct.error):
        logging.exception("Native Yoda closure failed")
        sys.exit(1)
