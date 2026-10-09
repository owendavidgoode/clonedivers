#!/usr/bin/env python3
"""Integrate five reviewed exact-zero-trim voice cues into a bounded overlay.

Uses the pinned primary SDK MAIN serializer, deriving only the resource type and
STREAM directory fields it does not support. No existing archive, BANK, manifest,
settings, cache or installed file is modified. Playback remains unaccepted.
"""
from __future__ import annotations

import argparse
from collections.abc import Sequence
import hashlib
import json
import logging
from pathlib import Path
import re
import runpy
import struct
import subprocess
import sys
from typing import Any
import wave

ROOT = Path(__file__).resolve().parents[1]
PUBLIC = ROOT / "dist/production-r21-2026-10-08"
FOLLOW = ROOT / "dist/empire-offline-followup-2026-10-08/voice"
SDK = ROOT / "dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d/sdk/tools/hd2_archive.py"
STREAM = 0x504B55235D21440E
BANK_KEY = (0xB7CC016F2537E3D3, 0x535A7BD3E650D799)
ROW = struct.Struct("<7Q6I")
TARGETS = {598236107, 774990988, 631423309, 975870236, 367932016}


def require(condition: Any, message: str) -> None:
    if not condition:
        raise ValueError(message)


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def pin(path: Path) -> dict[str, Any]:
    before = path.stat()
    require(path.is_file() and not path.is_symlink(), f"Regular source required: {path}")
    with path.open("rb") as handle:
        checksum = hashlib.file_digest(handle, "sha256").hexdigest()
    after = path.stat()
    require((before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns), "Input changed during hash")
    return {"path": str(path.resolve()), "bytes": after.st_size, "sha256": checksum}


def checked(reference: dict[str, Any]) -> Path:
    path = Path(reference["path"])
    require(pin(path) == reference, f"Sealed source drift: {path}")
    return path


def load(path: Path, expected: str) -> tuple[dict[str, Any], dict[str, Any]]:
    receipt = pin(path)
    require(receipt["sha256"] == expected, f"Proof drift: {path}")
    return json.loads(path.read_text(encoding="utf-8-sig")), receipt


def typed(text: str) -> tuple[int, int]:
    require(re.fullmatch(r"[a-f0-9]{16}\.[a-f0-9]{16}", text), "Canonical typed identity")
    identity, kind = text.split(".")
    return int(identity, 16), int(kind, 16)


def pcm(path: Path, frames: int) -> bytes:
    with wave.open(str(path), "rb") as handle:
        require(handle.getparams()[:4] == (1, 2, 48000, frames), "Exact native mono48k PCM16 frame extent")
        raw = handle.readframes(frames + 1)
    require(len(raw) == frames * 2, "Complete PCM sample extent")
    return raw


def decode(decoder: Path, source: Path, target: Path, frames: int, pcm16: bool) -> tuple[bytes, str]:
    metadata = subprocess.run([str(decoder), "-m", str(source)], capture_output=True, text=True, check=True).stdout
    match = re.search(r"stream total samples: (\d+)", metadata)
    require(match is not None and int(match.group(1)) == frames and "channels: 1" in metadata and
            "sample rate: 48000 Hz" in metadata and "loop start:" not in metadata, "Fresh decoder exact rate/frame/channel/no-loop contract")
    if pcm16:
        require("16-bit Little Endian PCM" in metadata, "Fresh WEM PCM codec contract")
    subprocess.run([str(decoder), "-i", "-o", str(target), str(source)], capture_output=True, text=True, check=True)
    return pcm(target, frames), metadata


def riff(data: bytes) -> dict[bytes, bytes]:
    require(data[:4] == b"RIFF" and data[8:12] == b"WAVE" and len(data) == 8 + struct.unpack_from("<I", data, 4)[0], "RIFF declared extent")
    chunks: dict[bytes, bytes] = {}
    offset = 12
    while offset < len(data):
        require(offset + 8 <= len(data), "RIFF chunk header bounds")
        tag, size = struct.unpack_from("<4sI", data, offset)
        require(tag not in chunks and offset + 8 + size <= len(data), "RIFF unique bounded chunk")
        chunks[tag] = data[offset + 8:offset + 8 + size]
        offset += 8 + size + (size & 1)
    require(offset == len(data), "Complete RIFF traversal")
    return chunks


