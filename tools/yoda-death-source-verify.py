#!/usr/bin/env python3
"""Verify and extract the preserved classic LEGO Star Wars Yoda death recording.

Reads only pinned public downloads within the workspace, extracts one exact ZIP
member, and creates an audition WAV containing the unmodified source PCM. No
game, launcher, preferences, bank, or public-release files are changed.
"""

import argparse
import hashlib
import io
import json
import logging
import struct
import subprocess
import sys
import wave
import zipfile
from collections.abc import Sequence
from pathlib import Path
from typing import Any

LOG = logging.getLogger(__name__)
ZIP_SHA256 = "d2ca59dbe19c7b5638158c1fbb9833594f6878e7df4c1e906462d97540ff3ee2"
WAV_SHA256 = "67c794b9fb1167124b052fd613f4497ed944265593fe74efc93533b09a668b6e"
DECODER_SHA256 = "29df08c557ada8269c92a6abfe3886ad2f65849b0b26ba78a0819b363bbc5b85"
MEMBER = "Character Voices/YODA/YODADEATH.WAV"
SOURCE_PAGE = "https://sounds.spriters-resource.com/pc_computer/legostarwarsthecompletesaga/asset/419697/"
ZIP_URL = "https://sounds.spriters-resource.com/media/assets/416/419697.zip?updated=1755541773"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def describe(path: Path) -> dict[str, Any]:
    data = path.read_bytes()
    return {"path": str(path.resolve()), "bytes": len(data), "sha256": sha256(data)}


def pcm(data: bytes) -> tuple[wave._wave_params, bytes]:
    with wave.open(io.BytesIO(data), "rb") as source:
        params = source.getparams()
        frames = source.readframes(source.getnframes())
    require(params.comptype == "NONE", "Source is not linear PCM")
    require(len(frames) == params.nframes * params.nchannels * params.sampwidth,
            "Source frame length mismatch")
    return params, frames


