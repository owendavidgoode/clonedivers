#!/usr/bin/env python3
"""Pin the v4 delivery and inventory the moved crash hash across every r45 type."""
from __future__ import annotations

from collections import Counter
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import empire_port as P
import verify_candidate as V
from build_v4 import FOLDER, EVIDENCE, resources_at


def main() -> int:
    hashes = P.load('v4_hashes', '../dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d/sdk/tools/hd2_archive.py')
    dictionaries = [P.OLD / 'dist/rc-upgrade/filediver-source/hashes' / filename
                    for filename in ('hashes.txt', 'thinhashes.txt')]
    thin_names = {}
    for path in dictionaries:
        for name in path.read_text(encoding='utf-8').splitlines():
            if name and not name.startswith('//'):
                thin_names.setdefault(hashes.resource_hash(name) >> 32, set()).add(name)
    resolved = sorted(thin_names[0xed9eeb61])
    if resolved != ['g_shock_trooper_cape']:
        raise ValueError(f'Unexpected thin hash resolution {resolved}')
    resources = resources_at(ROOT / 'empire-forge-r45-v3')
    names = P.read(ROOT / 'triage-r45/resource-names.json')
    hits, types = [], Counter()
    main_bytes = 0
    for key, resource in sorted(resources.items()):
        types[names.get(f'{key[1]:016x}', f'{key[1]:016x}')] += 1
        raw = resource.parts()[0]
        main_bytes += len(raw)
        offsets, at = [], 0
        while (at := raw.find(struct.pack('<I', 0xed9eeb61), at)) >= 0:
            offsets.append(at)
            at += 1
        if offsets:
            hits.append({'key': f'{key[0]:016x}.{key[1]:016x}', 'name': names.get(f'{key[0]:016x}', ''),
                         'type': names.get(f'{key[1]:016x}', ''), 'mainOffsets': offsets,
                         'archive': resource.path.name})
    scan = P.read(EVIDENCE / 'geometry-scan-v3.json')
    for record in scan['references']:
        record['name'] = names.get(record['owner'].split('.')[0], '')
        record['resolvedMeshNames'] = {value: sorted(thin_names.get(int(value, 16), set()))
                                       for value in record.get('meshNames', [])}
    report = {'thinHash': 'ed9eeb61', 'convention': 'Murmur64A resource_hash(name) >> 32',
              'resolvedNames': resolved, 'hashHits': hits, 'resourceTypesScanned': dict(types),
              'mainBytesScanned': main_bytes, 'geometryScan': scan,
              'scope': 'Decode every UNIT GeometryGroup at +16, including unknown IDs. Search every resource MAIN payload for all native-index and selected GeometryGroup IDs. The index contains no separate mesh resource type.',
              'runtimeResidency': 'Unknown. No captured package-residency proof is available. Closure requires included providers without assuming native family residency.',
              'inputs': [P.pin(path) for path in dictionaries] + [P.pin(ROOT / 'native-index-25480438.json')]}
    (EVIDENCE / 'provider-inventory.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    files = P.read(EVIDENCE / 'candidate-files.json')
    if [P.pin(Path(record['path'])) for record in files] != files:
        raise ValueError('Delivered v4 differs from full verifier file pins')
    if {Path(p['path']).name for p in files} != {p.name for p in FOLDER.iterdir()}:
        raise ValueError('Unexpected delivery files')
    before_files = P.read(ROOT / 'candidate-files.json')
    if [P.pin(Path(record['path'])) for record in before_files] != before_files:
        raise ValueError('v3 source changed')
    guids = [P.read(path)['Guid'] for path in ROOT.glob('empire-forge-r45*/manifest.json')]
    if len(guids) != len(set(guids)):
        raise ValueError('Pack GUID collision')
    expected = P.read(EVIDENCE / 'expected-resources.json')
    for key, resource in resources_at(FOLDER).items():
        if list(map(P.sha, resource.parts())) != expected[f'{key[0]:016x}.{key[1]:016x}']:
            raise ValueError('Delivered payload differs from expected')
    result = {'passed': True, 'v4FilesRehashed': len(files), 'v3FilesRehashed': len(before_files),
              'v4ResourcesRehashed': len(expected), 'uniqueGuids': len(guids),
              'geometryProviderRule': P.pin(ROOT / 'verify_candidate.py'),
              'inventory': P.pin(EVIDENCE / 'provider-inventory.json'),
              'runtimeTested': False}
    (EVIDENCE / 'delivery-audit.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main())
