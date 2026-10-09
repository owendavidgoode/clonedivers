#!/usr/bin/env python3
"""Repair ship archive resource alignment without changing hull or shader bytes."""
from __future__ import annotations

import importlib.util
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('finish_common_ship_build', ROOT / 'tools/finish-common.py')
C = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = C
spec.loader.exec_module(C)
L = C.module('finish_ship_material_refs', ROOT / 'tools/build-xwa-lambda-textures.py')
P = C.module('finish_ship_body_ids', ROOT / 'tools/boot-ship-winding-peer.py')
TYPE = struct.Struct('<IIQIIII')


def pad(part: bytearray, alignment: int) -> None:
    C.require(alignment > 0 and alignment & (alignment - 1) == 0, 'Power-of-two native alignment required')
    part.extend(bytes((-len(part)) % alignment))


def main() -> None:
    out = C.TASK / 'ships/alignment-candidate-v1'
    C.require(not out.exists(), 'Fresh ship candidate required')
    wins, _ = C.winners()
    unit_keys = {(identity, C.UNIT) for identity in P.SHIPS}
    material_keys = set()
    for key in unit_keys:
        main_bytes = wins[key].payloads()[0]
        at = struct.unpack_from('<I', main_bytes, 112)[0]
        C.require(struct.unpack_from('<I', main_bytes, at)[0] == 1, 'Reviewed singleton hull material expected')
        material_keys.add((struct.unpack_from('<Q', main_bytes, at + 8)[0], C.MATERIAL))
    C.require(len(material_keys) == 1, 'Hull material changed between variants')
    texture_keys = set()
    for key in material_keys:
        refs = L.material_refs(wins[key].payloads()[0])
        C.require(len(refs) == 2, 'Reviewed two-texture native ship material expected')
        texture_keys.update((identity, C.TEXTURE) for _, identity in refs.values())
    keys = unit_keys | material_keys | texture_keys
    C.require(len(keys) == 6, 'Complete reviewed ship closure expected')
    first = wins[next(iter(unit_keys))].archive.read_bytes()
    old_types = {TYPE.unpack_from(first, 72 + 32 * i)[2]: TYPE.unpack_from(first, 72 + 32 * i)
                 for i in range(struct.unpack_from('<I', first, 4)[0])}
    used_types = sorted({key[1] for key in keys})
    header = bytearray(first[:72])
    struct.pack_into('<II', header, 4, len(used_types), len(keys))
    types = []
    for identity in used_types:
        values = list(old_types[identity])
        values[3] = sum(key[1] == identity for key in keys)
        types.append(TYPE.pack(*values))
    prefix = bytes(header) + b''.join(types)
    parts = [bytearray(prefix + bytes(80 * len(keys))), bytearray(), bytearray()]
    rows, changes = [], []
    # The native type table describes contiguous resource ranges. Group rows in
    # that table's type order before ordering the names within each range.
    ordered_keys = sorted(keys, key=lambda key: (key[1], key[0]))
    for index, key in enumerate(ordered_keys):
        winner = wins[key]
        payloads = winner.payloads()
        row = list(winner.row)
        for i, payload in enumerate(payloads):
            if payload:
                pad(parts[i], row[11] if i == 2 else row[10])
                row[2 + i] = len(parts[i])
                parts[i].extend(payload)
            else:
                row[2 + i] = 0
        row[12] = index
        rows.append(row)
        changes.append({'typedKey': f'{key[0]:016x}.{key[1]:016x}', 'before': winner.evidence(),
                        'afterRow': row, 'payloadSha256s': list(map(C.sha, payloads)),
                        'oldGpuAlignmentRemainder': winner.row[4] % winner.row[11] if payloads[2] else 0,
                        'newGpuAlignmentRemainder': row[4] % row[11] if payloads[2] else 0})
    for index, row in enumerate(rows):
        C.ROW.pack_into(parts[0], len(prefix) + 80 * index, *row)
    expected_types = [identity for identity in used_types
                      for _ in range(sum(key[1] == identity for key in keys))]
    C.require([row[1] for row in rows] == expected_types,
              'Resource rows must be contiguous in native type-table order')
    C.require([row[12] for row in rows] == list(range(len(rows))),
              'Resource row ordinals must be dense in serialized order')
    # Approximate sizes are opaque in this installed mod header and stay exact.
    directory = out / 'bundle'
    directory.mkdir(parents=True)
    files = []
    for suffix, value in zip(C.SUFFIXES, parts, strict=True):
        path = directory / ('9ba626afa44a3aa3.patch_0' + suffix)
        path.write_bytes(value)
        files.append(C.pin(path))
    C.require(all(row['newGpuAlignmentRemainder'] == 0 for row in changes), 'New GPU alignment failed')
    C.require(sum(row['oldGpuAlignmentRemainder'] != 0 for row in changes) >= 3,
              'Expected actual deployed hull alignment defects changed')
    C.save(out / 'report.json', {'passed': True, 'baseline': C.pin(C.BASE / 'manifest.json'),
                               'builder': C.pin(Path(__file__)),
                               'primaryDirectoryContract': C.pin(ROOT / 'dist/rc-upgrade/filediver-source/stingray/archive.go'),
                               'context': C.pin(C.TASK / 'ships/context-v1/report.json'),
                               'files': files, 'resourceCount': len(keys), 'resources': changes,
                               'allResourcePayloadBytesExact': True,
                               'change': 'Honor each current MainAlignment/GPUAlignment in physical resource placement.',
                               'gameOrSettingsChanged': False, 'installed': False, 'runtimeAccepted': False})
    print('Built six-resource ship closure with exact payload bytes and aligned resource starts:', out)


if __name__ == '__main__':
    main()