def raw_archive(main: bytes, stream: bytes) -> dict[tuple[int, int], tuple[bytes, bytes, bytes]]:
    magic, types, count = struct.unpack_from("<III", main)
    require(magic == 0xF0000011 and types == 1 and count == 5 and struct.unpack_from("<Q", main, 32)[0] == len(main), "Exact complete five-resource archive")
    require(struct.unpack_from("<Q", main, 80)[0] == STREAM and struct.unpack_from("<I", main, 88)[0] == count, "Actual directory STREAM type/count")
    result = {}
    ranges: list[list[tuple[int, int]]] = [[], []]
    for index in range(count):
        row = ROW.unpack_from(main, 104 + ROW.size * index)
        key = row[:2]
        require(key not in result and row[1] == STREAM and row[7] == 12 and row[9] == 0 and row[12] == index, "Unique native STREAM wrapper/ordinal/noGPU")
        require(row[2] >= 104 + ROW.size * count and row[2] % 16 == 0 and row[3] % 16 == 0 and
                row[2] + row[7] <= len(main) and row[3] + row[8] <= len(stream), "MAIN/STREAM ranges and allocation alignment")
        for part, offset, length in ((0, row[2], row[7]), (1, row[3], row[8])):
            require(all(offset + length <= lo or offset >= hi for lo, hi in ranges[part]), "Resource payloads do not overlap")
            ranges[part].append((offset, offset + length))
        result[key] = (main[row[2]:row[2] + row[7]], stream[row[3]:row[3] + row[8]], b"")
    return result


def serialize(writer: dict[str, Any], replacements: dict[tuple[int, int], tuple[bytes, bytes]]) -> tuple[bytes, bytes, bytes]:
    require(len(replacements) == 5 and all(key[1] == STREAM for key in replacements), "Only five STREAM replacements")
    # The unmodified SDK supplies MAIN header, entry layout, ordinals and padding.
    base = writer["make_archive"]({key[0]: value[0] for key, value in replacements.items()})
    main, stream = bytearray(base), bytearray()
    require(struct.unpack_from("<Q", main, 80)[0] == writer["LUA_TYPE"], "Pinned SDK LUA type before bounded adaptation")
    struct.pack_into("<Q", main, 80, STREAM)
    for index, key in enumerate(sorted(replacements)):
        offset = 104 + ROW.size * index
        row = list(ROW.unpack_from(main, offset))
        require(row[0] == key[0] and row[1] == writer["LUA_TYPE"] and row[7] == 12, "Exact SDK generated resource identity")
        stream.extend(bytes(-len(stream) % 16))
        row[1], row[3], row[8] = STREAM, len(stream), len(replacements[key][1])
        ROW.pack_into(main, offset, *row)
        stream.extend(replacements[key][1])
    parsed = raw_archive(bytes(main), bytes(stream))
    require(set(parsed) == set(replacements) and all(parsed[key] == (*value, b"") for key, value in replacements.items()), "Independent raw roundtrip of every payload")
    # Restore only the intentionally adapted directory fields; SDK bytes exact.
    restored = bytearray(main)
    struct.pack_into("<Q", restored, 80, writer["LUA_TYPE"])
    for index in range(5):
        offset = 104 + ROW.size * index
        row = list(ROW.unpack_from(restored, offset))
        row[1], row[3], row[8] = writer["LUA_TYPE"], 0, 0
        ROW.pack_into(restored, offset, *row)
    require(bytes(restored) == base, "All primary SDK bytes exact outside type/STREAM offset/length adaptation")
    return bytes(main), bytes(stream), b""