def run(workspace: Path, source_dir: Path) -> dict[str, Any]:
    workspace = workspace.resolve(strict=True)
    source_dir = source_dir.resolve(strict=True)
    require(source_dir.is_relative_to(workspace), "Source directory must stay within workspace")
    archive = source_dir / "complete-saga-character-voices-419697.zip"
    wiki_wav = source_dir / "YODADEATH.WAV"
    decoder = workspace / "dist/rc-upgrade/vgmstream/vgmstream-cli.exe"
    require(sha256(archive.read_bytes()) == ZIP_SHA256, "Archive pin mismatch")
    require(sha256(wiki_wav.read_bytes()) == WAV_SHA256, "Wiki WAV pin mismatch")
    require(sha256(decoder.read_bytes()) == DECODER_SHA256, "Decoder pin mismatch")

    html = (source_dir / "sounds-resource-character-voices.html").read_text(encoding="utf-8")
    require("YODADEATH.WAV" in html and "419697.zip?updated=1755541773" in html,
            "Preserved archive page no longer identifies the source member/download")
    meta = json.loads((source_dir / "fandom-imageinfo.json").read_text(encoding="utf-8-sig"))
    info = meta["query"]["pages"]["33050"]["imageinfo"][0]
    require(info["size"] == 30064, "Public file size metadata mismatch")
    require(hashlib.sha1(wiki_wav.read_bytes()).hexdigest() == info["sha1"],
            "Public file SHA1 metadata mismatch")
    source_pages = json.loads((source_dir / "fandom-source-pages.json").read_text(encoding="utf-8-sig"))
    game_page = source_pages["query"]["pages"]["10488"]["revisions"][0]["slots"]["main"]["*"]
    require("* Death\n[[File:YODADEATH.WAV]]" in game_page,
            "Classic game character page does not label this death cue")

    with zipfile.ZipFile(archive) as zipped:
        names = zipped.namelist()
        require(names.count(MEMBER) == 1, "Expected one exact Yoda death member")
        bad = zipped.testzip()
        require(bad is None, f"Archive CRC failure: {bad}")
        raw = zipped.read(MEMBER)
    require(raw == wiki_wav.read_bytes(), "Independent game-extraction WAV differs from wiki copy")
    extracted = source_dir / "complete-saga-YODADEATH-original.wav"
    extracted.write_bytes(raw)
    params, samples = pcm(raw)
    require((params.nchannels, params.sampwidth, params.framerate, params.nframes) ==
            (1, 2, 11025, 14705), "Unexpected original PCM properties")
    audition = source_dir / "classic-yoda-death-audition.wav"
    with wave.open(str(audition), "wb") as target:
        target.setparams(params)
        target.writeframes(samples)
    require(pcm(audition.read_bytes()) == (params, samples), "Audition altered source PCM")
    fresh_decode = source_dir / "classic-yoda-death-fresh-decode.wav"
    result = subprocess.run([str(decoder), "-o", str(fresh_decode), str(extracted)],
                            check=True, capture_output=True, text=True)
    (source_dir / "vgmstream-fresh-decode.txt").write_text(result.stdout + result.stderr,
                                                       encoding="utf-8")
    require(pcm(fresh_decode.read_bytes()) == (params, samples),
            "Independent decoder changed source PCM")
    values = struct.unpack(f"<{params.nframes}h", samples)
    nonzero = [index for index, value in enumerate(values) if value]
    require(bool(nonzero), "Source cue is silent")
    return {
        "status": "PASS", "classification": "Classic LEGO Star Wars: The Complete Saga Yoda death scream",
        "basis": "Exact original game archive member and independent wiki copy match byte for byte",
        "sourceArchive": {**describe(archive), "page": SOURCE_PAGE, "download": ZIP_URL,
                          "uploader": "LukeWarnut", "submitted": "2019-10-14",
                          "items": len([name for name in names if not name.endswith("/")]),
                          "selectedMember": MEMBER},
        "wikiCrosscheck": {**describe(wiki_wav), "file": info["descriptionurl"],
                           "download": info["url"], "sha1": info["sha1"],
                           "gameCharacterPage": "https://legogames.fandom.com/wiki/Yoda/LSWCS",
                           "gamePageRevision": source_pages["query"]["pages"]["10488"]["revisions"][0]["timestamp"]},
        "original": describe(extracted), "audition": describe(audition),
        "freshIndependentDecode": describe(fresh_decode),
        "decoder": describe(decoder),
        "audio": {"channels": params.nchannels, "sampleWidthBytes": params.sampwidth,
                  "rateHz": params.framerate, "frames": params.nframes,
                  "durationSeconds": params.nframes / params.framerate,
                  "pcmSha256": sha256(samples), "peakAbsolute": max(abs(value) for value in values),
                  "firstNonzeroFrame": nonzero[0], "lastNonzeroFrame": nonzero[-1]},
        "processing": "No resampling, gain, pitch, denoising, trimming, synthesis, or time stretching; audition strips metadata only",
        "credit": "Original recording from LEGO Star Wars: The Complete Saga; Traveller's Tales / LucasArts. Extraction archive uploaded by LukeWarnut. Independent preserved file at LEGO Games Wiki.",
        "relatedSourceRelease": "Rhys / Sub2Rhys, Yoda - Old Death Sound, https://www.nexusmods.com/legostarwarstheskywalkersaga/mods/19; permits conversion and asset use with credit. This report does not assert the downloaded archive came from that release.",
        "scope": "Workspace source verification only; no gameplay acceptance, LEGO-only routing, live install, or public publication performed",
    }


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0],
                                     **({"suggest_on_error": True} if sys.version_info >= (3, 14) else {}))
    parser.add_argument("workspace", type=Path)
    parser.add_argument("source_dir", type=Path)
    parser.add_argument("-v", "--verbose", action="store_true")
    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.DEBUG if args.verbose else logging.INFO,
                        format="%(levelname)s: %(message)s")
    try:
        report = run(args.workspace, args.source_dir)
        report["script"] = describe(Path(__file__))
        output = args.source_dir / "source-verification.json"
        output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        LOG.info("PASS: authentic classic recording; %s", output.resolve())
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, subprocess.CalledProcessError):
        LOG.exception("Source verification failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
