#!/usr/bin/env python3
"""Independent raw archive/BANK/media preservation check of additive Yoda audio."""
from __future__ import annotations

import hashlib
import io
import json
from pathlib import Path
import struct
from typing import Any
import wave

ROOT = Path(__file__).resolve().parents[1]
AREA = ROOT / "dist/empire-yoda-death-2026-10-08"
IDS = {"event": 4055635130, "action": 1795825027, "sound": 1304541750,
       "mixer": 3967146992, "media": 2322503283, "cache": 364695607}


def need(condition: Any, message: str) -> None:
    if not condition:
        raise ValueError(message)


def pin(path: Path) -> dict[str, Any]:
    with path.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": digest}


def checked(item: dict[str, Any]) -> Path:
    path = Path(item["path"])
    need(pin(path) == item, f"Changed pinned source: {path}")
    return path


def archive(raw: bytes, singleton: bool = False) -> bytes:
    need(raw[:4] == bytes.fromhex("110000f0"), "Native archive magic")
    types, count = struct.unpack_from("<II", raw, 4)
    need(0 < types < 100 and 0 < count < 10000, "Archive type/file table bounds")
    if singleton:
        need(types == count == 1, "Candidate contains one typed BANK only")
        need(struct.unpack_from("<Q", raw, 80)[0] == 0x535A7BD3E650D799, "Candidate BANK type table")
    selected = []
    for i in range(count):
        row = struct.unpack_from("<7Q6I", raw, 72 + types * 32 + i * 80)
        need(row[2] + row[7] <= len(raw), "Complete MAIN resource")
        if row[:2] == (0xB7CC016F2537E3D3, 0x535A7BD3E650D799):
            need(row[8] == row[9] == 0, "Common BANK has no external archive companions")
            selected.append(raw[row[2]:row[2] + row[7]])
    need(len(selected) == 1, "Unique target common voice BANK")
    return selected[0]


def chunks(raw: bytes, riff: bool = False) -> dict[bytes, bytes]:
    if riff:
        need(raw[:4] == b"RIFF" and raw[8:12] == b"WAVE" and struct.unpack_from("<I", raw, 4)[0] + 8 == len(raw), "WEM RIFF extent")
        at = 12
    else:
        need(struct.unpack_from("<I", raw, 4)[0] + 16 == len(raw), "BANK wrapper length")
        at = 16
    result = {}
    while at < len(raw):
        need(at + 8 <= len(raw), "Bounded chunk header")
        tag, size = struct.unpack_from("<4sI", raw, at)
        need(tag not in result and at + 8 + size <= len(raw), "Unique bounded chunk payload")
        result[tag] = raw[at + 8:at + 8 + size]
        at += 8 + size + (size % 2 if riff else 0)
    need(at == len(raw), "Exact chunk tail")
    return result


def objects(data: bytes) -> list[tuple[int, int, bytes]]:
    need(len(data) >= 4, "HIRC count")
    result, at, seen = [], 4, set()
    for _ in range(struct.unpack_from("<I", data)[0]):
        need(at + 9 <= len(data), "HIRC object header")
        kind, size, identity = struct.unpack_from("<BII", data, at)
        need(size >= 4 and at + size + 5 <= len(data) and identity not in seen, "HIRC unique complete object")
        result.append((identity, kind, data[at + 9:at + size + 5]))
        seen.add(identity)
        at += size + 5
    need(at == len(data), "HIRC complete tail")
    return result


def media(parts: dict[bytes, bytes]) -> dict[int, bytes]:
    need(len(parts[b"DIDX"]) % 12 == 0, "DIDX complete row table")
    result = {}
    for identity, offset, size in struct.iter_unpack("<III", parts[b"DIDX"]):
        need(identity not in result and offset + size <= len(parts[b"DATA"]), "Unique media and bounded resident DATA")
        result[identity] = parts[b"DATA"][offset:offset + size]
    return result


