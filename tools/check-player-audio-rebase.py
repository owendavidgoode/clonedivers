#!/usr/bin/env python3
"""Verify serialized player audio against current banks and published sources."""

import argparse
import importlib.util
import json
import logging
import re
import struct
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('audio_rebase_check', ROOT / 'tools/rebase-player-audio.py')
A = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = A
spec.loader.exec_module(A)


def read_bundle(path: Path) -> dict:
    data = tuple(path.with_name(path.name+suffix).read_bytes() if path.with_name(path.name+suffix).exists() else b'' for suffix in A.V.SUFFIXES)
    return A.V.read_bundle(data, str(path))


def check(folder: Path, index_path: Path) -> dict:
    report = json.loads((folder / 'report.json').read_text(encoding='utf-8'))
    index = json.loads(index_path.read_text(encoding='utf-8'))
    baseline = json.loads((ROOT / 'manifest-v3.json').read_text(encoding='utf-8'))['pack']['files']
    banks, stream_count, published_parts = 0, 0, 0
    for bundle in report['bundles']:
        resource_set = read_bundle(folder / 'base' / bundle['name'])
        rows = [row for row in report['banks'] if row['patch'] == bundle['name']]
        source_path = ROOT / 'dist/mods' / ('recovered - '+rows[0]['mod'].replace('/', '_')+'.zip')
        with zipfile.ZipFile(source_path) as archive:
            data, old = A.V.read_zip_bundle(archive, bundle['source'])
        for suffix, blob in zip(A.V.SUFFIXES, data):
            matches = [file for file in baseline if file['name'] == bundle['name']+suffix]
            if blob and not any(file['size'] == len(blob) and file['sha256'] == A.V.sha(blob) for file in matches):
                raise ValueError(f'Source does not match published bytes: {bundle["name"]}{suffix}')
            published_parts += bool(blob)
        for key, resource in old.items():
            if key[1] not in (A.BANK, A.DEP):
                if resource_set[key].payloads != resource.payloads:
                    raise ValueError('An existing external recording or unrelated resource changed')
                stream_count += key[1] == A.STREAM
        for row in rows:
            key = int(row['bank'], 16), A.BANK
            result = resource_set[key].payloads[0]
            current = (ROOT / index[row['bank']]).read_bytes()
            before, after = A.chunks(current), A.chunks(result)
            if A.V.sha(result) != row['rebasedSha256'] or before.keys() != after.keys():
                raise ValueError('Bank receipt/chunk mismatch')
            if any(before[tag] != after[tag] for tag in before if tag not in (b'DIDX', b'DATA')):
                raise ValueError('Current bank routing or properties changed')
            old_media, current_media, final_media = A.media(A.chunks(old[key].payloads[0])), A.media(before), A.media(after)
            if final_media != {identity: old_media.get(identity, audio) for identity, audio in current_media.items()}:
                raise ValueError('Embedded sample preservation failed')
            banks += 1
    expanded_path = ROOT / 'dist/rc-voice-expansion-2026-09-23/patch/9ba626afa44a3aa3.patch_0'
    original = read_bundle(expanded_path)
    delta = read_bundle(folder / 'rc/9ba626afa44a3aa3.patch_0')
    if original.keys() != delta.keys():
        raise ValueError('RC resource coverage changed')
    for key, resource in original.items():
        if key[1] == A.STREAM and delta[key].payloads != resource.payloads:
            raise ValueError('Expanded RC recording changed')
    for slot in report['rc']:
        number = next(number for number, name in A.RC.CHARACTERS.items() if name == slot['character'])
        identity = A.RC.BANK_IDS[number]
        current = (ROOT / index[f'{identity:016x}']).read_bytes()
        result = delta[identity, A.BANK].payloads[0]
        restored = bytearray(result)
        for change in slot['changes']:
            position = 16 + change['codec_offset']
            if struct.unpack_from('<I', restored, position)[0] != 0x10001:
                raise ValueError('RC codec edit missing')
            struct.pack_into('<I', restored, position, 0x40001)
        if bytes(restored) != current:
            raise ValueError('RC bank differs beyond mapped codec edits')
    return {'banksVerified': banks, 'bundlesVerified': len(report['bundles']),
            'publishedSourcePartsMatched': published_parts, 'existingStreamsUnchanged': stream_count,
            'rcStreamsUnchanged': sum(key[1] == A.STREAM for key in delta),
            'rcBanksOnlyCodecEdits': 4, 'runtimeTested': False}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--audio', type=Path, required=True)
    parser.add_argument('--current-index', type=Path, required=True)
    args = parser.parse_args()
    try:
        result = check(args.audio, args.current_index)
        (args.audio / 'verification.json').write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
        print(json.dumps(result))
        return 0
    except (OSError, ValueError, KeyError, struct.error):
        logging.exception('Serialized audio check failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
