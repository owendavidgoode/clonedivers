#!/usr/bin/env python3
"""Add an isolated classic Yoda one-shot to the current common player voice BANK.

All existing HIRC entries, DIDX entries, DATA bytes, BKHD and bank identity are
retained. Four new objects provide a deterministic Event -> Play -> Sound with
one new ActorMixer copied from the native generic voice root. No normal event
points to the new graph. The only packed resource is the existing common BANK.
"""
import argparse
from collections.abc import Sequence
import copy
import hashlib
import io
import json
import logging
from pathlib import Path
import re
import runpy
import struct
import subprocess
import sys
import types
from typing import Any
import wave

LOG = logging.getLogger(__name__)
BANK_KEY = (0xB7CC016F2537E3D3, 0x535A7BD3E650D799)
ROW = struct.Struct("<7Q6I")
EVENT_NAME = "clonedivers_lego_yoda_death"
NAMES = {"event": EVENT_NAME, "action": EVENT_NAME + "_play", "sound": EVENT_NAME + "_sound",
         "mixer": EVENT_NAME + "_mixer", "media": EVENT_NAME + "_pcm"}
ROOT_MIXER, TEMPLATE_SOUND, TEMPLATE_ACTION = 936212895, 67848527, 1032286481
BUS = 2467607253
CURRENT_SHA = "70c3f08cae082fefad2a1822dd929581c2a7cf52769af15e5cd6a218992bffd7"
SOURCE_SHA = "67c794b9fb1167124b052fd613f4497ed944265593fe74efc93533b09a668b6e"
SELECTOR_SHA = "9652cd9331deb344c043b0c401a0973c226a16a1d89899e2f3458f2f38e797bb"
CLOSURE_SHA = "96aaf33c537dfb447ea45ff0077d15fe1c2f721c3de1097a5867600981d7438a"


def require(condition: Any, message: str) -> None:
    if not condition:
        raise ValueError(message)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def pin(path: Path) -> dict[str, Any]:
    with path.open("rb") as source:
        value = hashlib.file_digest(source, "sha256").hexdigest()
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": value}


def checked(reference: dict[str, Any]) -> Path:
    path = Path(reference["path"])
    require(pin(path) == reference, f"Pinned source changed: {path}")
    return path


def fnv1(name: str) -> int:
    value = 0x811C9DC5
    for byte in name.lower().encode("ascii"):
        value = ((value * 0x1000193) & 0xFFFFFFFF) ^ byte
    return value


def chunks(data: bytes) -> dict[bytes, bytes]:
    require(len(data) >= 24 and data[16:20] == b"BKHD" and struct.unpack_from("<I", data, 4)[0] + 16 == len(data), "BANK wrapper length")
    result, cursor = {}, 16
    while cursor < len(data):
        require(cursor + 8 <= len(data), "BANK chunk header extent")
        tag, length = struct.unpack_from("<4sI", data, cursor)
        require(tag not in result and cursor + 8 + length <= len(data), "BANK unique bounded chunks")
        result[tag] = data[cursor + 8:cursor + 8 + length]
        cursor += 8 + length
    require(cursor == len(data), "BANK complete chunk tail")
    require(struct.unpack_from("<I", result[b"BKHD"])[0] ^ 0x9211BCAC == 154, "Current native Wwise154")
    return result


def hirc(data: bytes) -> dict[int, tuple[int, bytes]]:
    cursor, result = 4, {}
    for _ in range(struct.unpack_from("<I", data)[0]):
        require(cursor + 9 <= len(data), "HIRC header extent")
        kind, length, identity = struct.unpack_from("<BII", data, cursor)
        require(length >= 4 and identity not in result and cursor + 5 + length <= len(data), "HIRC unique complete objects")
        result[identity] = kind, data[cursor + 9:cursor + 5 + length]
        cursor += 5 + length
    require(cursor == len(data), "HIRC complete tail")
    return result


def media(parts: dict[bytes, bytes]) -> dict[int, bytes]:
    require(len(parts[b"DIDX"]) % 12 == 0, "DIDX complete entries")
    result = {}
    for identity, offset, length in struct.iter_unpack("<III", parts[b"DIDX"]):
        require(identity not in result and offset + length <= len(parts[b"DATA"]), "DIDX unique DATA bounds")
        result[identity] = parts[b"DATA"][offset:offset + length]
    return result