def validate(before_raw: bytes, after_raw: bytes, pcm: bytes) -> dict[str, Any]:
    before, after = chunks(before_raw), chunks(after_raw)
    need(list(before) == list(after), "Original BANK chunk order/types preserved")
    need(before_raw[:4] == after_raw[:4] and before_raw[8:16] == after_raw[8:16], "Native BANK wrapper identity preserved")
    for tag in before:
        if tag not in (b"HIRC", b"DIDX", b"DATA"):
            need(before[tag] == after[tag], "Original non-HIRC/media chunk changed")
    old, new = objects(before[b"HIRC"]), objects(after[b"HIRC"])
    need(new[:len(old)] == old and len(new) == len(old) + 4, "All old HIRC records and order preserved; exactly four additions")
    need(after[b"HIRC"][4:len(before[b"HIRC"])] == before[b"HIRC"][4:], "Original serialized HIRC bytes preserved")
    original = {key: (kind, body) for key, kind, body in old}
    additions = {key: (kind, body) for key, kind, body in new[len(old):]}
    need(set(additions) == {IDS[name] for name in ("event", "action", "sound", "mixer")}, "Exact four new graph nodes")
    need(additions[IDS["event"]] == (4, b"\1" + struct.pack("<I", IDS["action"])), "Deterministic single action event")
    action = additions[IDS["action"]]
    template_action = original[1032286481]
    need(template_action[0] == action[0] == 3 and action[1] == template_action[1][:2] + struct.pack("<I", IDS["sound"]) + template_action[1][6:], "Native immediate Play Action exact except dedicated sound target")
    need(struct.unpack_from("<H", action[1])[0] == 0x0403, "Native Play action type")
    sound = additions[IDS["sound"]]
    template_sound = original[67848527]
    need(sound[0] == template_sound[0] == 2 and len(sound[1]) == len(template_sound[1]), "Native minimal Sound record layout")
    need(sound[1][18:26] == template_sound[1][18:26] and sound[1][30:] == template_sound[1][30:], "Sound native base parameters exact outside source and parent")
    codec, mode, source, cache, size, flags = struct.unpack_from("<IBIIIB", sound[1])
    need((codec, mode, source, cache, flags) == (0x10001, 0, IDS["media"], IDS["cache"], 0), "Language-neutral fully resident PCM Sound")
    need(struct.unpack_from("<I", sound[1], 26)[0] == IDS["mixer"], "Dedicated new Sound parent")
    mixer = additions[IDS["mixer"]]
    template_mixer = original[936212895]
    need(mixer[0] == template_mixer[0] == 7 and mixer[1][-8:] == struct.pack("<II", 1, IDS["sound"]), "Dedicated mixer has one child")
    prefix = mixer[1][:-8]
    need(template_mixer[1].startswith(prefix), "Native root mixer parameters exact")
    children = struct.unpack_from("<I", template_mixer[1], len(prefix))[0]
    need(len(template_mixer[1]) == len(prefix) + 4 + children * 4, "Only native mixer child table changed")
    need(struct.unpack_from("<II", prefix, 4) == (2467607253, 0), "Existing Init voice output bus; parent zero")
    need(after[b"DIDX"].startswith(before[b"DIDX"]) and len(after[b"DIDX"]) == len(before[b"DIDX"]) + 12, "Original DIDX table exact plus one media entry")
    need(after[b"DATA"].startswith(before[b"DATA"]), "All original resident media bytes exact")
    old_media, new_media = media(before), media(after)
    need(set(new_media) == set(old_media) | {IDS["media"]} and all(new_media[key] == val for key, val in old_media.items()), "Original media IDs/content preserved plus one source")
    wem = new_media[IDS["media"]]
    need(size == len(wem), "Sound memory size exactly new resident WEM")
    wem_parts = chunks(wem, True)
    need(list(wem_parts) == [b"fmt ", b"hash", b"data"], "Minimal WEM format/hash/PCM only")
    need(wem_parts[b"data"] == pcm, "Original classic Yoda PCM samples exact")
    need(struct.unpack_from("<HHIIHHH", wem_parts[b"fmt "]) == (0xfffe, 1, 11025, 22050, 2, 16, 6), "Mono11025 PCM16 extensible native format")
    need(wem_parts[b"fmt "][18:] == bytes.fromhex("000001410000"), "Wwise PCM extensible subtype")
    expected_hash = hashlib.sha256(b"clonedivers/lego/yoda/death/v1\0" + pcm).digest()[:16]
    need(wem_parts[b"hash"] == expected_hash and struct.unpack_from("<I", expected_hash)[0] == cache, "Content-derived exact cache ID/hash")
    return {"originalObjects": len(old), "newObjects": len(additions),
            "originalEvents": sum(kind == 4 for _, kind, _ in old), "originalSoundContracts": sum(kind == 2 for _, kind, _ in old),
            "originalMedia": len(old_media), "newMedia": 1, "pcmFrames": len(pcm) // 2,
            "sampleRateHz": 11025, "graph": "new event -> new PlayAction -> new Sound -> new ActorMixer -> existing Init voice bus",
            "newWemSha256": hashlib.sha256(wem).hexdigest()}


