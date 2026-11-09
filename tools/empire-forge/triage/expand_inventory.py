#!/usr/bin/env python3
"""Record UNIT geometry dependencies and ship-related resource closures for review."""
from __future__ import annotations

from collections import Counter, defaultdict
import csv
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


def main() -> int:
    out = ROOT / 'triage-r45'
    resources = candidate_resources()
    diff = {r['key']: r for r in P.read(ROOT / 'r19-resource-diff.json')}
    decisions = {r['key']: r for r in P.read(ROOT / 'port-decisions.json')['resources']}
    index = P.read(ROOT / 'native-index-25480438.json')
    names = P.read(out / 'resource-names.json')
    units, all_delta = [], []
    for key, resource in sorted(resources.items()):
        label = f'{key[0]:016x}.{key[1]:016x}'
        state = diff[label]['change']
        record = {'key': label, 'typeName': names.get(f'{key[1]:016x}', ''),
                  'resourceName': names.get(f'{key[0]:016x}', ''), 'r19Change': state,
                  'decision': decisions.get(label, {}).get('decision', 'r41-inherited'),
                  'source': decisions.get(label, {}).get('source', '')}
        if state != 'same':
            all_delta.append(record)
        if key[1] == P.UNIT and state != 'same':
            raw = resource.parts()[0]
            group = struct.unpack_from('<Q', raw, 16)[0]
            group_key = group, 0xc4f0f4be7fb0c8d6
            group_label = f'{group:016x}.c4f0f4be7fb0c8d6'
            units.append({**record, 'geometryGroup': f'{group:016x}',
                          'groupIncluded': group_key in resources if group else '',
                          'groupNativeFamilies': '|'.join(p['archive'] for p in index.get(group_label, [])) if group else '',
                          'gpuBytes': resource.row[9], 'descriptorCount': len(V.pointers(raw, 100))})
    ship_roots = {0x0c6567f9e37920e1: 'inherited ship UNIT', 0x7dcb6f44af36ada1: 'background Star Destroyer',
                  0xbb3881db2f8d8164: 'ship windows component', 0xc8ccd4986bf70c7c: 'support cannon planet killer'}
    owners = defaultdict(set)
    for identity in ship_roots:
        key = identity, P.UNIT
        owners[key].add(f'{identity:016x}')
        raw = resources[key].parts()[0]
        for field, kind in ((8, 0x18dead01056b72e9), (16, 0xc4f0f4be7fb0c8d6), (32, 0xa486d4045106165c)):
            value = struct.unpack_from('<Q', raw, field)[0]
            if value:
                owners[value, kind].add(f'{identity:016x}')
        for value in V.material_map(raw).values():
            owners[value, V.MATERIAL].add(f'{identity:016x}')
    pending = [key for key in owners if key[1] == V.MATERIAL]
    visited = set()
    while pending:
        key = pending.pop()
        if key in visited or key not in resources:
            continue
        visited.add(key)
        raw = resources[key].parts()[0]
        if len(raw) < 136:
            continue
        count = struct.unpack_from('<I', raw, 64)[0]
        if count > 10000 or 136 + 12 * count > len(raw):
            raise ValueError(f'Invalid material texture table {key[0]:016x}')
        for value in struct.unpack_from(f'<{count}Q', raw, 136 + count * 4):
            if value:
                owners[value, V.TEXTURE].update(owners[key])
        parent = struct.unpack_from('<Q', raw, 24)[0]
        if parent and parent != key[0]:
            owners[parent, V.MATERIAL].update(owners[key])
    ship = []
    for key, roots in sorted(owners.items()):
        label = f'{key[0]:016x}.{key[1]:016x}'
        if key not in resources:
            continue
        ship.append({'key': label, 'rootShipUnits': '|'.join(sorted(roots)),
                     'typeName': names.get(f'{key[1]:016x}', ''), 'resourceName': names.get(f'{key[0]:016x}', ship_roots.get(key[0], '')),
                     'r19Change': diff[label]['change'], 'decision': decisions.get(label, {}).get('decision', 'r41-inherited'),
                     'source': decisions.get(label, {}).get('source', '')})
    for filename, rows in (('all-resource-delta.csv', all_delta), ('changed-unit-contracts.csv', units), ('ship-context-resources.csv', ship)):
        with (out / filename).open('w', newline='', encoding='utf-8') as handle:
            writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)
    report = {'changedUnits': len(units), 'allResourceDeltaRows': len(all_delta), 'shipContextRows': len(ship),
              'shipContextChangedTypes': dict(Counter(r['typeName'] for r in ship if r['r19Change'] != 'same')),
              'shipRoots': {f'{k:016x}': v for k, v in ship_roots.items()},
              'namedUiDelta': [r for r in all_delta if any(w in r['resourceName'] for w in ('galactic', 'war_table', 'wartable', '/ui/', '/gui/'))],
              'unresolvedDeltaNames': sum(not r['resourceName'] for r in all_delta),
              'nativeOnlyGroupUnits': [r for r in units if r['geometryGroup'] != '0000000000000000' and not r['groupIncluded']]}
    (out / 'expanded-inventory.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'nativeOnlyGroupUnits'}, indent=2))
    print('Changed UNITs using native-only GeometryGroups', len(report['nativeOnlyGroupUnits']))
    return 0


if __name__ == '__main__':
    sys.exit(main())
