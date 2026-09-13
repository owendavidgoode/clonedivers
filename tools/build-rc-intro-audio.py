#!/usr/bin/env -S uv run
"""Stage a PCM replacement for Clone Armory's separate English intro stream.

Keeps every sound-bank byte except the intro source's codec ID. The input WEM
must be Wwise PCM (for example, produced by the independent MIT wwav tool).
Does not write to the game directory. Runtime compatibility requires testing.
"""

import argparse
import hashlib
import json
import struct
from pathlib import Path

ENTRY = struct.Struct('<QQQQQQQIIIIII')
STREAM_ID = 0x27D6E984491581B2
BANK_ID = 0xE8C7DA3561BEE799
STREAM_TYPE = 0x504B55235D21440E
BANK_TYPE = 0x535A7BD3E650D799
DEP_TYPE = 0xAF32095C82F2B070
SOUND_ID = 983687583
SOURCE_ID = 193288922


def change_intro_codec(bank: bytes) -> bytes:
    result = bytearray(bank)
    position = 0
    matches = []
    while position + 8 <= len(bank):
        tag, length = struct.unpack_from('<4sI', bank, position)
        start = position + 8
        end = start + length
        if end > len(bank):
            raise ValueError('Truncated bank chunk')
        if tag == b'HIRC':
            count = struct.unpack_from('<I', bank, start)[0]
            offset = start + 4
            for _ in range(count):
                kind, size, identity = struct.unpack_from('<BII', bank, offset)
                if offset + 5 + size > end:
                    raise ValueError('Truncated HIRC entry')
                if kind == 2 and identity == SOUND_ID:
                    codec, stream_type, source = struct.unpack_from('<IBI', bank, offset + 9)
                    if (codec, stream_type, source) != (0x40001, 2, SOURCE_ID):
                        raise ValueError('Unexpected intro source layout')
                    struct.pack_into('<I', result, offset + 9, 0x10001)
                    matches.append(offset + 9)
                offset += 5 + size
            if offset != end:
                raise ValueError('HIRC entry count mismatch')
        position = end
    if len(matches) != 1 or position != len(bank):
        raise ValueError('Expected exactly one intro sound source')
    restored = bytearray(result)
    struct.pack_into('<I', restored, matches[0], 0x40001)
    if bytes(restored) != bank:
        raise ValueError('An unrelated bank byte changed')
    return bytes(result)


def build(template: Path, wem: Path, output: Path) -> None:
    source = template.read_bytes()
    audio = wem.read_bytes()
    if audio[:4] != b'RIFF' or audio[8:16] != b'WAVEfmt ':
        raise ValueError('Expected a Wwise RIFF/WAVE file with fmt first')
    if struct.unpack_from('<H', audio, 20)[0] != 0xFFFE:
        raise ValueError('Expected Wwise PCM WEM (tag 0xFFFE)')
    magic, type_count, file_count = struct.unpack_from('<III', source)
    if magic != 0xF0000011:
        raise ValueError('Invalid template archive')
    wanted = [(STREAM_ID, STREAM_TYPE), (BANK_ID, BANK_TYPE), (BANK_ID, DEP_TYPE)]
    entries = {}
    for index in range(file_count):
        fields = list(ENTRY.unpack_from(source, 72 + type_count * 32 + index * 80))
        entries[tuple(fields[:2])] = fields
    rows = []
    for identity, typ in wanted:
        row = next(source[72 + index * 32:104 + index * 32]
                   for index in range(type_count)
                   if struct.unpack_from('<Q', source, 80 + index * 32)[0] == typ)
        row = bytearray(row)
        struct.pack_into('<Q', row, 16, 1)
        rows.append(bytes(row))
    header = bytearray(source[:72])
    struct.pack_into('<II', header, 4, len(rows), len(wanted))
    offset = 72 + 32 * len(rows) + ENTRY.size * len(wanted) + 8
    main = bytearray()
    directory = bytearray()
    for index, key in enumerate(wanted):
        fields = entries[key].copy()
        payload = source[fields[2]:fields[2] + fields[7]]
        if key[1] == STREAM_TYPE:
            payload = bytes.fromhex('D82F767800000000') + struct.pack('<Q', len(audio))
            fields[7:10] = [12, len(audio), 0]
        elif key[1] == BANK_TYPE:
            payload = payload[:16] + change_intro_codec(payload[16:])
        fields[2:7] = [offset, 0, 0, 0, 0]
        fields[12] = index
        directory.extend(ENTRY.pack(*fields))
        payload += bytes((-len(payload)) % 16)
        main.extend(payload)
        offset += len(payload)
    patch = bytes(header) + b''.join(rows) + directory + bytes(8) + main
    patch += bytes(max(0, len(wanted) * 256 - len(patch)))
    stream = audio + bytes((-len(audio)) % 16)
    output.mkdir(parents=True, exist_ok=True)
    path = output / '9ba626afa44a3aa3.patch_0'
    path.write_bytes(patch)
    stream_path = path.with_name(path.name + '.stream')
    stream_path.write_bytes(stream)
    report = {'template': str(template.resolve()), 'wem': str(wem.resolve()),
              'streamResource': f'{STREAM_ID:016x}', 'sourceID': SOURCE_ID,
              'soundID': SOUND_ID, 'codec': 'PCM', 'runtimeTested': False,
              'bankChange': 'Only intro source codec: 0x40001 to 0x10001',
              'files': [{'name': p.name, 'bytes': p.stat().st_size,
                         'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}
                        for p in (path, stream_path)]}
    (output / 'build-report.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--template', required=True, type=Path)
    parser.add_argument('--wem', required=True, type=Path)
    parser.add_argument('--out', required=True, type=Path)
    args = parser.parse_args()
    build(args.template, args.wem, args.out)


if __name__ == '__main__':
    main()
