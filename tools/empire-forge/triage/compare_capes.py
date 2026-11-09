#!/usr/bin/env python3
"""Compare Forge-native, r19 and r45 cape mesh contracts without running the game."""
from __future__ import annotations

import functools
import json
from pathlib import Path
import struct
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import empire_port as P
import verify_candidate as V
from inventory_suspects import candidate_resources


def describe(parts: tuple[bytes, ...]) -> dict:
    raw = parts[0]
    meshes = V.pointers(raw, 100)
    names_at = struct.unpack_from('<I', raw, 100)[0]
    names = struct.unpack_from(f'<{len(meshes)}I', raw, names_at + 4 + 4 * len(meshes)) if meshes else ()
    return {'payloadSha256': list(map(P.sha, parts)), 'sizes': list(map(len, parts)),
            'header': raw[:160].hex(), 'meshCount': len(meshes), 'meshNames': [f'{n:08x}' for n in names],
            'meshGroupBones': [f'{struct.unpack_from("<I", raw, m + 40)[0]:08x}' for m in meshes],
            'meshTypes': [struct.unpack_from('<I', raw, m + 36)[0] for m in meshes],
            'lodDescriptors': V.lod_descriptors(raw),
            'materialMap': {f'{k:08x}': f'{v:016x}' for k, v in V.material_map(raw).items()}}


def main() -> int:
    resources = candidate_resources()
    baseline = P.baseline()
    index = P.read(ROOT / 'native-index-25480438.json')
    native = P.load('triage_cape_native', 'walker-next-native.py').NativeSlim(P.GAME)
    native.chunk = functools.lru_cache(maxsize=32)(native.chunk)
    records = []
    for name in (0x3c33cf10a26cbb3e, 0xea846ef460cc5ba0):
        key = name, P.UNIT
        label = f'{name:016x}.{P.UNIT:016x}'
        providers = index[label]
        controls = []
        for provider in providers:
            row = provider['row']
            parts = tuple(native.read(provider['archive'] + s, row[2+i], row[7+i]) for i, s in enumerate(P.SUFFIXES))
            controls.append({'archive': provider['archive'], **describe(parts)})
        records.append({'key': label, 'r45': describe(resources[key].parts()),
                        'r19': describe(baseline[key].parts()) if key in baseline else None, 'nativeProviders': controls})
    (ROOT / 'triage-r45/cape-contracts.json').write_text(json.dumps(records, indent=2) + '\n', encoding='utf-8')
    for record in records:
        print(record['key'])
        for name in ('r45', 'r19'):
            print(name, {k: v for k, v in (record[name] or {}).items() if k not in ('header', 'payloadSha256', 'materialMap')})
        print('native', [{k: v for k, v in n.items() if k not in ('header', 'payloadSha256', 'materialMap')} for n in record['nativeProviders']])
    return 0


if __name__ == '__main__':
    sys.exit(main())
