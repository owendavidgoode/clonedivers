#!/usr/bin/env -S uv run
"""Stage RC voice and armor labels while preserving the winning English text bank.

Requires a verified current winning patch as template and filediver's current US
strings JSON as key evidence. No game, app, or recipe files are modified.
The output must be loaded only with Republic Commando mode, after existing text mods.
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
ENTRY = struct.Struct("<QQQQQQQIIIIII")
STRINGS = 0x0D972BAB10B40FD3
RESOURCE = 0x7C7587B563F10985
US = 0x03F97B57
ARCHIVE = "9ba626afa44a3aa3"
# IDs verified from installed US strings, rather than generated from guessed names.
LABELS = {
    1849445015: ("Default Helldiver Voice 1", "Sev"),
    1951373264: ("DEFAULT HELLDIVER VOICE 1", "SEV"),
    1048652091: ("Default Helldiver Voice 2", "Fixer"),
    1502976624: ("DEFAULT HELLDIVER VOICE 2", "FIXER"),
    2441287808: ("Default Helldiver Voice 3", "Scorch"),
    3193874095: ("DEFAULT HELLDIVER VOICE 3", "SCORCH"),
    3149181118: ("Default Helldiver Voice 4", "Boss"),
    534138438: ("DEFAULT HELLDIVER VOICE 4", "BOSS"),
    2740153811: ("DP-11 Champion of the People", "Boss — DP-11"),
    3555499414: ("DP-11 CHAMPION OF THE PEOPLE", "BOSS — DP-11"),
    2778148389: ("CM-10 Clinician", "Fixer — CM-10"),
    1763602207: ("CM-10 CLINICIAN", "FIXER — CM-10"),
    1265620134: ("CE-35 Trench Engineer", "Scorch — CE-35"),
    2501843501: ("CE-35 TRENCH ENGINEER", "SCORCH — CE-35"),
    3161943025: ("SC-30 Trailblazer Scout", "Sev — SC-30"),
    3046397365: ("SC-30 TRAILBLAZER SCOUT", "SEV — SC-30"),
}
RANDOM_KEYS = (676187033, 2442443121, 3141673940)


def parse_strings(data: bytes) -> tuple[dict[int, str], dict[int, int]]:
    """Read the game text-bank format, rejecting malformed or duplicate entries."""
    if len(data) < 16 or data[:8] != bytes.fromhex("aef3853e01000000"):
        raise ValueError("Unsupported strings header")
    count, language = struct.unpack_from("<II", data, 8)
    table_end = 16 + count * 8
    if language != US or table_end > len(data):
        raise ValueError("Expected a complete English (US) strings table")
    entries: dict[int, str] = {}
    offsets: dict[int, int] = {}
    for index in range(count):
        key = struct.unpack_from("<I", data, 16 + index * 4)[0]
        field = 16 + count * 4 + index * 4
        start = struct.unpack_from("<I", data, field)[0]
        if key in entries or start < table_end or start >= len(data):
            raise ValueError(f"Invalid strings entry {key}")
        end = data.index(b"\0", start)
        entries[key] = data[start:end].decode("utf-8")
        offsets[key] = field
    return entries, offsets


def extract_resource(source: bytes) -> tuple[list[int], bytes, bytes]:
    magic, types, files = struct.unpack_from("<III", source)
    if magic != 0xF0000011 or 72 + 32 * types + ENTRY.size * files > len(source):
        raise ValueError("Invalid template patch index")
    rows = [list(ENTRY.unpack_from(source, 72 + 32 * types + ENTRY.size * i))
            for i in range(files)]
    matches = [row for row in rows if row[:2] == [RESOURCE, STRINGS]]
    if len(matches) != 1:
        raise ValueError("Template must contain exactly one expected US text resource")
    entry = matches[0]
    if entry[8] or entry[9] or entry[2] + entry[7] > len(source):
        raise ValueError("Unexpected strings stream or resource extent")
    type_rows = [source[72 + 32 * i:104 + 32 * i] for i in range(types)]
    type_row = next(row for row in type_rows if struct.unpack_from("<Q", row, 8)[0] == STRINGS)
    return entry, type_row, source[entry[2]:entry[2] + entry[7]]


def build(template: Path, base_json: Path, out: Path) -> Path:
    evidence = json.loads(base_json.read_text(encoding="utf-8-sig"))
    if evidence["Language"]["Hash"] != "0x03f97b57":
        raise ValueError("Key evidence is not English (US)")
    base = {item["Key"]: item["Value"] for item in evidence["Items"]}
    for key, (expected, _) in LABELS.items():
        if base.get(key) != expected:
            raise ValueError(f"Current base label differs at key {key}; inspect before rebuilding")
    source = template.read_bytes()
    entry, type_row, original = extract_resource(source)
    before, fields = parse_strings(original)
    if any(key not in before for key in (*LABELS, *RANDOM_KEYS)):
        raise ValueError("Winning text bank is missing a required label")
    # Append replacement text and change only its offset pointers. Original text
    # storage and every unrelated offset remain byte-for-byte unchanged.
    edited = bytearray(original)
    for key, (_, value) in LABELS.items():
        struct.pack_into("<I", edited, fields[key], len(edited))
        edited.extend(value.encode("utf-8") + b"\0")
    after, _ = parse_strings(bytes(edited))
    changed = {key for key in before if before[key] != after[key]}
    if changed != set(LABELS) or before.keys() != after.keys():
        raise ValueError("Text changes do not match the exact 16-key edit set")
    unedited = bytearray(edited[:len(original)])
    for key in LABELS:
        unedited[fields[key]:fields[key] + 4] = original[fields[key]:fields[key] + 4]
    if bytes(unedited) != original:
        raise ValueError("An unrelated source byte changed")
    header = bytearray(source[:72])
    struct.pack_into("<II", header, 4, 1, 1)
    type_row = bytearray(type_row)
    struct.pack_into("<Q", type_row, 16, 1)
    entry[2:5] = [192, 0, 0]
    entry[7:10] = [len(edited), 0, 0]
    entry[12] = 0
    patch = bytes(header) + bytes(type_row) + ENTRY.pack(*entry) + bytes(8) + bytes(edited)
    patch += bytes((-len(patch)) % 16)
    _, _, written = extract_resource(patch)
    if parse_strings(written)[0] != after:
        raise ValueError("Serialized patch round-trip failed")
    out.mkdir(parents=True, exist_ok=True)
    path = out / f"{ARCHIVE}.patch_0"
    path.write_bytes(patch)
    (out / "modified-us.strings.main").write_bytes(written)
    report = {
        "template": str(template.resolve()),
        "templateSha256": hashlib.sha256(source).hexdigest(),
        "baseKeyEvidence": str(base_json.resolve()),
        "resource": f"{RESOURCE:016x}", "type": f"{STRINGS:016x}",
        "resourceCount": 1, "language": "English (US)",
        "stringCount": len(before), "changedCount": len(changed),
        "runtimeTested": False,
        "scope": "Shared item-name keys; affects every UI using these keys, not a menu-specific override.",
        "requires": "Republic Commando mode only; load after the existing Clone Armory text patch.",
        "changes": [{"key": key, "before": before[key], "after": after[key]}
                    for key in sorted(changed)],
        "randomUnchanged": {str(key): after[key] for key in RANDOM_KEYS},
        "patchSha256": hashlib.sha256(patch).hexdigest(),
    }
    (out / "build-report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    archive_path = out / "Republic Commando Voice and Armor Labels.zip"
    with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.write(path, "RC Voice and Armor Labels/" + path.name)
        archive.writestr("README.txt", "English (US) labels for Republic Commando mode.\n"
                         "Voice 1 Sev, Voice 2 Fixer, Voice 3 Scorch, Voice 4 Boss.\n"
                         "Armor labels retain SC-30, CM-10, CE-35 and DP-11. Random is unchanged.\n"
                         "Load after existing Clone Armory text. Labels do not implement voice audio or automatic matching.\n")
    LOG.info("Built %s: one text resource, %s verified label changes", archive_path, len(changed))
    return archive_path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--template", required=True, type=Path)
    parser.add_argument("--base-json", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    try:
        build(args.template, args.base_json, args.out)
        return 0
    except (OSError, ValueError, KeyError, struct.error, StopIteration) as exc:
        LOG.error("Build failed: %s", exc)
        return 1
    except KeyboardInterrupt:
        return 130


if __name__ == "__main__":
    sys.exit(main())
