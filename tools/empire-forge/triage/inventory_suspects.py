#!/usr/bin/env python3
"""Inventory changed visual/text resources and correlate dump pointers offline."""
from __future__ import annotations

from collections import Counter
import csv
import functools
import json
from pathlib import Path
import struct
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import empire_port as P
from analyze_dump import Dump


def candidate_resources() -> dict:
    resources = {}
    for path in sorted((ROOT / 'empire-forge-r45-v3').iterdir()):
        if P.MAIN.fullmatch(path.name):
            for row in P.directory(path):
                resources[row[:2]] = P.Resource(path, row, tuple(path.with_name(path.name + s) for s in P.SUFFIXES))
    return resources


def main() -> int:
    out = ROOT / 'triage-r45'
    hashes = P.load('triage_hashes', '../dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d/sdk/tools/hd2_archive.py')
    names, thin = {}, {}
    for filename in ('hashes.txt', 'thinhashes.txt'):
        path = P.OLD / 'dist/rc-upgrade/filediver-source/hashes' / filename
        for name in path.read_text(encoding='utf-8').splitlines():
            if name and not name.startswith('//'):
                value = hashes.resource_hash(name)
                names[value] = name
                thin[value >> 32] = name
    print('Thin hash', thin.get(0xa61438bc), flush=True)
    resources = candidate_resources()
    diff = {r['key']: r for r in P.read(ROOT / 'r19-resource-diff.json')}
    decisions = {r['key']: r for r in P.read(ROOT / 'port-decisions.json')['resources']}
    kinds = {P.UNIT: 'UNIT', P.TEXT: 'STRINGS', 0xeac0b497876adedf: 'MATERIAL', 0xcd4238c6a0c69e32: 'TEXTURE'}
    records = []
    for key, resource in sorted(resources.items()):
        label = f'{key[0]:016x}.{key[1]:016x}'
        name = names.get(key[0], '')
        if key[1] not in kinds and not any(word in name for word in ('galactic', 'wartable', 'war_table', 'planet', 'spaceship')):
            continue
        if diff[label]['change'] == 'same':
            continue
        row = {'key': label, 'type': kinds.get(key[1], f'{key[1]:016x}'), 'name': name,
               'r19Change': diff[label]['change'], 'decision': decisions.get(label, {}).get('decision', 'r41-inherited'),
               'source': decisions.get(label, {}).get('source', ''), 'candidateArchive': resource.path.name,
               'mainBytes': resource.row[7], 'hashA61438bcOffsets': '', 'collidableStrings': ''}
        if key[1] == P.UNIT:
            raw = resource.parts()[0]
            needle = struct.pack('<I', 0xa61438bc)
            row['hashA61438bcOffsets'] = '|'.join(hex(i) for i in range(len(raw)) if raw.startswith(needle, i))
            row['collidableStrings'] = '|'.join(s.decode('ascii', errors='replace') for s in (b'Collidable_head', b'Collidable_hips') if s in raw)
        records.append(row)
    with (out / 'changed-visual-text-resources.csv').open('w', newline='', encoding='utf-8') as handle:
        writer = csv.DictWriter(handle, fieldnames=list(records[0]))
        writer.writeheader()
        writer.writerows(records)
    dump = Dump(Path('D:/ClaudeWork/harness-home/runs/20261108-212200-mods-hellforge/hellforge-session/crash-10888.dmp'))
    pointers = []
    rcx = dump.registers['rcx']
    raw = dump.read(rcx, 256)
    for at in range(0, len(raw), 8):
        value = struct.unpack_from('<Q', raw, at)[0]
        data = dump.read(value, 128)
        pointers.append({'offset': hex(at), 'value': hex(value), 'captured': data is not None,
                         'hex': data.hex() if data else None})
    matches = []
    for record in pointers:
        if not record['hex']:
            continue
        needle = bytes.fromhex(record['hex'])[:32]
        if len(set(needle)) < 5:
            continue
        for key, resource in resources.items():
            if key[1] != P.UNIT:
                continue
            raw = resource.parts()[0]
            at = raw.find(needle)
            if at >= 0:
                matches.append({'pointer': record['offset'], 'key': f'{key[0]:016x}.{key[1]:016x}', 'offset': at})
    summary = {'counts': dict(Counter(r['type'] for r in records)), 'thinHashA61438bc': thin.get(0xa61438bc),
               'runtimeUnitPointers': pointers, 'candidateUnitPointerMatches': matches,
               'unitHashMatches': [r for r in records if r['hashA61438bcOffsets']],
               'namedShipUiResources': [r for r in records if any(w in r['name'] for w in ('galactic', 'wartable', 'war_table', 'planet', 'spaceship', 'destroyer', 'hellpod'))]}
    (out / 'suspect-inventory.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    (out / 'resource-names.json').write_text(json.dumps({f'{k:016x}': v for k, v in names.items()}, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(summary, indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main())