def main() -> None:
    candidate = AREA / "audio/candidate-v1"
    output = AREA / "armor/audio-peer-v1"
    need(not output.exists(), "Fresh peer output")
    report_path = candidate / "report.json"
    need(pin(report_path)["sha256"] == "994b87c9c7f78d5841b10f6dbec1d65c82ffb1f1ec818523a12c53e0051981f5", "Frozen audio candidate report")
    source_report = json.loads(report_path.read_text())
    raw = checked(source_report["files"][0]).read_bytes()
    current = checked(source_report["currentCommonBank"]).read_bytes()
    actual = archive(raw, True)
    need(actual == checked(source_report["candidateBank"]).read_bytes(), "Raw serialized BANK exact authored payload")
    for item in source_report["files"][1:]:
        need(checked(item).stat().st_size == 0, "No streamed or GPU media added")
    winning = checked(source_report["freshWinningArchivePins"][0])
    need(archive(winning.read_bytes()) == current, "Baseline equals actual selected r22 winner")
    source = checked(source_report["source"])
    with wave.open(io.BytesIO(source.read_bytes())) as wav:
        need((wav.getnchannels(), wav.getsampwidth(), wav.getframerate(), wav.getnframes()) == (1, 2, 11025, 14705), "Exact classic Yoda source format")
        pcm = wav.readframes(wav.getnframes())
    validation = validate(current, actual, pcm)
    closure_path = checked(source_report["nativeClosure"])
    closure = json.loads(closure_path.read_text())
    ids_path = checked(closure["allIds"])
    native = json.loads(ids_path.read_text())
    blocked = set(native["objects"]) | set(native["media"]) | set(native["caches"])
    need(len(set(IDS.values())) == 6 and not set(IDS.values()) & blocked, "Every new graph/media/cache identity disjoint from all479 native BANK ID corpus")
    need(closure["uniqueNativeBanks"] == 479 and any(bus["id"] == 2467607253 and bus["type"] == 8 and bus["bank"] == "065cfa3b2c82a13d" for bus in closure["standardVoiceBus"]), "Resident Init bus typed AudioBus closure")
    negatives = []
    for name, offset in (("existing-BKHD-mutated", actual.index(b"BKHD") + 8),
                         ("original-media-mutated", actual.index(b"DATA") + 8),
                         ("new-PCM-mutated", len(actual) - 1)):
        mutant = bytearray(actual);mutant[offset] ^= 1
        try:
            validate(current, bytes(mutant), pcm)
        except ValueError as error:
            negatives.append({"name": name, "rejected": True, "reason": str(error)})
        else:
            raise ValueError("Mutation escaped independent peer: " + name)
    output.mkdir(parents=True)
    report = {"status": "passed", "candidateReport": pin(report_path), "archive": pin(checked(source_report["files"][0])),
              "baselineWinningArchive": pin(winning), "source": pin(source), "validation": validation, "ids": IDS,
              "nativeClosure": pin(closure_path), "nativeIds": pin(ids_path), "negativeControls": negatives,
              "independence": "Direct archive, BANK chunks, HIRC records, DIDX/DATA and WEM decoding with stdlib. No builder or authoring-parser import. Native479-bank collision/bus corpus independently hash-checked, not rescanned.",
              "scope": "Asset structural/source-preservation peer only. No loaded engine event or audible playback proof.",
              "runtimeAccepted": False, "liveChanges": False, "tool": pin(Path(__file__))}
    destination = output / "report.json"
    destination.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(pin(destination)))


if __name__ == "__main__":
    main()