def raw_pcm(data: bytes) -> tuple[wave._wave_params, bytes]:
    with wave.open(io.BytesIO(data), "rb") as source:
        params, frames = source.getparams(), source.readframes(source.getnframes())
    require((params.nchannels, params.sampwidth, params.framerate, params.nframes, params.comptype) ==
            (1, 2, 11025, 14705, "NONE"), "Exact classic source audio extent")
    require(len(frames) == 29410, "Exact classic source PCM bytes")
    return params, frames


def make_bank(original: bytes, parts: dict[bytes, bytes]) -> bytes:
    body = b"".join(struct.pack("<4sI", tag, len(data)) + data for tag, data in parts.items())
    wrapper = bytearray(original[:16])
    struct.pack_into("<I", wrapper, 4, len(body))
    return bytes(wrapper) + body


def record(kind: int, identity: int, body: bytes) -> bytes:
    return struct.pack("<BII", kind, len(body) + 4, identity) + body


def validate(original: bytes, candidate: bytes, ids: dict[str, int], wem: bytes,
             additions: dict[int, tuple[int, bytes]]) -> dict[str, Any]:
    before, after = chunks(original), chunks(candidate)
    require(before.keys() == after.keys(), "No new BANK chunk types")
    require(candidate[:4] == original[:4] and candidate[8:16] == original[8:16], "BANK identity wrapper retained")
    require(all(before[tag] == after[tag] for tag in before if tag not in (b"HIRC", b"DIDX", b"DATA")), "Every nonmedia BANK chunk unchanged")
    old, new = hirc(before[b"HIRC"]), hirc(after[b"HIRC"])
    require(set(new) == set(old) | set(additions) and not set(old) & set(additions), "Exactly four disjoint appended HIRC objects")
    require(all(new[key] == value for key, value in old.items()), "All original HIRC objects byte exact")
    require(after[b"HIRC"][4:4 + len(before[b"HIRC"]) - 4] == before[b"HIRC"][4:], "All original HIRC order/header/body bytes exact")
    require(all(new[key] == value for key, value in additions.items()), "Exact isolated authored graph")
    require(new[ids["event"]] == (4, b"\x01" + struct.pack("<I", ids["action"])), "One event action only")
    action = new[ids["action"]][1]
    require(new[ids["action"]][0] == 3 and struct.unpack_from("<HI", action) == (0x0403, ids["sound"]), "Play action targets only the dedicated sound")
    sound = new[ids["sound"]][1]
    codec, mode, source, cache, memory, flags = struct.unpack_from("<IBIIIB", sound)
    require((codec, mode, source, memory, flags) == (0x10001, 0, ids["media"], len(wem), 0), "Complete language-neutral resident PCM source")
    require(cache == ids["cache"], "WEM hash/cache identity match")
    require(struct.unpack_from("<I", sound, 26)[0] == ids["mixer"], "Sound parent is new deterministic mixer")
    mixer = new[ids["mixer"]][1]
    require(new[ids["mixer"]][0] == 7 and struct.unpack_from("<II", mixer, 4) == (BUS, 0), "Mixer root output routes to resident native voice bus")
    require(mixer[-8:] == struct.pack("<II", 1, ids["sound"]), "Mixer lists only dedicated sound")
    require(after[b"DIDX"][:len(before[b"DIDX"])] == before[b"DIDX"] and len(after[b"DIDX"]) == len(before[b"DIDX"]) + 12, "All original DIDX rows/order exact; one addition")
    require(after[b"DATA"].startswith(before[b"DATA"]), "All original DATA bytes exact")
    original_media, candidate_media = media(before), media(after)
    require(candidate_media == {**original_media, ids["media"]: wem}, "All existing resident media/cache bytes exact and one new WEM")
    require(len(new) == len(old) + 4 and sum(kind == 4 for kind, _ in new.values()) == sum(kind == 4 for kind, _ in old.values()) + 1, "One new event and four new objects")
    old_sounds = {key: value for key, value in old.items() if value[0] == 2}
    require(len(old_sounds) == 644, "All existing644 nonverbal sources retained")
    return {"originalObjects": len(old), "newObjects": 4, "originalEvents": sum(kind == 4 for kind, _ in old.values()),
            "newEvents": 1, "existingSoundContractsExact": len(old_sounds), "residentMediaRetainedExact": len(original_media),
            "oldHircBytesAfterCountExact": True, "oldDidxBytesExactPrefix": True, "oldDataBytesExactPrefix": True,
            "allExistingMetadataAndWrapperIdentityExact": True, "onlyNewGraphReachableFromNewEvent": True}


