#!/usr/bin/env python3
"""Independently parse and decode the five-cue r22 voice STREAM overlay.

The proof starts from sealed source and wrapper reports and raw archive bytes.
It does not import the voice builder or trust its parsed resource records.
"""
from __future__ import annotations

import argparse
from collections.abc import Sequence
import hashlib
import json
import logging
from pathlib import Path
import re
import struct
import subprocess
import sys
from typing import Any
import wave

ROOT = Path(__file__).resolve().parents[1]
AREA = ROOT / "dist/production-r22-2026-10-08"
FOLLOW = ROOT / "dist/empire-offline-followup-2026-10-08/voice"
STREAM_TYPE = 0x504B55235D21440E
BANK_KEY = (0xB7CC016F2537E3D3, 0x535A7BD3E650D799)
EXPECTED_TARGETS = {598236107, 774990988, 631423309, 975870236, 367932016}
ROW = struct.Struct("<7Q6I")


def require(condition: Any, message: str) -> None:
    if not condition:
        raise ValueError(message)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def pin(path: Path) -> dict[str, Any]:
    before = path.stat()
    require(path.is_file() and not path.is_symlink(), f"Regular input required: {path}")
    with path.open("rb") as handle:
        digest = hashlib.file_digest(handle, "sha256").hexdigest()
    after = path.stat()
    require((before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns), "Input changed while hashing")
    return {"path": str(path.resolve()), "bytes": after.st_size, "sha256": digest}


def checked(reference: dict[str, Any]) -> Path:
    path = Path(reference["path"])
    require(pin(path) == reference, f"Pinned input drift: {path}")
    return path


def load(path: Path, expected: str | None = None) -> dict[str, Any]:
    require(expected is None or pin(path)["sha256"] == expected, f"Sealed proof drift: {path}")
    return json.loads(path.read_text(encoding="utf-8-sig"))


def typed(text: str) -> tuple[int, int]:
    require(re.fullmatch(r"[a-f0-9]{16}\.[a-f0-9]{16}", text), "Canonical typed key")
    return tuple(int(part, 16) for part in text.split("."))  # type: ignore[return-value]


def directory(main: bytes) -> dict[tuple[int, int], tuple[int, ...]]:
    require(len(main) >= 72 and struct.unpack_from("<I", main)[0] == 0xF0000011, "Native SLIM signature/header")
    types, count = struct.unpack_from("<II", main, 4)
    start = 72 + types * 32
    require(0 < types < 100 and count < 100_000 and start + ROW.size * count <= len(main), "Bounded native directory")
    result = {}
    for index in range(count):
        row = ROW.unpack_from(main, start + ROW.size * index)
        key = row[:2]
        require(key not in result and row[2] + row[7] <= len(main), "Unique bounded MAIN payload")
        result[key] = row
    return result


def strict_overlay(main: bytes, stream: bytes, gpu: bytes) -> dict[tuple[int, int], tuple[bytes, bytes]]:
    require(struct.unpack_from("<III", main) == (0xF0000011, 1, 5) and not gpu, "Exactly five STREAM replacements/no GPU")
    require(struct.unpack_from("<Q", main, 32)[0] == len(main), "Declared complete MAIN extent")
    require(struct.unpack_from("<QQQII", main, 72) == (0, STREAM_TYPE, 5, 16, 16), "Exact primary SDK type directory and alignments")
    entries = directory(main)
    require(len(entries) == 5 and all(key[1] == STREAM_TYPE for key in entries), "Only five STREAM typed resources")
    occupied_main = bytearray(len(main))
    occupied_main[:104 + ROW.size * 5] = b"\1" * (104 + ROW.size * 5)
    occupied_stream = bytearray(len(stream))
    result = {}
    for ordinal, (key, row) in enumerate(entries.items()):
        require(row[5:7] == (0, 0) and row[4] == 0 and row[7] == 12 and row[9] == 0 and
                row[10:13] == (16, 16, ordinal), "Primary SDK wrapper/reserved/GPU/ordinal fields exact")
        require(row[2] >= 104 + ROW.size * 5 + 8 and row[2] % 16 == 0 and row[3] % 16 == 0 and
                row[3] + row[8] <= len(stream) and row[8] >= 9728, "Aligned bounded MAIN/STREAM allocations")
        for marks, offset, size in ((occupied_main, row[2], row[7]), (occupied_stream, row[3], row[8])):
            require(not any(marks[offset:offset + size]), "Resource extents do not overlap")
            marks[offset:offset + size] = b"\1" * size
        result[key] = (main[row[2]:row[2] + 12], stream[row[3]:row[3] + row[8]])
    require(list(entries) == sorted(entries), "Canonical sorted typed directory")
    require(all(value == 0 for value, used in zip(main, occupied_main, strict=True) if not used) and
            all(value == 0 for value, used in zip(stream, occupied_stream, strict=True) if not used), "All allocation gaps are zero padding")
    return result