def run(out: Path) -> dict[str, Any]:
    require(out.resolve().is_relative_to(ROOT / "dist/production-r22-2026-10-08") and not out.exists(), "Fresh scoped candidate output required")
    batch, batch_pin = load(FOLLOW / "silence-batch-v1/report.json", "74519956387cbf06da5a914489a930b803eb0d5169cb1145a7ec821a9eb64626")
    wrappers, wrappers_pin = load(FOLLOW / "silence-wrappers-v2/report.json", "abdc4f7069cbca22fc0838a717467c7e39af23a5ee71a169cfea285cf5af39cd")
    peer, peer_pin = load(FOLLOW / "silence-peer-v1/report.json", "d88dee56ddda7ff1f6cbb225bc00e78cda5e96c55136ce675bb968a91a7e7187")
    public, public_pin = load(PUBLIC / "release-ready-v1/manifest.json", "55d5297988c0b134eef4738edf81026ebbbe688c917ad97c6170ff7ef1b59c2e")
    selected, selected_pin = load(Path(batch["actualProduction96"]["path"]), batch["actualProduction96"]["sha256"])
    selector_manifest = json.loads(checked(batch["selectorManifest"]).read_text(encoding="utf-8-sig"))
    require(public == selector_manifest and selected["manifestSha256"] == batch["selectorManifest"]["sha256"] and selected["requestedStates"] == 96,
            "Actual production96 semantic public manifest identity")
    prepared = json.loads(checked(batch["prepared"]).read_text(encoding="utf-8-sig"))
    original = json.loads(checked(batch["native"]).read_text(encoding="utf-8-sig"))
    candidate = json.loads(checked(batch["candidate"]).read_text(encoding="utf-8-sig"))
    decoder = checked(batch["decoder"])
    parser_path = checked(batch["rawParser"])
    parser = runpy.run_path(str(parser_path))
    require(pin(SDK)["sha256"] == "49050160f17d25f4838652071811d65311f8f3e84eb47f795bf3a573f43ba66d", "Primary SDK writer pin")
    writer = runpy.run_path(str(SDK))
    require(batch["passed"] and wrappers["passed"] and peer["passed"] and batch["prototypeCount"] == 5 and
            {row["targetMedia"] for row in batch["prototypes"]} == TARGETS and len(wrappers["wrappers"]) == 5, "Exact reviewed five-cue source scope")
    out.mkdir(parents=True)
    sources = {row["sourceMedia"]: row for row in prepared["recordings"]}
    natives = {row["media"]: row for row in original["completeRecordings"]}
    wrapper_by_id = {row["targetMedia"]: row for row in wrappers["wrappers"]}
    replacements, rows, source_decodes = {}, [], {}
    for prototype in batch["prototypes"]:
        media, source_id = prototype["targetMedia"], prototype["sourceMedia"]
        source, native = sources[source_id], natives[media]
        original_pcm = pcm(checked(source["actualPcmDecode"]), source["samples"])
        if source_id not in source_decodes:
            source_wem = checked(source["sourceWem"])
            decoded, metadata = decode(decoder, source_wem, out / f"source-{source_id}.wav", source["samples"], False)
            require(decoded == original_pcm, "Fresh original source Vorbis decode byte-exact")
            source_decodes[source_id] = {"sourceMedia": source_id, "wem": pin(source_wem), "freshDecode": pin(out / f"source-{source_id}.wav"), "metadata": metadata}
        first = next(index for index, value in enumerate(struct.iter_unpack("<h", original_pcm)) if value[0] != 0)
        samples = [value[0] for value in struct.iter_unpack("<h", original_pcm)]
        last = len(samples) - 1 - next(index for index, value in enumerate(reversed(samples)) if value != 0)
        total = source["samples"] - prototype["targetAndOutputFrames"]
        tail = min(total, len(samples) - last - 1)
        head = total - tail
        require(total > 0 and 0 <= head <= first and (head, tail) == (prototype["removedLeadingExactZeroFrames"], prototype["removedTrailingExactZeroFrames"]), "Independently measured minimal edge-zero-only trim")
        end = len(original_pcm) - 2 * tail
        kept = original_pcm[2 * head:end]
        require(original_pcm[:2 * head] == bytes(2 * head) and original_pcm[end:] == bytes(2 * tail) and
                bytes(2 * head) + kept + bytes(2 * tail) == original_pcm and digest(kept) == prototype["remainingPcmSha256"], "Complete original source reconstruction and every nonzero/intervening byte retained")
        frames = prototype["targetAndOutputFrames"]
        require(prototype["rate"] == native["decoder"]["rate"] == 48000 and prototype["channels"] == native["decoder"]["channels"] == 1 and
                native["decoder"]["samples"] == frames and native["decoder"]["encodedLoop"] is False and len(kept) == frames * 2, "Original native duration/rate/channel/no-loop retained")
        family = "fight_take_sml" if prototype["authorGroup"] == "13" else "fight_give"
        require(prototype["sourcePersona"] in source["sourcePersonas"] and any(family in value for value in source["sourceCategories"]), "Reviewed persona/action category retained")
        wem_ref = next(row for row in prototype["files"] if row["path"].endswith(".wem"))
        wem_path = checked(wem_ref)
        wem = wem_path.read_bytes()
        chunks = riff(wem)
        require(set(chunks) == {b"fmt ", b"data"} and chunks[b"fmt "].hex() == source["fmtHex"] and chunks[b"data"] == kept and len(wem) == 52 + 2 * frames and len(wem) >= 9728, "Exact minimal PCM envelope/sample bytes/native initial read")
        decoded, metadata = decode(decoder, wem_path, out / f"prototype-{media}.wav", frames, True)
        require(decoded == kept, "All five fresh prototype WEM decodes exact")
        wrapper_row = wrapper_by_id[media]
        new_wrapper = checked(wrapper_row["wrapper"]).read_bytes()
        before = bytes.fromhex(prototype["currentTargetWrapperHex"])
        require(len(new_wrapper) == len(before) == 12 and new_wrapper[:8] == before[:8] and struct.unpack_from("<I", new_wrapper, 8)[0] == len(wem) and
                wrapper_row["prototypeWem"] == wem_ref, "Only wrapper length DWORD changes")
        key = typed(prototype["typedKey"])
        require(key == typed(native["typedKey"]) and key[1] == STREAM and key not in replacements, "Exact unique native typed STREAM key")
        replacements[key] = (new_wrapper, wem)
        rows.append({"targetMedia": media, "typedKey": prototype["typedKey"], "sourceMedia": source_id,
                     "persona": prototype["sourcePersona"], "authorGroup": prototype["authorGroup"], "voiceSlot": prototype["voiceSlot"],
                     "sourceCategories": source["sourceCategories"], "nativeSounds": native["sounds"], "nativeEvents": native["events"],
                     "sourceFrames": source["samples"], "nativeAndOutputFrames": frames, "rate": 48000, "channels": 1,
                     "leadingExactZeros": first, "trailingExactZeros": len(samples) - last - 1, "removedLeadingZeros": head, "removedTrailingZeros": tail,
                     "onsetAdvanceMilliseconds": head / 48, "originalReconstructionExact": True, "allNonzeroAndInterveningSamplesExact": True,
                     "originalPcmSha256": digest(original_pcm), "outputPcmSha256": digest(kept), "prototypeWem": wem_ref,
                     "freshPrototypeDecode": pin(out / f"prototype-{media}.wav"), "freshPrototypeMetadata": metadata,
                     "oldWrapperHex": before.hex(), "newWrapperHex": new_wrapper.hex(), "wrapperFirstEightBytesExact": True})
    blobs = serialize(writer, replacements)
    bundle = out / "bundle"
    bundle.mkdir()
    files = []
    for suffix, blob in zip(("", ".stream", ".gpu_resources"), blobs, strict=True):
        path = bundle / ("9ba626afa44a3aa3.patch_0" + suffix)
        path.write_bytes(blob)
        files.append(pin(path))
    serialized = raw_archive(blobs[0], blobs[1])
    for row in rows:
        media = row["targetMedia"]
        payload = serialized[typed(row["typedKey"])][1]
        serialized_wem = out / f"serialized-{media}.wem"
        serialized_wem.write_bytes(payload)
        decoded, metadata = decode(decoder, serialized_wem, out / f"serialized-{media}.wav", row["nativeAndOutputFrames"], True)
        require(digest(decoded) == row["outputPcmSha256"], "All five serialized STREAM WEM decodes independently exact")
        row["serializedWem"], row["freshSerializedDecode"] = pin(serialized_wem), pin(out / f"serialized-{media}.wav")
        row["freshSerializedMetadata"] = metadata
    # Resolve the actual last typed winners, not a guessed historical archive.
    assets = {row["sha256"]: row for row in batch["freshSelectedAssets"]}
    checked_assets, parsed = {}, {}
    all_keys = set(replacements) | {BANK_KEY}
    file_sets = sorted({row["fileSet"] for row in selected["selections"] if row["effectiveOptions"]["empire"]})
    require(len(file_sets) == 8, "All eight actual Empire Full/Lighter effective sets")
    winner_proofs = []
    for set_id in file_sets:
        ordered = selected["fileSets"][set_id]
        by_name = {row["name"]: row for row in ordered}
        positions = [index for index, row in enumerate(ordered) if row["sha256"] == candidate["currentSourceMain"]["sha256"]]
        require(len(positions) == 1, "Reviewed complete target source selected once")
        winners = {}
        for item in ordered[positions[0]:]:
            if not re.fullmatch(r"[a-f0-9]{16}\.patch_\d+", item["name"]):
                continue
            sha = item["sha256"]
            if sha not in parsed:
                require(sha in assets, "All bounded winner providers covered by sealed batch")
                source_path = checked(assets[sha])
                require(assets[sha]["bytes"] == item["size"], "Actual selected MAIN size")
                checked_assets[sha] = assets[sha]
                parsed[sha] = parser["slim"](source_path.read_bytes())
            for key in all_keys & parsed[sha].keys():
                winners[key] = item
        require(set(winners) == all_keys, "Five actual current winners and BANK present")
        records = []
        before_payloads = {}
        for key in sorted(winners):
            item = winners[key]
            resource = parsed[item["sha256"]][key]
            wrapper, payload = resource["parts"][0], b""
            if resource["row"][8]:
                stream_row = by_name[item["name"] + ".stream"]
                sha = stream_row["sha256"]
                require(sha in assets, "Winning STREAM provider reviewed")
                stream_path = checked(assets[sha]) if sha not in checked_assets else Path(checked_assets[sha]["path"])
                require(assets[sha]["bytes"] == stream_row["size"], "Actual winning STREAM size")
                checked_assets[sha] = assets[sha]
                with stream_path.open("rb") as handle:
                    handle.seek(resource["row"][3])
                    payload = handle.read(resource["row"][8])
                require(len(payload) == resource["row"][8], "Complete selected WEM extent")
            if key == BANK_KEY:
                require(digest(wrapper) == batch["currentBankSha256"], "All eight BANK winners byte-exact; entire graph and active cache untouched")
                declarations = parser["sounds"](parser["bank_chunks"](wrapper)[b"HIRC"][1])
                require(len(declarations) == 644, "644 native nonverbal Sound contracts retained")
                for row in rows:
                    actual = [declarations[sound] for sound in row["nativeSounds"]]
                    prototype = next(value for value in batch["prototypes"] if value["targetMedia"] == row["targetMedia"])
                    require(actual == prototype["unchangedSoundContracts"] and all(value["codec"] == 0x10001 and value["mode"] == 2 and value["media"] == row["targetMedia"] and value["memory"] == 9728 for value in actual), "Actual unchanged PCM mode2/9728 contracts")
            else:
                row = next(value for value in rows if typed(value["typedKey"]) == key)
                prototype = next(value for value in batch["prototypes"] if value["targetMedia"] == row["targetMedia"])
                require(wrapper.hex() == row["oldWrapperHex"] and digest(payload) == prototype["currentTargetWemSha256"] and
                        len(wrapper) == 12 and struct.unpack_from("<I", wrapper, 8)[0] == len(payload) and not resource["row"][9], "Actual r21 target wrapper/WEM/noGPU exact")
            before_payloads[key] = (wrapper, payload, b"")
            records.append({"typedKey": f"{key[0]:016x}.{key[1]:016x}", "winner": item["name"], "archiveSha256": item["sha256"], "mainPayloadSha256": digest(wrapper), "streamPayloadSha256": digest(payload)})
        # An append-only overlay has exactly these keys; it cannot contribute a
        # different resource payload for any other typed identity in any set.
        after = dict(before_payloads)
        after.update(serialized)
        require({key for key in before_payloads if before_payloads[key] != after[key]} == set(replacements) and BANK_KEY not in serialized, "Exactly five typed resource payload changes; BANK unchanged")
        winner_proofs.append({"fileSet": set_id, "profile": sorted({row["profile"] for row in selected["selections"] if row["fileSet"] == set_id}),
                              "existingSelectionRows": len(ordered), "originalPhysicalFileRowsRetainedExact": True,
                              "changedTypedKeys": sorted(f"{key[0]:016x}.{key[1]:016x}" for key in replacements), "actualR21Winners": records,
                              "allOtherTypedResourcesExactByAppendOnlyOverlay": True, "bankAndHircAndActiveCacheByteExact": True})
    require(len(source_decodes) == 4, "Four unique original source recordings independently decoded")
    # Fresh rehashes establish no mutation of bounded source archives/proofs.
    require(all(pin(Path(row["path"])) == row for row in checked_assets.values()), "Every bounded existing archive unchanged after integration")
    report = {"passed": True, "tool": pin(Path(__file__)), "sealedPublicManifest": public_pin, "actualProduction96": selected_pin,
              "batch": batch_pin, "wrappers": wrappers_pin, "priorIndependentPeer": peer_pin, "primarySdkWriter": pin(SDK),
              "primaryWriterAdaptation": "Unmodified primary make_archive emits MAIN header, padded 12-byte wrappers and entry ordinals. Only type-directory type, entry type, STREAM offset and STREAM length fields are adapted; restoring those fields reproduces primary SDK bytes exactly.",
              "decoder": pin(decoder), "independentRawParser": pin(parser_path), "files": files, "resourceCount": 5,
              "typedKeys": sorted(f"{key[0]:016x}.{key[1]:016x}" for key in replacements), "sourceDecodes": list(source_decodes.values()), "cues": rows,
              "actualEmpireWinnerProofs": winner_proofs, "freshBoundedExistingAssets": list(checked_assets.values()),
              "allFiveOriginalAndSerializedWemFreshDecodesExact": True, "allMode2Pcm9728ContractsExact": True,
              "allEightEmpireSetsExactlyFiveOverlayReplacements": True, "noBankHircOrActiveCacheResourceEmitted": True,
              "allExistingArchiveBytesUnchanged": True, "allOtherResourceBytesExactByOverlayScope": True,
              "authenticNonverbalCuesBefore": 303, "authenticNonverbalCuesAfter": 308, "helmetFilteredNativeBefore": 341, "helmetFilteredNativeAfter": 336,
              "totalNonverbalCues": 644, "priorAuthoredSpokenAssignmentsUnchanged": 217,
              "policy": batch["newTechnicalPolicy"], "remainingUnfitShortTargets": 20,
              "readyForComposition": True, "independentIntegrationReviewPending": True, "readyForDeployment": False,
              "enginePlaybackAndMixAccepted": False, "gameSettingsManifestOrDeploymentChanged": False,
              "limits": ["The five reported onset advances are intentional edge-zero removal, not a claim of perceptual equivalence.",
                         "Archive overlay semantics are checked against production selections; native playback, event timing and voice mix require playtest.",
                         "No BANK/HIRC, mode1 prefetch, gain, filtering, pitch, stretching, or sample-rate change is introduced."]}
    path = out / "report.json"
    path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return {"passed": True, "report": pin(path), "files": files, "authenticAfter": 308, "remainingFiltered": 336, "EmpireEffectiveSets": 8}


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=Path, default=ROOT / "dist/production-r22-2026-10-08/voice-v1")
    logging.basicConfig(level=logging.INFO)
    try:
        print(json.dumps(run(parser.parse_args(argv).out), indent=2))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception("Bounded r22 voice integration failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
