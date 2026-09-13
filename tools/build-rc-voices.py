#!/usr/bin/env -S uv run
"""Build selectable RC voices over the existing clone banks, preserving unmapped audio.

Requires the explicit semantic mapping, current Clone Voice template, and staged
MIT wwav / Audio Modder utilities. Writes staging files only; never the game.
"""

import argparse
import hashlib
import json
import logging
import struct
import sys
import zipfile
from pathlib import Path
from typing import Any

LOG = logging.getLogger(__name__)
ENTRY = struct.Struct('<QQQQQQQIIIIII')
ARCHIVE = '9ba626afa44a3aa3'
STREAM = 0x504B55235D21440E
BANK = 0x535A7BD3E650D799
DEP = 0xAF32095C82F2B070
BANK_IDS = {1: 0x0A39096A51AE9E86, 2: 0x017F8B366AF141F8,
            3: 0x498646C6A1FFBA95, 4: 0x6C91A47B88D26638}
CHARACTERS = {1: 'Sev', 2: 'Fixer', 3: 'Scorch', 4: 'Boss'}


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def archive_entries(data: bytes) -> dict[tuple[int, int], list[int]]:
    magic, types, count = struct.unpack_from('<III', data)
    if magic != 0xF0000011 or 72 + 32 * types + 80 * count > len(data):
        raise ValueError('Invalid template index')
    entries = {}
    for index in range(count):
        row = list(ENTRY.unpack_from(data, 72 + 32 * types + 80 * index))
        if tuple(row[:2]) in entries or row[2] + row[7] > len(data):
            raise ValueError('Duplicate or truncated template resource')
        entries[tuple(row[:2])] = row
    return entries


def patch_bank(bank: bytes, targets: set[int]) -> tuple[bytes, list[dict[str, int]]]:
    """Change codec only on explicitly mapped streamed Sounds; retain every other byte."""
    result = bytearray(bank)
    changed = []
    position = 0
    while position < len(bank):
        if position + 8 > len(bank):
            raise ValueError('Truncated bank chunk')
        tag, length = struct.unpack_from('<4sI', bank, position)
        start, end = position + 8, position + 8 + length
        if end > len(bank):
            raise ValueError('Bank chunk out of bounds')
        if tag == b'BKHD' and struct.unpack_from('<I', bank, start)[0] ^ 0x9211BCAC != 154:
            raise ValueError('Expected current bank version 154')
        if tag == b'HIRC':
            count = struct.unpack_from('<I', bank, start)[0]
            offset = start + 4
            for _ in range(count):
                kind, size, identity = struct.unpack_from('<BII', bank, offset)
                if size < 4 or offset + 5 + size > end:
                    raise ValueError('Malformed HIRC object')
                if kind == 2:
                    codec, stream_type, media = struct.unpack_from('<IBI', bank, offset + 9)
                    if media in targets:
                        if (codec, stream_type) != (0x40001, 2):
                            raise ValueError(f'Mapped media {media} is not a streamed Vorbis Sound')
                        struct.pack_into('<I', result, offset + 9, 0x10001)
                        changed.append({'sound_id': identity, 'media_id': media, 'codec_offset': offset + 9})
                offset += 5 + size
            if offset != end:
                raise ValueError('HIRC object count mismatch')
        position = end
    if {change['media_id'] for change in changed} != targets:
        raise ValueError('Mapped media missing from current clone bank')
    restored = bytearray(result)
    for change in changed:
        struct.pack_into('<I', restored, change['codec_offset'], 0x40001)
    if restored != bank:
        raise ValueError('Unrelated bank bytes changed')
    return bytes(result), changed


