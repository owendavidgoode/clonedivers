#!/usr/bin/env python3
"""Independently decode the additive Yoda graph with the pinned wwiser parser.

The diagnostic input strips the HD2 resource wrapper and decodes only the
obfuscated BKHD version DWORD (XOR 0x9211BCAC). Every other candidate BANK byte
stays exact. Production files are never rewritten or replaced.
"""
import argparse
import hashlib
import json
import logging
from pathlib import Path
import struct
import subprocess
import sys
from typing import Any
import xml.etree.ElementTree as ET


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def pin(path: Path) -> dict[str, Any]:
    with path.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": digest}


def value(node: ET.Element, name: str) -> list[str]:
    return [field.attrib["value"] for field in node.iter("field") if field.get("name") == name]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("workspace", type=Path)
    parser.add_argument("out", type=Path)
    args = parser.parse_args()
    root, out = args.workspace.resolve(strict=True), args.out.resolve()
    require(out.is_relative_to(root / "dist/empire-yoda-death-2026-10-08/audio") and not out.exists(), "Fresh scoped peer output")
    candidate_report = root / "dist/empire-yoda-death-2026-10-08/audio/candidate-v1/report.json"
    require(pin(candidate_report)["sha256"] == "994b87c9c7f78d5841b10f6dbec1d65c82ffb1f1ec818523a12c53e0051981f5", "Frozen candidate report pin")
    candidate = json.loads(candidate_report.read_text())
    bank_path = Path(candidate["candidateBank"]["path"])
    require(pin(bank_path) == candidate["candidateBank"], "Actual candidate BANK pin")
    original = bank_path.read_bytes()
    require(original[16:20] == b"BKHD", "HD2 wrapper followed by BKHD")
    diagnostic = bytearray(original[16:])
    version = struct.unpack_from("<I", diagnostic, 8)[0] ^ 0x9211BCAC
    require(version == 154, "Native current version154 encoding")
    struct.pack_into("<I", diagnostic, 8, version)
    require(diagnostic[:8] == original[16:24] and diagnostic[12:] == original[28:], "Only bank-version DWORD decoded in diagnostic")
    wwiser = root / "dist/empire-next-2026-10-05/audio/owned-voices-v1/tool/wwiser.pyz"
    require(pin(wwiser)["sha256"] == "f4ba1368895adab285f27fa37b142e5da805bcc6fb77e8daba282dac65d89411", "Independent wwiser tool pin")
    out.mkdir(parents=True)
    diag_path = out / "candidate-schema-version-normalized.bnk"
    diag_path.write_bytes(diagnostic)
    parsed = subprocess.run([sys.executable, "-B", str(wwiser), "-d", "xml", "-dn", str(out / "candidate"), str(diag_path)],
                            capture_output=True, text=True, check=True, cwd=out)
    log_path = out / "wwiser.txt"
    log_path.write_text(parsed.stdout + parsed.stderr, encoding="utf-8")
    require("parser: done" in parsed.stdout + parsed.stderr and not any(needle in (parsed.stdout + parsed.stderr).lower() for needle in ("error", "failed", "exception")), "Independent parser completed without errors")
    xml_path = out / "candidate.xml"
    tree = ET.parse(xml_path).getroot()
    require(not tree.findall(".//error"), "No schema error nodes")
    objects = {}
    for node in tree.iter("object"):
        identity = node.find("./field[@name='ulID']")
        if identity is not None:
            key = int(identity.attrib["value"])
            require(key not in objects, "Unique parsed version154 HIRC IDs")
            objects[key] = node
    require(len(objects) == 2644, "All2644 actual serialized objects independently decoded")
    ids = candidate["ids"]
    expected = {"event": "CAkEvent", "action": "CAkActionPlay", "sound": "CAkSound", "mixer": "CAkActorMixer"}
    for key, classname in expected.items():
        require(objects[ids[key]].get("name") == classname, "New graph object schema class: " + key)
    event, action, sound, mixer = (objects[ids[key]] for key in ("event", "action", "sound", "mixer"))
    require(value(event, "ulActionListSize") == ["1"] and value(event, "ulActionID") == [str(ids["action"])], "Event contains exactly the new Play action")
    require(value(action, "ulActionType") == ["1027"] and value(action, "idExt") == [str(ids["sound"])] and value(action, "bankType") == ["0"], "Version154 PlayAction targets only dedicatedSound/nativebanktype")
    fields = {"ulPluginID": "65537", "StreamType": "0", "sourceID": str(ids["media"]), "cacheID": str(ids["cache"]),
              "uInMemoryMediaSize": str(candidate["wem"]["bytes"]), "uSourceBits": "0", "DirectParentID": str(ids["mixer"])}
    require(all(value(sound, name) == [expected] for name, expected in fields.items()), "Independent PCM/Data-bnk/fullmemory/nonlanguage source decoding")
    require(value(sound, "bIsLanguageSpecific") == ["0"] and value(sound, "bPrefetch") == ["0"], "New source independent of localization or streaming")
    require(value(mixer, "OverrideBusId") == ["2467607253"] and value(mixer, "DirectParentID") == ["0"] and
            value(mixer, "ulNumChilds") == ["1"] and value(mixer, "ulChildID") == [str(ids["sound"])], "Deterministic one-child parent and nativeInit output bus")
    require(value(sound, "uNumCurves") == ["0"] and value(mixer, "uNumCurves") == ["0"] and
            value(sound, "ulNumStateGroups") == ["0"] and value(mixer, "ulNumStateGroups") == ["0"], "No RTPC or state dependencies in new graph")
    require(not any("loop" in field.get("valuefmt", "").lower() for node in (sound, mixer) for field in node.iter("field") if field.get("name") == "pID"), "No loop property in new graph")
    proofs = {key: {"id": ids[key], "class": objects[ids[key]].get("name"),
                    "fieldValues": {name: value(objects[ids[key]], name) for name in
                        ("ulActionID", "ulActionType", "idExt", "bankID", "bankType", "ulPluginID", "StreamType", "sourceID", "cacheID",
                         "uInMemoryMediaSize", "DirectParentID", "OverrideBusId", "ulNumChilds", "ulChildID", "bIsLanguageSpecific", "bPrefetch")
                                    if value(objects[ids[key]], name)}} for key in expected}
    report = {"passed": True, "tool": pin(Path(__file__)), "candidateReport": pin(candidate_report), "actualCandidateBank": pin(bank_path),
              "independentParser": pin(wwiser), "schemaVersion": 154, "diagnosticBank": pin(diag_path), "diagnosticXml": pin(xml_path),
              "parserLog": pin(log_path), "onlyDiagnosticVersionDwordDecoded": True, "productionBankUnchanged": pin(bank_path) == candidate["candidateBank"],
              "parsedHircObjects": len(objects), "newGraphIndependentSchema": proofs,
              "oneShot": True, "residentPcmSource": True, "languageNeutral": True, "noStateRtpcOrLoopDependency": True,
              "positioning": "Copied native generic voice root uses DirectSpeakerAssignment with no listener-relative routing; local self cue follows native generic voice bus rather than adding an unproven spatial attenuation object.",
              "gameSettingsPublicOrDeploymentChanged": False, "enginePlaybackAccepted": False}
    output = out / "report.json"
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(pin(output)))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        sys.exit(130)
    except (OSError, ValueError, KeyError, ET.ParseError, subprocess.CalledProcessError):
        logging.exception("Independent wwiser graph verification failed")
        sys.exit(1)
