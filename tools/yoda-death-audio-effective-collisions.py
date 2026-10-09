#!/usr/bin/env python3
"""Check proposed Yoda IDs against actual current Empire BANK winners, bounded reads."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
LANE = ROOT / "dist/empire-yoda-death-2026-10-08/audio"
R22 = ROOT / "dist/production-r22-2026-10-08"
ROW = struct.Struct("<7Q6I")
BANK = 0x535A7BD3E650D799
MAIN = re.compile(r"[a-f0-9]{16}\.patch_\d+$")


def require(value: Any, message: str) -> None:
    if not value:
        raise ValueError(message)


def pin(path: Path) -> dict[str, Any]:
    with path.open("rb") as handle:
        digest = hashlib.file_digest(handle, "sha256").hexdigest()
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": digest}


def read(path: Path, sha: str | None = None) -> Any:
    require(sha is None or pin(path)["sha256"] == sha, f"Sealed source changed: {path}")
    return json.loads(path.read_text(encoding="utf-8-sig"))


def run(out: Path) -> dict[str, Any]:
    require(not out.exists() and out.resolve().is_relative_to(LANE), "Fresh scoped output required")
    selector_path = R22 / "inputs-v1/production96.json"
    selected = read(selector_path, "9652cd9331deb344c043b0c401a0973c226a16a1d89899e2f3458f2f38e797bb")
    peer = read(R22 / "qa/runtime-peer-v1/report.json", "b5823c09feee7dfb0416dbc7aabc053022575175a68c0f646bcb2cec00739ab2")
    proof = peer["geometryAndEagleProfileDependencyPeer"]["freshMainAndAffectedCompanionHashes"]
    refs = read(Path(proof["path"]), proof["sha256"])
    sources = {row["sha256"]: row for row in refs}
    voice = read(R22 / "voice-v1/report.json", "4670d60a11156c9bf04630d2bee4671cf7384a678c0ad845a84083d474b20791")
    runtime = read(R22 / "runtime-v1/report.json", "e7810a0dc435d9cef588813a6c8b9d6a648ff9584d2130117f281449b8aac108")
    for row in voice["files"] + runtime["sources"][0]["files"]:
        sources[row["sha256"]] = row
    audio_path = LANE / "candidate-v1/report.json"
    audio = read(audio_path, "994b87c9c7f78d5841b10f6dbec1d65c82ffb1f1ec818523a12c53e0051981f5")
    proposed = set(audio["ids"].values())
    require(len(proposed) == 6, "Six disjoint proposed IDs")
    dirs, directory_proofs = {}, []

    def directory(row: dict[str, Any]) -> dict[tuple[int, int], tuple[int, ...]]:
        digest = row["sha256"]
        if digest not in dirs:
            ref = sources[digest]
            path = Path(ref["path"])
            require(path.stat().st_size == row["size"] == ref["bytes"], "Retained source extent")
            with path.open("rb") as handle:
                head = handle.read(72)
                magic, types, count = struct.unpack_from("<III", head)
                require(magic == 0xF0000011 and types < 1000 and count < 1_000_000, "Bounded native archive")
                table = handle.read(types * 32 + count * 80)
            require(len(table) == types * 32 + count * 80, "Complete directory")
            records = list(ROW.iter_unpack(table[types * 32:]))
            require(len({r[:2] for r in records}) == count and all(r[2] + r[7] <= row["size"] for r in records), "Unique bounded MAIN records")
            dirs[digest] = {r[:2]: r for r in records}
            directory_proofs.append({"wholeFileIdentityReused": ref, "freshDirectorySha256": hashlib.sha256(head + table).hexdigest()})
        return dirs[digest]

    sets = sorted({s["fileSet"] for s in selected["selections"] if s["mode"] == "EmpireDivers"})
    require(len(sets) == 8, "All eight Empire effective file sets")
    union, selections = {}, []
    for file_set in sets:
        winners = {}
        for row in selected["fileSets"][file_set]:
            if MAIN.fullmatch(row["name"]) and row["size"]:
                for key, record in directory(row).items():
                    if key[1] == BANK:
                        winners[key] = row["sha256"], record
        for key, (digest, record) in winners.items():
            union[key, digest] = record
        selections.append({"fileSet": file_set, "winningBanks": len(winners)})
    object_ids, media_ids, cache_ids = set(), set(), set()
    unique_hirc, banks, bytes_read = {}, [], 0
    for (key, digest), record in sorted(union.items()):
        path = Path(sources[digest]["path"])
        start, length = record[2], record[7]
        with path.open("rb") as handle:
            handle.seek(start)
            wrapper = handle.read(16)
            require(len(wrapper) == 16 and struct.unpack_from("<I", wrapper, 4)[0] + 16 == length, "BANK wrapper extent")
            parts, cursor = {}, 16
            bytes_read += 16
            while cursor < length:
                handle.seek(start + cursor)
                header = handle.read(8)
                require(len(header) == 8, "Complete BANK chunk header")
                tag, extent = struct.unpack("<4sI", header)
                require(tag not in parts and cursor + 8 + extent <= length, "Unique bounded BANK chunks")
                bytes_read += 8
                if tag in (b"BKHD", b"HIRC", b"DIDX"):
                    payload = handle.read(extent)
                    require(len(payload) == extent, "Complete bounded BANK metadata")
                    parts[tag] = payload
                    bytes_read += extent
                else:
                    parts[tag] = None
                cursor += 8 + extent
            require(cursor == length and b"BKHD" in parts and b"HIRC" in parts, "Complete BANK chunks")
        version = struct.unpack_from("<I", parts[b"BKHD"])[0] ^ 0x9211BCAC
        require(version == 154, "Current effective Wwise154 source contract")
        hirc = parts[b"HIRC"]
        hirc_sha = hashlib.sha256(hirc).hexdigest()
        if hirc_sha not in unique_hirc:
            cursor, count = 4, struct.unpack_from("<I", hirc)[0]
            for _ in range(count):
                kind, extent, identity = struct.unpack_from("<BII", hirc, cursor)
                require(extent >= 4 and cursor + 5 + extent <= len(hirc), "Complete HIRC object")
                object_ids.add(identity)
                body = hirc[cursor + 9:cursor + 5 + extent]
                if kind == 2:
                    require(len(body) >= 18, "Complete native source declaration")
                    _, _, media, cache, _, _ = struct.unpack_from("<IBIIIB", body)
                    media_ids.add(media)
                    cache_ids.add(cache)
                cursor += 5 + extent
            require(cursor == len(hirc), "Complete HIRC tail")
            unique_hirc[hirc_sha] = count
        if parts.get(b"DIDX") is not None:
            require(len(parts[b"DIDX"]) % 12 == 0, "Complete DIDX entries")
            media_ids.update(row[0] for row in struct.iter_unpack("<III", parts[b"DIDX"]))
        banks.append({"typedKey": f"{key[0]:016x}.{key[1]:016x}", "archiveSha256": digest, "hircSha256": hirc_sha, "bkhdSha256": hashlib.sha256(parts[b"BKHD"]).hexdigest(), "version": version})
    require(not proposed & (object_ids | media_ids | cache_ids), "No Yoda collision in any effective modded BANK")
    out.mkdir(parents=True)
    report = {"passed": True, "tool": pin(Path(__file__)), "selector": pin(selector_path), "audio": pin(audio_path), "priorIndependentWholeMainHashProof": proof,
              "eightEffectiveSelections": selections, "bankWinnerIdentities": len(union), "uniqueHircPayloads": len(unique_hirc), "objectIds": len(object_ids), "mediaIds": len(media_ids), "cacheIds": len(cache_ids),
              "allSixProposedIdsDisjointFromEffectiveModdedBanks": True, "boundedBankMetadataBytesRead": bytes_read, "bankProofs": banks,
              "freshDirectoryProofs": directory_proofs, "existingMainWholeFileHashReuse": True, "native479BankCollisionProofSeparatelyRetained": True,
              "gameSettingsOrDeploymentChanged": False}
    (out / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return {"passed": True, "report": pin(out / "report.json"), "bankWinnerIdentities": len(union), "boundedBankMetadataBytesRead": bytes_read}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=LANE / "effective-collision-peer-v1")
    print(json.dumps(run(parser.parse_args().out.resolve())))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
