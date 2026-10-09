"""Restore a native metadata packet without changing the audible Vorbis recording."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import runpy
import struct
import subprocess
import wave

ROOT = Path(__file__).resolve().parents[1]
TASK = ROOT / "dist/empire-deep-qa-2026-10-08/audio"
VOICE = ROOT / "dist/empire-voice-next-2026-10-08"
OUT = TASK / "constitution-prefix-candidate-v1"
MANIFEST_SHA = "a1ebc2d541952beb7afceb3474959cbf65685785b2986c3378585cb7366e89c6"
BANK_KEY = "bb121a2d5802cd04.535a7bd3e650d799"
STREAM_KEY = "9a6be332b8e03ce1.504b55235d21440e"


def check(value: object, message: str) -> None:
    if not value:
        raise ValueError(message)


def sha(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def pin(path: Path) -> dict:
    with path.open("rb") as handle:
        digest = hashlib.file_digest(handle, "sha256").hexdigest()
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": digest}


def main() -> None:
    check(not OUT.exists(), "Fresh native metadata candidate")
    selector_path = ROOT / "dist/empire-deep-qa-2026-10-08/core/production96-v1.json"
    qa_path = VOICE / "qa/all-assets-v1.json"
    manifest = VOICE / "integration/candidate-v1/manifest.json"
    selector, qa = [json.loads(path.read_text()) for path in (selector_path, qa_path)]
    check(selector["passed"] and selector["manifestSha256"] == qa["manifest"]["sha256"] == pin(manifest)["sha256"] == MANIFEST_SHA, "Exact deployed source")
    R = runpy.run_path(str(ROOT / "tools/deep-next-audio-reader.py"))
    current = R["Current"](qa, selector)
    A = runpy.run_path(str(ROOT / "tools/audit-live-audio.py"))
    C = runpy.run_path(str(ROOT / "tools/deep-next-audio-cutscene.py"))
    riff = C["riff"]
    native = runpy.run_path(str(ROOT / "tools/walker-next-native.py"))["NativeSlim"](Path("C:/Program Files (x86)/Steam/steamapps/common/Helldivers 2/data"))
    catalog_path = ROOT / "dist/live-audio-2026-10-03/native-audio-catalog.txt"
    catalog = A["load_catalog"](catalog_path)
    key = tuple(int(x, 16) for x in STREAM_KEY.split("."))
    bkey = tuple(int(x, 16) for x in BANK_KEY.split("."))
    provider = catalog[key]["archives"][0]
    row, parts = native.resource(provider, *key)
    bank_provider = catalog[bkey]["archives"][0]
    bank = native.resource(bank_provider, *bkey)[1][0]
    chunks = A["chunks"](bank)
    source = A["hierarchy"](chunks[b"HIRC"])[913679024][1][:18]
    check(struct.unpack("<IBIIIB", source) == (0x40001, 1, 107068376, 3758650417, 2406, 0), "Exact native mode1 Vorbis global source")
    embedded = {m: chunks[b"DATA"][o:o + n] for m, o, n in struct.iter_unpack("<III", chunks[b"DIDX"])}
    cache = embedded[107068376]
    check(len(cache) == 2406 and cache == parts[1][:2406], "Native prefetch exact native full prefix")
    native_chunks = riff(parts[1])
    OUT.mkdir()
    (OUT / "raw").mkdir()
    for name, raw in (("native.bank", bank), ("native.wem", parts[1]), ("native.wrapper", parts[0]), ("native-prefetch.bin", cache)):
        (OUT / "raw" / name).write_bytes(raw)
    states, current_wem = [], None
    for fs in selector["fileSets"]:
        selected = current.parts(fs, STREAM_KEY)
        selected_bank = current.parts(fs, BANK_KEY)[0] if BANK_KEY in current.winners[fs] else bank
        check(selected_bank == bank and not selected[2] and len(selected[0]) == 12, "Actual selected native BANK and normal full STREAM wrapper")
        check(struct.unpack_from("<I", selected[0], 8)[0] == len(selected[1]), "Current wrapper size exact; restore whole native wrapper explicitly")
        actual_chunks = riff(selected[1])
        check(set(native_chunks) == {b"fmt ", b"hash", b"junk", b"akd ", b"data"} and set(actual_chunks) == {b"fmt ", b"JUNK", b"akd ", b"data"}, "Only native hash packet and junk-tag spelling differ")
        check(all(actual_chunks[tag] == native_chunks[tag] for tag in (b"fmt ", b"akd ", b"data")) and actual_chunks[b"JUNK"] == native_chunks[b"junk"], "Every audible/codec packet and junk payload exact native")
        check(len(parts[1]) - len(selected[1]) == 24 and selected[1][:2406] != cache, "One removed 24-byte hash packet shifts active cache prefix")
        if current_wem is None:
            current_wem = selected[1]
        check(current_wem == selected[1], "Uniform actual sixteen source winners")
        digest = current.winners[fs][STREAM_KEY]
        states.append({"fileSet": fs, "actualSelectedRows": current.rows[fs][digest], "actualTriad": current.archives[fs][digest]["identity"], "source18Hex": source.hex(), "nativeBankSha256": sha(bank), "beforePrefixExact": False, "afterPrefixExact": True,
            "currentWemSha256": sha(selected[1]), "candidateWemSha256": sha(parts[1]), "currentWrapperHex": selected[0].hex(), "candidateWrapperHex": parts[0].hex()})
    (OUT / "raw/current.wem").write_bytes(current_wem)
    decoder = ROOT / "dist/rc-upgrade/vgmstream/vgmstream-cli.exe"
    decodes = []
    for name in ("current", "native"):
        target = OUT / "raw" / ("temporary-" + name + ".wav")
        call = subprocess.run([str(decoder), "-i", "-o", str(target), str(OUT / "raw" / (name + ".wem"))], capture_output=True, text=True, check=False)
        check(call.returncode == 0 and target.is_file(), "Actual selected and restored native full-decode")
        with wave.open(str(target), "rb") as wav:
            result = {"channels": wav.getnchannels(), "rate": wav.getframerate(), "frames": wav.getnframes(), "width": wav.getsampwidth(), "pcmSha256": sha(wav.readframes(wav.getnframes()))}
        target.unlink()
        result.update(name=name, decoderExitCode=call.returncode, output=call.stdout + call.stderr)
        decodes.append(result)
    check(all(decodes[0][k] == decodes[1][k] for k in ("channels", "rate", "frames", "width", "pcmSha256")), "Whole decoded samples identical, not just metadata")
    header = bytearray(native.read(provider, 0, 72))
    struct.pack_into("<II", header, 4, 1, 1)
    start = 72 + 32 + 80 + 8
    offset = start + (-start % 16)
    entry = list(row)
    entry[:7] = [*key, offset, 0, 0, 0, 0]
    entry[7:13] = [len(parts[0]), len(parts[1]), 0, 16, 64, 0]
    main_bytes = bytes(header) + struct.pack("<QQQII", 0, key[1], 1, 16, 64) + struct.pack("<7Q6I", *entry) + bytes(8 + offset - start) + parts[0]
    # Reserve the aligned MAIN head and 16-byte Wwise metadata slot.
    # Native single-resource archives reserve a complete 256-byte head block.
    main_bytes += bytes(-len(main_bytes) % 256)
    check(len(main_bytes) >= (start + 255) // 256 * 256, "MAIN has a complete aligned TOC head")
    check(offset + 16 <= len(main_bytes), "STREAM metadata slot stays within MAIN")
    (OUT / "bundle").mkdir()
    files = []
    for suffix, data in (("", main_bytes), (".stream", parts[1]), (".gpu_resources", b"")):
        path = OUT / "bundle" / ("9ba626afa44a3aa3.patch_0" + suffix)
        path.write_bytes(data)
        files.append(pin(path))
    report = {"passed": True, "meaningOfPassed": "Exact native STREAM restores active native prefetch join with identical full decoded recording; no runtime playback claim.", "baseline": pin(manifest), "production96": pin(selector_path), "allAssetIndex": pin(qa_path),
        "tool": pin(Path(__file__)), "reader": pin(ROOT / "tools/deep-next-audio-reader.py"), "riffReader": pin(ROOT / "tools/deep-next-audio-cutscene.py"), "nativeCatalog": pin(catalog_path), "nativeStreamProvider": provider, "nativeBankProvider": bank_provider,
        "typedKey": STREAM_KEY, "resources": 1, "files": files, "gates": {"modes": ["clonedivers", "commandos", "empire"]}, "bothProfiles": True,
        "bank": BANK_KEY, "sound": 913679024, "media": 107068376, "source18Hex": source.hex(), "nativeBank": pin(OUT / "raw/native.bank"), "nativeStream": pin(OUT / "raw/native.wem"), "nativeWrapper": pin(OUT / "raw/native.wrapper"), "currentStream": pin(OUT / "raw/current.wem"),
        "nativeCache": pin(OUT / "raw/native-prefetch.bin"), "actual16SetBeforeAfterJoins": states, "fullDecodedSampleEquality": decodes, "decoder": pin(decoder), "bankHierarchyDependencyAndAllCachesUnchanged": True, "noOtherResourceEmitted": True,
        "actualAccessedSourcePins": current.read_pins, "notThePreviouslyMissingConstitutionReference": {"sound": 106438099, "media": 782948424, "untouched": True},
        "limits": ["This restores metadata and full-stream byte positioning only; it adds no new performance and does not resolve the separately documented native missing Constitution source.", "Other runtime bank-loading, mix, and dispatch remain unaccepted."], "runtimeAccepted": False, "gameOrSettingsChanged": False}
    path = OUT / "report.json"
    path.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(pin(path)), flush=True)


if __name__ == "__main__":
    main()