def pcm(path: Path, frames: int) -> bytes:
    with wave.open(str(path), "rb") as handle:
        require(handle.getparams()[:4] == (1, 2, 48000, frames), "Mono48k PCM16 exact frame contract")
        data = handle.readframes(frames + 1)
    require(len(data) == 2 * frames, "Complete PCM frames")
    return data


def decode(decoder: Path, source: Path, output: Path, frames: int, pcm_codec: bool) -> tuple[bytes, str]:
    metadata = subprocess.run([str(decoder), "-m", str(source)], capture_output=True, text=True, check=True).stdout
    count = re.search(r"stream total samples: (\d+)", metadata)
    require(count is not None and int(count.group(1)) == frames and "channels: 1" in metadata and
            "sample rate: 48000 Hz" in metadata and "loop start:" not in metadata, "Independent decoder frame/rate/channel/no-loop contract")
    require(not pcm_codec or "16-bit Little Endian PCM" in metadata, "Native PCM codec preserved")
    subprocess.run([str(decoder), "-i", "-o", str(output), str(source)], capture_output=True, text=True, check=True)
    return pcm(output, frames), metadata


def wav_chunks(wem: bytes) -> dict[bytes, bytes]:
    require(wem[:4] == b"RIFF" and wem[8:12] == b"WAVE" and struct.unpack_from("<I", wem, 4)[0] + 8 == len(wem), "WEM RIFF complete extent")
    cursor, chunks = 12, {}
    while cursor < len(wem):
        require(cursor + 8 <= len(wem), "RIFF header extent")
        tag, count = struct.unpack_from("<4sI", wem, cursor)
        require(tag not in chunks and cursor + count + 8 <= len(wem), "Bounded unique RIFF chunks")
        chunks[tag] = wem[cursor + 8:cursor + 8 + count]
        cursor += 8 + count + (count & 1)
    require(cursor == len(wem) and set(chunks) == {b"fmt ", b"data"}, "Minimal exact PCM RIFF chunks")
    return chunks


def sound_contracts(bank: bytes) -> dict[int, dict[str, Any]]:
    require(len(bank) == 16 + struct.unpack_from("<I", bank, 4)[0], "BANK wrapper complete extent")
    cursor, chunks = 16, {}
    while cursor < len(bank):
        tag, size = struct.unpack_from("<4sI", bank, cursor)
        require(tag not in chunks and cursor + 8 + size <= len(bank), "Unique bounded BANK chunk")
        chunks[tag] = bank[cursor + 8:cursor + 8 + size]
        cursor += 8 + size
    require(cursor == len(bank), "Complete BANK traversal")
    hirc, cursor, result = chunks[b"HIRC"], 4, {}
    for _ in range(struct.unpack_from("<I", hirc)[0]):
        kind, size, identity = struct.unpack_from("<BII", hirc, cursor)
        require(size >= 4 and cursor + 5 + size <= len(hirc), "HIRC object extent")
        if kind == 2:
            source = hirc[cursor + 9:cursor + 27]
            codec, mode, media, cache, memory, flags = struct.unpack("<IBIIIB", source)
            require(identity not in result, "Unique Sound object")
            result[identity] = dict(zip(("codec", "mode", "media", "cache", "memory", "flags"),
                                        (codec, mode, media, cache, memory, flags), strict=True))
            result[identity].update(sound=identity, source18=source.hex())
        cursor += 5 + size
    require(cursor == len(hirc), "Full HIRC object traversal")
    return result