def build(args: argparse.Namespace) -> None:
    root = args.workspace.resolve()
    sys.path.insert(0, str(root / 'dist/rc-upgrade/wwav'))
    sys.path.insert(0, str(root / 'dist/rc-upgrade/hd2-audio-modder'))
    from wwav import convert
    from util import murmur64_hash

    template = args.template.read_bytes()
    entries = archive_entries(template)
    mapping = json.loads(args.mapping.read_text(encoding='utf-8-sig'))
    if sorted(int(slot['slot']) for slot in mapping['slots']) != [1, 2, 3, 4]:
        raise ValueError('Exactly four mapped voice slots are required')
    args.out.mkdir(parents=True, exist_ok=True)
    wem_dir = args.out / 'wem'
    wem_dir.mkdir(exist_ok=True)
    replacements: dict[tuple[int, int], tuple[bytes, bytes]] = {}
    reports = []
    for slot in mapping['slots']:
        number = int(slot['slot'])
        if slot['character'] != CHARACTERS[number]:
            raise ValueError('Unexpected character-to-slot assignment')
        bank_id = BANK_IDS[number]
        targets = set()
        for row in slot['rows']:
            media = int(row['hd2_media_id'])
            if media in targets:
                raise ValueError(f'Duplicate mapped media {media}')
            targets.add(media)
            resource = murmur64_hash(f'content/audio/us/{media}'.encode())
            if resource != int(row['hd2_archive_resource_hash'], 16):
                raise ValueError(f'Transcript resource hash mismatch for {media}')
            if (resource, STREAM) not in entries:
                raise ValueError(f'Clone stream resource missing for {media}')
            wav = Path(row['rc_wav'])
            if digest(wav.read_bytes()) != row['rc_sha256']:
                raise ValueError(f'RC source changed: {wav}')
            wem = wem_dir / (row['rc_sha256'] + '.wem')
            if not wem.exists():
                convert(str(wav), str(wem), codec='pcm', quiet=True)
            audio = wem.read_bytes()
            if audio[:4] != b'RIFF' or struct.unpack_from('<H', audio, 20)[0] != 0xFFFE:
                raise ValueError('Invalid PCM WEM')
            key = (resource, STREAM)
            value = (bytes.fromhex('D82F767800000000') + struct.pack('<Q', len(audio)), audio)
            if key in replacements and replacements[key] != value:
                raise ValueError('Conflicting replacements for a shared stream')
            replacements[key] = value
        if not targets:
            raise ValueError(f'No replacements for {slot["character"]}')
        bank_row = entries[bank_id, BANK]
        payload = template[bank_row[2]:bank_row[2] + bank_row[7]]
        bank, changes = patch_bank(payload[16:], targets)
        replacements[bank_id, BANK] = (payload[:16] + bank, b'')
        dep_row = entries[bank_id, DEP]
        replacements[bank_id, DEP] = (template[dep_row[2]:dep_row[2] + dep_row[7]], b'')
        reports.append({'slot': number, 'character': slot['character'], 'bank_id': f'{bank_id:016x}',
                        'mapped_media': len(targets), 'changes': changes,
                        'original_bank_sha256': digest(payload[16:]), 'new_bank_sha256': digest(bank)})
    types = [typ for typ in (STREAM, BANK, DEP) if any(key[1] == typ for key in replacements)]
    ordered = sorted(replacements, key=lambda key: (types.index(key[1]), key[0]))
    header = bytearray(template[:72])
    struct.pack_into('<II', header, 4, len(types), len(ordered))
    type_rows = b''.join(struct.pack('<QQQII', 0, typ,
                                    sum(key[1] == typ for key in ordered), 16, 64) for typ in types)
    offset = 72 + len(type_rows) + ENTRY.size * len(ordered) + 8
    directory, main, stream = bytearray(), bytearray(), bytearray()
    for index, key in enumerate(ordered):
        payload, audio = replacements[key]
        fields = entries[key].copy()
        fields[2:7] = [offset, len(stream), 0, 0, 0]
        fields[7:10] = [12 if key[1] == STREAM else len(payload), len(audio), 0]
        fields[12] = index
        directory.extend(ENTRY.pack(*fields))
        main.extend(payload + bytes((-len(payload)) % 16))
        offset += (len(payload) + 15) // 16 * 16
        stream.extend(audio + bytes((-len(audio)) % 16))
    patch = bytes(header) + type_rows + directory + bytes(8) + main
    patch += bytes(max(0, len(ordered) * 256 - len(patch)))
    path = args.out / f'{ARCHIVE}.patch_0'
    path.write_bytes(patch)
    stream_path = path.with_name(path.name + '.stream')
    stream_path.write_bytes(stream)
    loaded = archive_entries(path.read_bytes())
    stored_audio = stream_path.read_bytes()
    for key, (payload, audio) in replacements.items():
        row = loaded[key]
        if patch[row[2]:row[2] + len(payload)] != payload or stored_audio[row[3]:row[3] + row[8]] != audio:
            raise ValueError('Serialized resource differs from intended bytes')
    report: dict[str, Any] = {'slots': reports, 'mapping_sha256': digest(args.mapping.read_bytes()),
        'template_sha256': digest(template), 'resource_count': len(loaded), 'runtime_tested': False,
        'fallback': 'Unmapped clone sounds and shared Standard bank retained unchanged',
        'files': [{'name': p.name, 'size': p.stat().st_size, 'sha256': digest(p.read_bytes())}
                  for p in (path, stream_path)]}
    (args.out / 'build-report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    with zipfile.ZipFile(args.out / 'Republic Commando Voices.zip', 'w', compression=zipfile.ZIP_STORED) as archive:
        for p in (path, stream_path):
            archive.write(p, 'RC Voices/' + p.name)
        archive.writestr('README.txt', 'Republic Commando mode: Voice 1 Sev; 2 Fixer; 3 Scorch; 4 Boss.\n'
                        'Original Republic Commando recordings. Load after Full Clone Voice Conversion.\n'
                        'Unmapped lines and shared exertions retain existing clone audio.\n')
    LOG.info('Built %s resources; mapped voices: %s', len(loaded), [(r['character'], r['mapped_media']) for r in reports])


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--workspace', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--template', type=Path, required=True)
    parser.add_argument('--mapping', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    try:
        build(args)
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, ImportError, struct.error):
        LOG.exception('RC voice build failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