def baseline_winners(root: Path, current: bytes) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    selector_path = root / "dist/production-r22-2026-10-08/inputs-v1/production96.json"
    require(pin(selector_path)["sha256"] == SELECTOR_SHA, "Actual current r22 production selector pin")
    selected = json.loads(selector_path.read_text(encoding="utf-8"))
    peer = json.loads((root / "dist/production-r22-2026-10-08/qa/runtime-peer-v1/report.json").read_text())
    source_proof_path = checked(peer["geometryAndEagleProfileDependencyPeer"]["freshMainAndAffectedCompanionHashes"])
    refs = json.loads(source_proof_path.read_text())
    sources = {item["sha256"]: item for item in refs}
    for item in json.loads((root / "dist/production-r22-2026-10-08/voice-v1/report.json").read_text())["files"]:
        sources[item["sha256"]] = item
    runtime = json.loads((root / "dist/production-r22-2026-10-08/runtime-v1/report.json").read_text())
    for item in runtime["sources"][0]["files"]:
        sources[item["sha256"]] = item
    directories, pins, proofs = {}, {}, []
    file_sets = sorted({state["fileSet"] for state in selected["selections"] if state["mode"] == "EmpireDivers"})
    require(len(file_sets) == 8, "All eight actual Empire Full/Lighter effective selections")
    for file_set in file_sets:
        winner = None
        for item in selected["fileSets"][file_set]:
            if not re.fullmatch(r"[a-f0-9]{16}\.patch_\d+", item["name"]):
                continue
            digest = item["sha256"]
            if digest not in directories:
                require(digest in sources, "Each current selected MAIN has pinned source")
                path = Path(sources[digest]["path"])
                require(path.stat().st_size == item["size"] == sources[digest]["bytes"], "Current selected MAIN pinned extent")
                with path.open("rb") as source:
                    header = source.read(72)
                    magic, types, count = struct.unpack_from("<III", header)
                    require(magic == 0xF0000011 and count < 1000000 and types < 1000, "Fresh bounded archive directory")
                    table = source.read(types * 32 + count * 80)
                require(len(table) == types * 32 + count * 80, "Complete archive directory")
                records = list(ROW.iter_unpack(table[types * 32:]))
                require(len({row[:2] for row in records}) == count, "Unique typed archive rows")
                directories[digest] = {row[:2]: row for row in records}
            if BANK_KEY in directories[digest]:
                winner = item, directories[digest][BANK_KEY], Path(sources[digest]["path"])
        require(winner is not None, "Common player BANK selected")
        item, row, path = winner
        if item["sha256"] not in pins:
            pins[item["sha256"]] = pin(path)
            require(pins[item["sha256"]] == sources[item["sha256"]], "Fresh entire winning source archive pin")
        with path.open("rb") as source:
            source.seek(row[2])
            payload = source.read(row[7])
        require(payload == current and row[8:10] == (0, 0), "Actual all-eight current BANK payload exact/no companions")
        proofs.append({"fileSet": file_set, "profiles": sorted({state["profile"] for state in selected["selections"] if state["fileSet"] == file_set}),
                       "winner": item["name"], "archiveSha256": item["sha256"], "bankSha256": sha(payload),
                       "onlyOverlayTypedDelta": f"{BANK_KEY[0]:016x}.{BANK_KEY[1]:016x}"})
    return proofs, list(pins.values())


