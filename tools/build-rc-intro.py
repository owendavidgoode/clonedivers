#!/usr/bin/env -S uv run
"""Build a startup intro video override from an existing working HD2 intro patch.

HD2 plays the intro soundtrack through a separate Wwise stream. Pass the audio
patch built by build-rc-intro-audio.py to package both. Writes only staging files.
Python 3.13+; no third-party dependencies. Encoded input must be Bink 2 with audio.
"""

import argparse
import hashlib
import json
import logging
import struct
import sys
import zipfile
from pathlib import Path

LOG = logging.getLogger(__name__)
INTRO = 0xC4038098EC6384AB
BINK = 0x5EE65304478F8DB5
ENTRY = struct.Struct("<QQQQQQQIIIIII")
ARCHIVE = "9ba626afa44a3aa3"


def sha256_file(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def bink_info(data: bytes) -> dict[str, int | float | str]:
    """Validate the Bink header and complete frame index without decoding."""
    if len(data) < 48 or data[:3] != b"KB2":
        raise ValueError("Input must be a Bink 2 file")
    size, frames = struct.unpack_from("<II", data, 4)
    width, height, rate, scale = struct.unpack_from("<IIII", data, 20)
    tracks = struct.unpack_from("<I", data, 40)[0]
    if size + 8 != len(data) or not frames or not rate or not scale:
        raise ValueError("Bink size, frame count or rate is invalid")
    if data[3:4] != b"n":
        raise ValueError("This builder has been validated for the installed KB2n revision")
    flags = struct.unpack_from("<I", data, 36)[0]
    # Match the working Clone Armory intro and HD2 Audio Modder's /V6344.
    # The two-slice /V200 encode crashed in the installed bink2w64.dll.
    if flags & 3 != 2:
        raise ValueError("Use four-slice Bink 2 encoding (/V6344) to match the working HD2 intro")
    # KB2n has an extra 4-byte header field, then 12 bytes per audio track.
    table_start = 48 + tracks * 12
    index_end = table_start + (frames + 1) * 4
    if index_end > len(data):
        raise ValueError("Truncated Bink index")
    offsets = [struct.unpack_from("<I", data, table_start + i * 4)[0] & ~1
               for i in range(frames + 1)]
    if offsets[0] < index_end or offsets[-1] != len(data):
        raise ValueError("Bink frame offsets do not match the file")
    if any(a >= b for a, b in zip(offsets, offsets[1:])):
        raise ValueError("Bink frame index is not increasing")
    if tracks != 1:
        raise ValueError("The RC intro requires exactly one embedded audio track")
    return {"signature": data[:4].decode("ascii"), "frames": frames,
            "width": width, "height": height, "fpsNumerator": rate,
            "fpsDenominator": scale, "seconds": frames * scale / rate,
            "audioTracks": tracks, "flags": flags, "slices": 4, "bytes": len(data)}


RC_CREDITS = ("Republic Commando remaster by WoofWoofWolffe and collaborators.\n"
              "Source: https://www.youtube.com/watch?v=CL5i33CTld8\n"
              "Trim: 8.4 to 172.166667 seconds, with an ending fade.\n")


def build(template: Path, video: Path, out: Path, audio_patch: Path | None = None,
          title: str = "Republic Commando Remastered Intro", credits: str = RC_CREDITS) -> Path:
    source = template.read_bytes()
    movie = video.read_bytes()
    info = bink_info(movie)
    magic, types, files = struct.unpack_from("<III", source)
    if magic != 0xF0000011 or 72 + 32 * types + 80 * files > len(source):
        raise ValueError("Invalid template archive")
    matches = []
    for i in range(files):
        fields = list(ENTRY.unpack_from(source, 72 + 32 * types + 80 * i))
        if fields[:2] == [INTRO, BINK]:
            matches.append(fields)
    if len(matches) != 1:
        raise ValueError("Template must contain exactly one startup intro resource")
    fields = matches[0]
    original_main = source[fields[2]:fields[2] + fields[7]]
    if len(original_main) != 16:
        raise ValueError("Unexpected intro main-resource size")
    type_rows = [source[72 + 32*i:104 + 32*i] for i in range(types)]
    type_row = next(row for row in type_rows if struct.unpack_from("<Q", row, 8)[0] == BINK)
    type_row = bytearray(type_row)
    struct.pack_into("<Q", type_row, 16, 1)
    header = bytearray(source[:72])
    struct.pack_into("<II", header, 4, 1, 1)
    fields[2:5] = [192, 0, 0]
    fields[7:10] = [16, len(movie), 0]
    fields[12] = 0
    patch = bytes(header) + type_row + ENTRY.pack(*fields) + bytes(8) + original_main
    patch += bytes(max(0, 256-len(patch)))
    stream = movie + bytes((-len(movie)) % 16)
    out.mkdir(parents=True, exist_ok=True)
    patch_path = out / f"{ARCHIVE}.patch_0"
    patch_path.write_bytes(patch)
    stream_path = out / f"{ARCHIVE}.patch_0.stream"
    stream_path.write_bytes(stream)
    # Verify the serialized resource, including its original metadata and exact video.
    loaded = ENTRY.unpack_from(patch_path.read_bytes(), 104)
    if loaded[0:2] != (INTRO, BINK) or patch[loaded[2]:loaded[2]+loaded[7]] != original_main:
        raise ValueError("Written patch metadata verification failed")
    if stream_path.read_bytes()[loaded[3]:loaded[3]+loaded[8]] != movie:
        raise ValueError("Written video differs from encoded input")
    report = {"video": str(video.resolve()), "template": str(template.resolve()),
              "videoInfo": info, "resourceCount": 1,
              "videoSha256": hashlib.sha256(movie).hexdigest(),
              "audioStrategy": "Separate English Wwise stream from build-rc-intro-audio.py",
              "requires": "Clone Armory Opening Videos already installed",
              "runtimeTested": False,
              "files": [{"name": p.name, "bytes": p.stat().st_size,
                         "sha256": sha256_file(p)}
                        for p in (patch_path, stream_path)]}
    (out / "build-report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    zip_path = out / (f"{title}.zip" if audio_patch else f"{title} (video only).zip")
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_STORED) as archive:
        for p in (patch_path, stream_path):
            archive.write(p, "RC Intro/" + p.name)
        if audio_patch:
            for suffix in ("", ".stream"):
                audio_file = audio_patch / f"{ARCHIVE}.patch_0{suffix}"
                archive.write(audio_file, f"RC Intro/{ARCHIVE}.patch_1{suffix}")
        archive.writestr("README.txt", credits.rstrip("\n") + "\n"
                         "Load after Clone Armory Opening Videos. Soundtrack requires the separate English audio patch.\n")
    LOG.info("Built %s: %.3fs, %s frames, four slices", zip_path, info["seconds"], info["frames"])
    return zip_path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--template", type=Path, required=True)
    parser.add_argument("--video", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--audio-patch", type=Path, help="Audio staging directory to include in the complete zip")
    parser.add_argument("--title", default="Republic Commando Remastered Intro", help="Zip/package name")
    parser.add_argument("--credits", help="README credit text (default: the RC remaster credits)")
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    try:
        build(args.template, args.video, args.out, args.audio_patch, args.title, args.credits or RC_CREDITS)
        return 0
    except (OSError, ValueError, struct.error, StopIteration) as exc:
        LOG.error("Build failed: %s", exc)
        return 1
    except KeyboardInterrupt:
        return 130


if __name__ == "__main__":
    sys.exit(main())
