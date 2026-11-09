#!/usr/bin/env python3
"""Reproduce the r45 ship-slot disposition using read-only pinned sources."""
from __future__ import annotations

import functools
from pathlib import Path
import struct

import empire_port as P
from inspect_inputs import native_families
from verify_candidate import MATERIAL, material_map, pointers, ship_helper_evidence


def fixture(parts: tuple[bytes, ...], name: str) -> None:
    """Keep real section bytes, rebasing only its buffer extents for small tests."""
    raw, _, gpu = parts
    main = bytearray(raw)
    mesh = pointers(raw, 100)[0]
    layout = pointers(raw, 92)[struct.unpack_from('<i', raw, mesh + 60)[0]]
    group_at = mesh + struct.unpack_from('<I', raw, mesh + 124)[0]
    material, base, count, start, length, group_index = struct.unpack_from('<6I', raw, group_at)
    stride = struct.unpack_from('<I', raw, layout + 356)[0]
    total = struct.unpack_from('<I', raw, layout + 392)[0]
    vo, vb, io, ib = struct.unpack_from('<4I', raw, layout + 416)
    width = ib // total
    vertex_data = gpu[vo + base * stride:vo + (base + count) * stride]
    index_data = gpu[io + start * width:io + (start + length) * width]
    struct.pack_into('<6I', main, group_at, material, 0, count, 0, length, group_index)
    struct.pack_into('<I', main, layout + 352, count)
    struct.pack_into('<I', main, layout + 392, length)
    struct.pack_into('<4I', main, layout + 416, 0, len(vertex_data), len(vertex_data), len(index_data))
    folder = P.ROOT / 'archive-fixture' / 'ship-helper'
    folder.mkdir(parents=True, exist_ok=True)
    (folder / (name + '.main')).write_bytes(main)
    (folder / (name + '.gpu')).write_bytes(vertex_data + index_data)


def main() -> int:
    index_path = P.ROOT / 'native-index-25480438.json'
    index = P.read(index_path)
    module = P.load('ship_proof_native', 'walker-next-native.py')
    native = module.NativeSlim(P.GAME)
    native.chunk = functools.lru_cache(maxsize=32)(native.chunk)
    fresh = {}
    families = native_families(native.archives, P.GAME)
    for i, family in enumerate(families):
        header = native.read(family, 0, 72)
        magic, types, count = struct.unpack_from('<III', header)
        assert magic == 0xf0000011
        rows = P.ROW.iter_unpack(native.read(family, 72 + 32 * types, 80 * count))
        for row in rows:
            label = f'{row[0]:016x}.{row[1]:016x}'
            fresh.setdefault(label, []).append({'archive': family, 'row': list(row)})
        if i % 500 == 0:
            print('Fresh native directories', i, len(fresh), flush=True)
    assert fresh == index, 'Native index no longer matches complete fresh directory scan'
    hashes = P.load('ship_proof_hashes', '../dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d/sdk/tools/hd2_archive.py')
    names = {s: f'{hashes.resource_hash(s):016x}' for s in ('lambert1', 'Default', 'g_hellpod')}
    assert names == {'lambert1': '093fc9345f166874', 'Default': 'cd121f4b2c09f389', 'g_hellpod': 'fe58f1bb981e0616'}
    scans = []
    for path in [index_path, *sorted((P.OLD / 'dist').glob('**/native-index*.json'))]:
        catalog = P.read(path)
        matches = [k for k in catalog if k.startswith('093fc934')]
        assert not matches
        scans.append({'index': P.pin(path), 'typedIdentities': len(catalog), 'thinMatchesAllTypes': matches})
    old, choice = P.matching_build()
    assert not [k for k in old if k[0] >> 32 == 0x093fc934]
    candidate = P.ROOT / 'empire-forge-r45-v3'
    records = []
    for uid in (0x0c6567f9e37920e1, 0x7dcb6f44af36ada1):
        key = uid, P.UNIT
        path = candidate / f'9ba626afa44a3aa3.patch_{1000 + uid % 32}'
        row = next(r for r in P.directory(path) if r[:2] == key)
        parts = P.Resource(path, row, tuple(path.with_name(path.name + s) for s in P.SUFFIXES)).parts()
        assert parts == old[key].parts(), 'Ship no longer byte-exact r41 inheritance'
        providers = index[f'{uid:016x}.{P.UNIT:016x}']
        controls = []
        for provider in providers:
            r = provider['row']
            control = tuple(native.read(provider['archive'] + s, r[2+i], r[7+i]) for i, s in enumerate(P.SUFFIXES))
            evidence = ship_helper_evidence(parts, control, 0x093fc934)
            assert evidence, 'Section no longer meets native equivalence proof'
            controls.append({'archive': provider['archive'], 'payloadSha256': list(map(P.sha, control)), **evidence})
        if uid == 0x0c6567f9e37920e1:
            fixture(parts, 'candidate')
            fixture(control, 'native')
        records.append({'owner': f'{uid:016x}', 'r41Provider': str(old[key].path),
                        'r41ProviderPin': P.pin(old[key].path), 'candidatePayloadSha256': list(map(P.sha, parts)),
                        'r41PayloadExact': True, 'nativeControls': controls})
    previous = P.read(P.ROOT / 'candidate-files.json')
    assert all(P.pin(Path(f['path'])) == f for f in previous), 'Candidate file changed'
    schema = P.OLD / 'dist/rc-upgrade/filediver-source'
    P.save('ship-helper-evidence.json', {'date': '2026-11-08', 'decision': 'c: native-equivalent materialless helper; verifier only',
        'slotNames': names, 'slotIdentityMeaning': 'Thin material slot name, not a material resource reference; no full material identity is bound',
        'freshNativeFamilies': len(families), 'freshNativeIdentities': len(fresh), 'freshNativeIndexExact': True,
        'indexSearches': scans, 'r41Selection': choice, 'r41Resources': len(old), 'r41ThinProviders': [],
        'sourcePins': list(P.SOURCE_PINS.values()), 'ships': records, 'candidateFilesUnchanged': True,
        'primarySources': [P.pin(schema / f) for f in ('stingray/hash.go', 'stingray/unit/unit.go', 'hashes/hashes.txt', 'hashes/thinhashes.txt')],
        'tool': P.pin(Path(__file__)), 'runtimeTested': False,
        'limits': ['No blanket mesh-type exemption; rendered type-5 sections remain checked.',
                   'Native materialless behavior is preserved. Live rendering/mission acceptance remains pending.']})
    print('PASS: all native directories, five indexes, r41 lineage, every native ship provider, unchanged candidate files', flush=True)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