def run(root: Path, out: Path) -> dict[str, Any]:
    root, out = root.resolve(strict=True), out.resolve()
    require(out.is_relative_to(root / "dist/empire-yoda-death-2026-10-08/audio") and not out.exists(), "Fresh scoped audio output")
    current_path = root / "dist/empire-voice-next-2026-10-08/conversion/authentic-candidate-v1/raw/current.bank"
    source_path = root / "dist/empire-yoda-death-2026-10-08/source-v1/complete-saga-YODADEATH-original.wav"
    closure_path = root / "dist/empire-yoda-death-2026-10-08/audio/native-closure-v1/report.json"
    require(pin(current_path)["sha256"] == CURRENT_SHA and pin(source_path)["sha256"] == SOURCE_SHA and pin(closure_path)["sha256"] == CLOSURE_SHA, "Sealed source/current/native-closure pins")
    closure = json.loads(closure_path.read_text())
    all_ids = json.loads(checked(closure["allIds"]).read_text())
    blocked = set(all_ids["objects"]) | set(all_ids["media"]) | set(all_ids["caches"])
    ids = {name: fnv1(value) for name, value in NAMES.items()}
    require(len(set(ids.values())) == len(ids) and not set(ids.values()) & blocked, "All new object/media IDs disjoint from every479 native BANK IDs")
    params, source_pcm = raw_pcm(source_path.read_bytes())
    hash16 = hashlib.sha256(b"clonedivers/lego/yoda/death/v1\0" + source_pcm).digest()[:16]
    ids["cache"] = struct.unpack_from("<I", hash16)[0]
    require(ids["cache"] not in blocked | set(ids[name] for name in NAMES), "Fresh cache identity disjoint across native inventory")
    fmt = struct.pack("<HHIIHHH", 0xFFFE, 1, 11025, 22050, 2, 16, 6) + bytes.fromhex("000001410000")
    wem_body = b"WAVE" + b"".join(struct.pack("<4sI", tag, len(data)) + data + bytes(len(data) % 2)
                                  for tag, data in ((b"fmt ", fmt), (b"hash", hash16), (b"data", source_pcm)))
    wem = b"RIFF" + struct.pack("<I", len(wem_body)) + wem_body
    current = current_path.read_bytes()
    before = chunks(current)
    old_objects = hirc(before[b"HIRC"])
    require(set(ids.values()).isdisjoint(old_objects | media(before)), "New IDs disjoint from actual current helmet/authentic bank")
    logger_module = types.ModuleType("log")
    logger_module.logger = LOG
    sys.modules["log"] = logger_module
    parser_dir = root / "dist/rc-upgrade/hd2-audio-modder"
    sys.path.insert(0, str(parser_dir))
    import wwise_hierarchy_154 as P
    hierarchy = P.WwiseHierarchy_154()
    hierarchy.load(before[b"HIRC"])
    for identity, obj in hierarchy.entries.items():
        kind, body = old_objects[identity]
        require(bytes(obj.get_data()) == record(kind, identity, body), "Every current object independently parses/roundtrips Wwise154")
    sound = copy.deepcopy(hierarchy.entries[TEMPLATE_SOUND])
    require(sound.baseParam.positioningParamData == b"\0" and sound.baseParam.propBundle.cProps == 0 and sound.baseParam.uNumCurves == 0,
            "Minimal native sound no loops/RTPC/properties")
    sound.hierarchy_id = ids["sound"]
    sound.baseParam.directParentID = ids["mixer"]
    sound.sources[0].plugin_id, sound.sources[0].stream_type = 0x10001, 0
    sound.sources[0].source_id, sound.sources[0].cache_id = ids["media"], ids["cache"]
    sound.sources[0].mem_size, sound.sources[0].bit_flags = len(wem), 0
    sound.update_size()
    mixer = copy.deepcopy(hierarchy.entries[ROOT_MIXER])
    require(mixer.baseParam.directParentID == 0 and mixer.baseParam.overrideBusId == BUS and mixer.baseParam.uNumFx == 0 and
            mixer.baseParam.uNumFxMetadata == 0 and mixer.baseParam.uNumCurves == 0 and mixer.baseParam.stateParams.ulNumStateGroups == 0,
            "Native generic voice root has only resident output bus and no FX/RTPC/state dependencies")
    mixer.hierarchy_id = ids["mixer"]
    mixer.children.numChildren, mixer.children.children = 1, [ids["sound"]]
    mixer.update_size()
    action = copy.deepcopy(hierarchy.entries[TEMPLATE_ACTION])
    require(action.ulActionType == 0x0403 and action.propBundle.cProps == 0 and action.rangePropBundle.cProps == 0, "Native immediate single Play action shape")
    action.hierarchy_id, action.idExt = ids["action"], ids["sound"]
    new_records = [bytes(mixer.get_data()), bytes(sound.get_data()), bytes(action.get_data()), record(4, ids["event"], b"\x01" + struct.pack("<I", ids["action"]))]
    appended_hirc = struct.pack("<I", len(old_objects) + 4) + before[b"HIRC"][4:] + b"".join(new_records)
    additions = {identity: (kind, body) for identity, (kind, body) in hirc(struct.pack("<I", 4) + b"".join(new_records)).items()}
    new_data = before[b"DATA"] + bytes(-len(before[b"DATA"]) % 16)
    didx = before[b"DIDX"] + struct.pack("<III", ids["media"], len(new_data), len(wem))
    after = {**before, b"HIRC": appended_hirc, b"DIDX": didx, b"DATA": new_data + wem}
    candidate = make_bank(current, after)
    validation = validate(current, candidate, ids, wem, additions)
    final_hierarchy = P.WwiseHierarchy_154()
    final_hierarchy.load(after[b"HIRC"])
    require(set(final_hierarchy.entries) == set(old_objects) | set(additions), "Independent final hierarchy inventory")
    for identity, obj in final_hierarchy.entries.items():
        kind, body = hirc(after[b"HIRC"])[identity]
        require(bytes(obj.get_data()) == record(kind, identity, body), "Every final object independently roundtrips")
    winner_proofs, source_pins = baseline_winners(root, current)
    writer_path = root / "dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d/sdk/tools/hd2_archive.py"
    require(pin(writer_path)["sha256"] == "49050160f17d25f4838652071811d65311f8f3e84eb47f795bf3a573f43ba66d", "Primary archive writer pin")
    writer = runpy.run_path(str(writer_path))
    base = writer["make_archive"]({BANK_KEY[0]: candidate})
    packed = bytearray(base)
    struct.pack_into("<Q", packed, 80, BANK_KEY[1])
    struct.pack_into("<Q", packed, 112, BANK_KEY[1])
    archive_row = ROW.unpack_from(packed, 104)
    require(archive_row[:2] == BANK_KEY and archive_row[7] == len(candidate) and not archive_row[8] and not archive_row[9] and
            packed[archive_row[2]:archive_row[2] + archive_row[7]] == candidate, "Raw serialized singleton BANK/no companions roundtrip exact")
    restored = bytearray(packed)
    struct.pack_into("<Q", restored, 80, writer["LUA_TYPE"])
    struct.pack_into("<Q", restored, 112, writer["LUA_TYPE"])
    require(bytes(restored) == base, "Only resource type adapted from primary SDK serialization")
    out.mkdir(parents=True)
    (out / "classic-yoda-death.wem").write_bytes(wem)
    (out / "current-common.bank").write_bytes(current)
    (out / "candidate-common.bank").write_bytes(candidate)
    (out / "new-objects.json").write_text(json.dumps({str(key): {"kind": kind, "bodyHex": body.hex()} for key, (kind, body) in additions.items()}, indent=2) + "\n")
    bundle = out / "bundle"
    bundle.mkdir()
    files = []
    for suffix, data in (("", bytes(packed)), (".stream", b""), (".gpu_resources", b"")):
        path = bundle / ("9ba626afa44a3aa3.patch_0" + suffix)
        path.write_bytes(data)
        files.append(pin(path))
    decoder = root / "dist/rc-upgrade/vgmstream/vgmstream-cli.exe"
    require(pin(decoder)["sha256"] == "29df08c557ada8269c92a6abfe3886ad2f65849b0b26ba78a0819b363bbc5b85", "Pinned independent decoder")
    decode_path = out / "fresh-independent-decode.wav"
    decoded = subprocess.run([str(decoder), "-o", str(decode_path), str(out / "classic-yoda-death.wem")], capture_output=True, text=True, check=True)
    require(raw_pcm(decode_path.read_bytes()) == (params, source_pcm), "Fresh serialized WEM decoder retains exact classic PCM")
    (out / "decoder.txt").write_text(decoded.stdout + decoded.stderr, encoding="utf-8")
    negative = []
    for name in ("oldHircChanged", "oldDataChanged", "oldDidxChanged", "newEventTargetMissing", "sourceMemoryTruncated", "newSoundParentMissing", "newMixerBusChanged", "duplicateNewId", "nativeVersionChanged"):
        mutant = dict(after)
        if name == "oldHircChanged":
            block = bytearray(mutant[b"HIRC"]); block[20] ^= 1; mutant[b"HIRC"] = bytes(block)
        elif name == "oldDataChanged":
            block = bytearray(mutant[b"DATA"]); block[5] ^= 1; mutant[b"DATA"] = bytes(block)
        elif name == "oldDidxChanged":
            block = bytearray(mutant[b"DIDX"]); block[0] ^= 1; mutant[b"DIDX"] = bytes(block)
        elif name == "nativeVersionChanged":
            block = bytearray(mutant[b"BKHD"]); block[0] ^= 1; mutant[b"BKHD"] = bytes(block)
        else:
            block = bytearray(mutant[b"HIRC"])
            target, offset = {"newEventTargetMissing": ("event", 9 + 1), "sourceMemoryTruncated": ("sound", 9 + 13),
                              "newSoundParentMissing": ("sound", 9 + 26), "newMixerBusChanged": ("mixer", 9 + 4),
                              "duplicateNewId": ("event", 5)}[name]
            cursor = 4
            for _ in range(struct.unpack_from("<I", block)[0]):
                _, size, identity = struct.unpack_from("<BII", block, cursor)
                if identity == ids[target]:
                    struct.pack_into("<I", block, cursor + offset, ids["sound"] if name == "duplicateNewId" else 0)
                    break
                cursor += 5 + size
            mutant[b"HIRC"] = bytes(block)
        try:
            validate(current, make_bank(current, mutant), ids, wem, additions)
        except ValueError as error:
            negative.append({"fixture": name, "rejected": True, "reason": str(error)})
        else:
            raise ValueError("Negative fixture accepted: " + name)
    return {"passed": True, "tool": pin(Path(__file__)), "source": pin(source_path), "nativeClosure": pin(closure_path),
            "currentCommonBank": pin(current_path), "targetResource": "content/audio/Helldiver_Standard_VO",
            "typedKeys": [f"{BANK_KEY[0]:016x}.{BANK_KEY[1]:016x}"], "files": files,
            "candidateBank": pin(out / "candidate-common.bank"), "wem": pin(out / "classic-yoda-death.wem"),
            "freshDecode": pin(decode_path), "eventName": EVENT_NAME, "ids": ids,
            "graph": "new Event -> one new immediate Play Action -> one new Sound; new one-child ActorMixer parent -> existing Init voice bus",
            "sourceDeclarationHex": hirc(after[b"HIRC"])[ids["sound"]][1][:18].hex(), "fmtHex": fmt.hex(), "wemHash16": hash16.hex(),
            "audio": {"channels": 1, "rateHz": 11025, "frames": 14705, "durationSeconds": 14705 / 11025,
                      "pcmSha256": sha(source_pcm), "unchangedClassicPcm": True, "codec": "Wwise PCM16", "mode": 0,
                      "memoryBytes": len(wem), "languageSpecific": False, "streaming": False, "loops": False,
                      "processing": "No resampling, pitch, gain, trim, stretch or synthesis"},
            "templates": {"sound": TEMPLATE_SOUND, "playAction": TEMPLATE_ACTION, "rootActorMixer": ROOT_MIXER,
                          "rootBaseParamCopiedExact": True, "onlySoundParentAndSourceDeclarationChanged": True,
                          "playNativeBankIdAndBankTypeAndFadeBytesRetainedExact": True},
            "rootBus": {"id": BUS, "proof": closure["standardVoiceBus"], "parent": 0, "noFxRtpcStateOrAttenuationDependency": True},
            "oldBankPreservation": validation, "actualR22AllEightEmpireWinners": winner_proofs,
            "freshWinningArchivePins": source_pins, "actualR22Selector": pin(root / "dist/production-r22-2026-10-08/inputs-v1/production96.json"),
            "all479NativeBankIdCollisionCheck": True, "nativeCollisionObjects": closure["uniqueObjectIds"],
            "nativeCollisionMedia": closure["uniqueMediaIds"], "nativeCollisionCaches": closure["uniqueCacheIds"],
            "negativeFixtures": negative, "primaryWriter": pin(writer_path), "independentVersion154Parser": pin(parser_dir / "wwise_hierarchy_154.py"),
            "readyForOfflinePeer": True, "readyForComposition": True, "enginePlaybackAccepted": False,
            "limits": ["Mode0 PCM uses current version154 Data/bnk source contract, actual native PCM codec/tag and native packed mono configuration; no native embedded-PCM control was found (all six stock PCM controls stream).",
                       "Common Helldiver_Standard_VO native graph contains all persona nonverbal switches and language-neutral source flags; actual engine residency and audible event/mix remain playtest acceptance.",
                       "Runtime must require LEGO body armor, local death transition, pinned native world and Wwise.has_event; this bank alone never replaces/posts any ordinary cue."],
            "gameSettingsPublicOrDeploymentChanged": False}


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0], **({"suggest_on_error": True} if sys.version_info >= (3, 14) else {}))
    parser.add_argument("workspace", type=Path)
    parser.add_argument("out", type=Path)
    parser.add_argument("-v", "--verbose", action="store_true")
    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.DEBUG if args.verbose else logging.INFO, format="%(levelname)s: %(message)s")
    try:
        report = run(args.workspace, args.out)
        path = args.out / "report.json"
        path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(pin(path)))
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, struct.error, subprocess.CalledProcessError):
        LOG.exception("Yoda audio candidate failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