def run(output: Path) -> dict[str, Any]:
    require(output.resolve().is_relative_to(AREA) and not output.exists(), "Fresh bounded peer output")
    batch_path = FOLLOW / "silence-batch-v1/report.json"
    wrappers_path = FOLLOW / "silence-wrappers-v2/report.json"
    batch = load(batch_path, "74519956387cbf06da5a914489a930b803eb0d5169cb1145a7ec821a9eb64626")
    wrappers = load(wrappers_path, "abdc4f7069cbca22fc0838a717467c7e39af23a5ee71a169cfea285cf5af39cd")
    prepared_path, native_path, candidate_path = (checked(batch[key]) for key in ("prepared", "native", "candidate"))
    prepared, native, candidate = map(load, (prepared_path, native_path, candidate_path))
    selection_path = checked(batch["actualProduction96"])
    selection = load(selection_path)
    manifest_path = ROOT / "dist/production-r21-2026-10-08/release-ready-v1/manifest.json"
    manifest = load(manifest_path, "55d5297988c0b134eef4738edf81026ebbbe688c917ad97c6170ff7ef1b59c2e")
    selector_manifest = checked(batch["selectorManifest"])
    require(manifest == load(selector_manifest) and selection["manifestSha256"] == batch["selectorManifest"]["sha256"] and
            selection["requestedStates"] == 96 and len(selection["selections"]) == 96, "Exact actual public1.7.3 selector96/source identity")
    paths = [AREA / "voice-v1/bundle" / ("9ba626afa44a3aa3.patch_0" + suffix) for suffix in ("", ".stream", ".gpu_resources")]
    expected = ("a822e74fd3ce50f92db7855d8a14ccc7ad9d7291c14aefceaf4f8b7843442333", "d73b12f1d18428a5e5270683d5d66c125b522c39108af93499909cb3627ebdae", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
    require(all(pin(path)["sha256"] == checksum for path, checksum in zip(paths, expected, strict=True)), "Fresh candidate triad pins")
    overlay = strict_overlay(*(path.read_bytes() for path in paths))
    require(len(batch["prototypes"]) == 5 and {row["targetMedia"] for row in batch["prototypes"]} == EXPECTED_TARGETS and
            set(overlay) == {typed(row["typedKey"]) for row in batch["prototypes"]} and BANK_KEY not in overlay, "Exact five reviewed keys/no BANK or cache resource")
    decoder = checked(batch["decoder"])
    output.mkdir(parents=True)
    source_by_id = {row["sourceMedia"]: row for row in prepared["recordings"]}
    native_by_id = {row["media"]: row for row in native["completeRecordings"]}
    wrapper_by_id = {row["targetMedia"]: row for row in wrappers["wrappers"]}
    decoded_sources, cue_proofs = {}, []
    for cue in batch["prototypes"]:
        media, source_id = cue["targetMedia"], cue["sourceMedia"]
        source, original = source_by_id[source_id], native_by_id[media]
        if source_id not in decoded_sources:
            source_wem = checked(source["sourceWem"])
            decoded, metadata = decode(decoder, source_wem, output / f"original-{source_id}.wav", source["samples"], False)
            require(decoded == pcm(checked(source["actualPcmDecode"]), source["samples"]) and sha(decoded) == source["pcmSha256"], "Fresh independent original decode exact")
            decoded_sources[source_id] = (decoded, {"sourceMedia": source_id, "wem": pin(source_wem), "decode": pin(output / f"original-{source_id}.wav"), "metadata": metadata})
        source_pcm = decoded_sources[source_id][0]
        frames = original["decoder"]["samples"]
        require(frames == cue["targetAndOutputFrames"] and original["decoder"]["rate"] == cue["rate"] == 48000 and
                original["decoder"]["channels"] == cue["channels"] == 1 and not original["decoder"]["encodedLoop"], "Unchanged native duration/channel/rate/no-loop")
        values = [value[0] for value in struct.iter_unpack("<h", source_pcm)]
        leading = next(i for i, value in enumerate(values) if value)
        trailing = next(i for i, value in enumerate(reversed(values)) if value)
        excess = len(values) - frames
        tail = min(excess, trailing)
        head = excess - tail
        require(excess > 0 and 0 <= head <= leading and (head, tail) ==
                (cue["removedLeadingExactZeroFrames"], cue["removedTrailingExactZeroFrames"]), "Minimal measured exact-zero-only trim")
        wanted = source_pcm[head * 2:len(source_pcm) - tail * 2]
        require(bytes(head * 2) + wanted + bytes(tail * 2) == source_pcm and len(wanted) == frames * 2, "Full original PCM reconstruction/no nonzero or internal samples lost")
        wrapper, wem = overlay[typed(cue["typedKey"])]
        prior = bytes.fromhex(cue["currentTargetWrapperHex"])
        wrapper_report = wrapper_by_id[media]
        require(wrapper == checked(wrapper_report["wrapper"]).read_bytes() and wrapper[:8] == prior[:8] and
                len(wrapper) == len(prior) == 12 and struct.unpack_from("<I", wrapper, 8)[0] == len(wem), "Actual serialized wrapper only length DWORD changed")
        prototype_ref = next(row for row in cue["files"] if row["path"].endswith(".wem"))
        require(wem == checked(prototype_ref).read_bytes() and wrapper_report["prototypeWem"] == prototype_ref, "Actual serialized WEM exact sealed prototype")
        chunks = wav_chunks(wem)
        require(chunks[b"fmt "].hex() == source["fmtHex"] and chunks[b"data"] == wanted and sha(wanted) == cue["remainingPcmSha256"], "Envelope and all kept source samples exact")
        wem_path = output / f"serialized-{media}.wem"
        wem_path.write_bytes(wem)
        decoded, metadata = decode(decoder, wem_path, output / f"serialized-{media}.wav", frames, True)
        require(decoded == wanted, "Fresh independently serialized WEM decode byte exact")
        cue_proofs.append({"media": media, "typedKey": cue["typedKey"], "sourceMedia": source_id, "sourcePersona": cue["sourcePersona"],
                           "sourceFrames": len(values), "outputFrames": frames, "removedLeadingExactZeros": head, "removedTrailingExactZeros": tail,
                           "onsetAdvanceMilliseconds": head / 48, "wrapperPrefixExact": True, "sourceReconstructionExact": True,
                           "outputPcmSha256": sha(decoded), "serializedWem": pin(wem_path), "freshDecode": pin(output / f"serialized-{media}.wav"), "metadata": metadata})
    catalog = {row["sha256"]: row for row in batch["freshSelectedAssets"]}
    checked_assets, parsed, joins = {}, {}, []
    wanted_keys = set(overlay) | {BANK_KEY}
    file_sets = sorted({state["fileSet"] for state in selection["selections"] if state["effectiveOptions"]["empire"]})
    require(len(file_sets) == 8, "Eight actual Empire effective sets")
    for set_id in file_sets:
        ordered = selection["fileSets"][set_id]
        by_name = {row["name"]: row for row in ordered}
        anchor = [index for index, row in enumerate(ordered) if row["sha256"] == candidate["currentSourceMain"]["sha256"]]
        require(len(anchor) == 1, "Known complete target provider selected exactly once")
        winners = {}
        for item in ordered[anchor[0]:]:
            if not re.fullmatch(r"[a-f0-9]{16}\.patch_\d+", item["name"]):
                continue
            checksum = item["sha256"]
            if checksum not in parsed:
                require(checksum in catalog and catalog[checksum]["bytes"] == item["size"], "Bounded selected MAIN catalog entry")
                source_path = checked(catalog[checksum])
                checked_assets[checksum] = catalog[checksum]
                parsed[checksum] = directory(source_path.read_bytes())
            if item is ordered[anchor[0]]:
                require(wanted_keys <= parsed[checksum].keys(), "Complete anchor provides every target and BANK; earlier entries cannot win")
            for key in wanted_keys & parsed[checksum].keys():
                winners[key] = item
        require(winners.keys() == wanted_keys, "All six actual typed winners resolved")
        records = []
        for key, item in sorted(winners.items()):
            row = parsed[item["sha256"]][key]
            main = Path(catalog[item["sha256"]]["path"]).read_bytes()
            before_main = main[row[2]:row[2] + row[7]]
            before_stream = b""
            if row[8]:
                stream_item = by_name[item["name"] + ".stream"]
                reference = catalog[stream_item["sha256"]]
                require(reference["bytes"] == stream_item["size"], "Exact active STREAM companion size")
                if stream_item["sha256"] not in checked_assets:
                    checked(reference)
                    checked_assets[stream_item["sha256"]] = reference
                with Path(reference["path"]).open("rb") as handle:
                    handle.seek(row[3])
                    before_stream = handle.read(row[8])
                require(len(before_stream) == row[8], "Complete active target STREAM extent")
            if key == BANK_KEY:
                require(sha(before_main) == batch["currentBankSha256"], "Entire active BANK/HIRC/cache byte exact")
                contracts = sound_contracts(before_main)
                require(len(contracts) == 644, "All644 native nonverbal Sound contracts retained")
                for cue in batch["prototypes"]:
                    require([contracts[contract["sound"]] for contract in cue["unchangedSoundContracts"]] == cue["unchangedSoundContracts"] and
                            all(contract["codec"] == 0x10001 and contract["mode"] == 2 and contract["memory"] == 9728 and
                                contract["media"] == cue["targetMedia"] for contract in cue["unchangedSoundContracts"]), "Unchanged exact Sound source18, mode2 PCM, cache identity and 9728-byte initial read")
            else:
                cue = next(cue for cue in batch["prototypes"] if typed(cue["typedKey"]) == key)
                require(before_main.hex() == cue["currentTargetWrapperHex"] and sha(before_stream) == cue["currentTargetWemSha256"] and
                        len(before_main) == 12 and struct.unpack_from("<I", before_main, 8)[0] == len(before_stream) and not row[9], "Actual public target winner joins exact")
                require(overlay[key] != (before_main, before_stream), "Each of five resources is replaced")
            records.append({"typedKey": f"{key[0]:016x}.{key[1]:016x}", "winner": item["name"], "providerSha256": item["sha256"],
                            "mainPayloadSha256": sha(before_main), "streamPayloadSha256": sha(before_stream)})
        joins.append({"fileSet": set_id, "profiles": sorted({state["profile"] for state in selection["selections"] if state["fileSet"] == set_id}),
                      "actualWinners": records, "changedTypedKeys": sorted(f"{key[0]:016x}.{key[1]:016x}" for key in overlay), "bankHircCacheExact": True,
                      "appendOverlayCannotReplaceAnyOtherKey": True})
    require(len(decoded_sources) == 4 and all(pin(Path(reference["path"])) == reference for reference in checked_assets.values()), "Four originals decoded and bounded existing files unchanged")
    report = {"passed": True, "tool": pin(Path(__file__)), "batch": pin(batch_path), "wrappers": pin(wrappers_path), "prepared": pin(prepared_path),
              "native": pin(native_path), "candidateSources": pin(candidate_path), "actualPublicProduction96": pin(selection_path), "publicManifest": pin(manifest_path),
              "decoder": pin(decoder), "rawCandidateFiles": list(map(pin, paths)), "rawArchiveParsedIndependently": True,
              "builderParsedRecordsOrCodeUsed": False, "resourceCount": 5, "sourceDecodes": [item[1] for item in decoded_sources.values()], "cues": cue_proofs,
              "allEightActualEmpireWinnerJoins": joins, "freshBoundedExistingFiles": list(checked_assets.values()),
              "allFiveSerializedWemsFreshlyDecoded": True, "exactZeroOnlyOriginalReconstruction": True, "soundBankHircCacheContractsUnchanged": True,
              "allExistingSelectionsPreservedByAppendOnlyScope": True, "gameSettingsCacheOrExistingArchiveChanged": False,
              "enginePlaybackMixOrTimingAccepted": False,
              "limits": ["No gain, filtering, pitch, stretching or sample-rate change. Five onset advances are intentional exact-zero removal.",
                         "This independent archive/decode proof does not establish native event timing, sound playback or perceived voice mix."]}
    report_path = output / "report.json"
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return pin(report_path)


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=Path, default=AREA / "qa/voice-peer-v1")
    logging.basicConfig(level=logging.INFO)
    try:
        print(json.dumps(run(parser.parse_args(argv).out)))
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, struct.error, subprocess.CalledProcessError):
        logging.exception("Independent r22 voice review failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
