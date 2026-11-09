#!/usr/bin/env python3
"""Independently read back the offline Forge candidate and its native contracts."""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import csv
import functools
import json
import logging
from pathlib import Path
import struct
import sys

import empire_port as P

MATERIAL = 0xeac0b497876adedf
TEXTURE = 0xcd4238c6a0c69e32
GEOMETRY_GROUP = 0xc4f0f4be7fb0c8d6


def geometry_provider_scan(resources: dict, index: dict) -> dict:
    """Require explicit geometry closure for consolidated overrides.

    A native index entry proves existence, not residency. No native family is
    assumed resident merely because one of its UNITs was copied to patch_1000+.
    Also search every resource type for known GeometryGroup identities so a
    consumer outside the decoded UNIT schema cannot silently bypass this rule.
    """
    groups = {int(label.split('.')[0], 16) for label in index
              if label.endswith(f'.{GEOMETRY_GROUP:016x}')}
    groups.update(key[0] for key in resources if key[1] == GEOMETRY_GROUP)
    needles = {group: struct.pack('<Q', group) for group in groups}
    references, findings = [], []
    units = 0
    for key, resource in sorted(resources.items()):
        raw = resource.parts()[0]
        label = f'{key[0]:016x}.{key[1]:016x}'
        hits = {group: raw.find(needle) for group, needle in needles.items() if needle in raw}
        if key[1] == P.UNIT:
            units += 1
            group = struct.unpack_from('<Q', raw, 16)[0]
            if group:
                hits[group] = 16  # Includes unknown/dangling IDs absent from both indexes.
        for group, offset in sorted(hits.items()):
            provider_key = group, GEOMETRY_GROUP
            provider = f'{group:016x}.{GEOMETRY_GROUP:016x}'
            if key == provider_key:
                continue
            record = {'owner': label, 'key': provider, 'offset': offset,
                      'role': 'external-geometry-provider',
                      'nativeFamilies': [p['archive'] for p in index.get(provider, [])],
                      'providerInPack': provider_key in resources}
            if key[1] == P.UNIT and offset == 16:
                meshes = pointers(raw, 100)
                at = struct.unpack_from('<I', raw, 100)[0]
                names = struct.unpack_from(f'<{len(meshes)}I', raw, at + 4 + 4 * len(meshes)) if meshes else ()
                record['meshNames'] = [f'{name:08x}' for name in names]
            references.append(record)
            if provider_key not in resources:
                findings.append({**record, 'reason': 'Native existence does not prove residency. Include provider or inline geometry'})
                continue
            if key[1] != P.UNIT or offset != 16:
                continue
            gg, _, gpu = resources[provider_key].parts()
            count = struct.unpack_from('<I', gg, 8)[0]
            if count > 10000 or 16 + 20 * count > len(gg):
                raise ValueError('Invalid GeometryGroup owner table')
            owners = [struct.unpack_from('<Q', gg, 24 + 16 * i)[0] for i in range(count)]
            if owners.count(key[0]) != 1:
                findings.append({**record, 'reason': 'GeometryGroup does not uniquely provide owner UNIT'})
                continue
            root = struct.unpack_from('<I', gg, 16 + 16 * count + 4 * owners.index(key[0]))[0]
            mesh_count = struct.unpack_from('<I', gg, root)[0]
            if mesh_count > 10000 or root + 4 + 8 * mesh_count > len(gg):
                raise ValueError('Invalid GeometryGroup mesh table')
            provided_names = struct.unpack_from(f'<{mesh_count}I', gg, root + 4)
            if not set(names) <= set(provided_names):
                findings.append({**record, 'reason': 'GeometryGroup lacks UNIT mesh names'})
            layouts = pointers(gg, 12)
            for layout in layouts:
                vo, vb, io, ib = struct.unpack_from('<4I', gg, layout + 416)
                if vo + vb > len(gpu) or io + ib > len(gpu):
                    findings.append({**record, 'reason': 'GeometryGroup GPU extent outside provider payload'})
                    break
    return {'passed': not findings, 'resourcesScanned': len(resources), 'unitsScanned': units,
            'knownGeometryProviders': len(groups), 'references': references, 'findings': findings,
            'residencyPolicy': 'Consolidated overrides require providers in the pack. Native index membership alone is insufficient'}


def pointers(raw: bytes, field: int) -> list[int]:
    at = struct.unpack_from('<I', raw, field)[0]
    if not at:
        return []
    count = struct.unpack_from('<I', raw, at)[0]
    if count > 10000 or at + 4 + 4 * count > len(raw):
        raise ValueError('Invalid bounded pointer table')
    return [at + struct.unpack_from('<I', raw, at + 4 + 4 * i)[0] for i in range(count)]


def material_map(raw: bytes) -> dict[int, int]:
    at = struct.unpack_from('<I', raw, 112)[0]
    if not at:
        return {}
    count = struct.unpack_from('<I', raw, at)[0]
    if count > 10000 or at + 4 + 12 * count > len(raw):
        raise ValueError('Invalid material map')
    return dict(zip(struct.unpack_from(f'<{count}I', raw, at + 4),
                    struct.unpack_from(f'<{count}Q', raw, at + 4 + 4 * count), strict=True))


def lod_descriptors(raw: bytes) -> list[list[int]]:
    entries = []
    meshes = pointers(raw, 100)
    for at in pointers(raw, 48):
        levels = struct.unpack_from('<I', raw, at + 16)[0]
        if levels > 10000 or at + 20 + 4 * levels > len(raw):
            raise ValueError('Invalid LOD table')
        for i in range(levels):
            entry = at + struct.unpack_from('<I', raw, at + 20 + 4 * i)[0]
            count = struct.unpack_from('<I', raw, entry + 8)[0]
            if count > 10000 or entry + 12 + 4 * count > len(raw):
                raise ValueError('Invalid LOD entry')
            indices = list(struct.unpack_from(f'<{count}I', raw, entry + 12))
            if any(v >= len(meshes) for v in indices):
                raise ValueError('Invalid LOD descriptor')
            entries.append(indices)
    return entries


def ship_helper_evidence(parts: tuple[bytes, ...], control: tuple[bytes, ...], thin_id: int) -> dict | None:
    """Recognize only the native-equivalent, materialless g_hellpod section.

    lambert1 and Default are slot names, not material resource identities. Mesh
    type 5 alone is insufficient: other sections with that type are rendered.
    """
    if thin_id != 0x093fc934:
        return None
    signatures, records = [], []
    for payloads, slot in ((parts, thin_id), (control, 0xcd121f4b)):
        raw, _, gpu = payloads
        if struct.unpack_from('<Q', raw, 16)[0] or slot in material_map(raw):
            return None
        meshes = pointers(raw, 100)
        entries = lod_descriptors(raw)
        if not meshes or not entries or any(0 in e for e in entries):
            return None
        uses = []
        for di, mesh in enumerate(meshes):
            nm, mo = struct.unpack_from('<II', raw, mesh + 104)
            materials = struct.unpack_from(f'<{nm}I', raw, mesh + mo)
            ng, go = struct.unpack_from('<II', raw, mesh + 120)
            for gi in range(ng):
                group = struct.unpack_from('<6I', raw, mesh + go + 24 * gi)
                if group[4] and materials[group[0]] == slot:
                    uses.append((di, mesh, group))
        if len(uses) != 1 or uses[0][0] != 0:
            return None
        _, mesh, group = uses[0]
        if struct.unpack_from('<II', raw, mesh + 36) != (5, 0xfe58f1bb):
            return None
        if group[0] != 0 or group[2] != 121 or group[4] != 600:
            return None
        layout_index = struct.unpack_from('<i', raw, mesh + 60)[0]
        layouts = pointers(raw, 92)
        if not 0 <= layout_index < len(layouts):
            return None
        layout = layouts[layout_index]
        # Resolve XYZ within the interleaved layout (the export adds color).
        sizes = {0: 4, 2: 12, 4: 4, 28: 4, 30: 4, 33: 4, 35: 8}
        attributes = struct.unpack_from('<I', raw, layout + 328)[0]
        if not 0 < attributes <= 16:
            return None
        position_offset, cursor = None, 0
        for ai in range(attributes):
            kind, fmt, layer = struct.unpack_from('<3I', raw, layout + 8 + 20 * ai)
            if fmt not in sizes:
                return None
            if (kind, fmt, layer) == (0, 2, 0):
                position_offset = cursor
            cursor += sizes[fmt]
        vertices, stride = struct.unpack_from('<II', raw, layout + 352)
        if position_offset is None or cursor != stride:
            return None
        indices = struct.unpack_from('<I', raw, layout + 392)[0]
        vo, vb, io, ib = struct.unpack_from('<4I', raw, layout + 416)
        width = ib // indices if indices else 0
        if width not in (2, 4) or ib != indices * width or stride < 12:
            return None
        if vo + vertices * stride > len(gpu) or io + ib > len(gpu):
            raise ValueError('Helper GPU extent')
        base, count, start, length = group[1:5]
        if base + count > vertices or start + length > indices:
            raise ValueError('Helper group extent')
        positions = [struct.unpack_from('<3f', gpu, vo + (base + i) * stride + position_offset) for i in range(count)]
        corners = struct.unpack_from(f'<{length}{"H" if width == 2 else "I"}', gpu, io + start * width)
        if any(i >= count for i in corners):
            raise ValueError('Helper index outside section')
        # Export reorders vertices and triangles; compare complete geometric
        # triangles, retaining multiplicity, rather than buffer offsets/order.
        triangles = sorted(tuple(sorted(positions[i] for i in corners[t:t+3])) for t in range(0, length, 3))
        signature = (sorted(positions), triangles, raw[mesh + 32:mesh + 56])
        signatures.append(signature)
        records.append({'slot': f'{slot:08x}', 'descriptor': 0, 'meshType': 5,
                        'groupBone': 'fe58f1bb', 'vertices': count, 'indices': length,
                        'lodDescriptors': entries, 'mapped': False,
                        'geometrySha256': P.sha(repr(signature[:2]).encode('ascii'))})
    if signatures[0] != signatures[1]:
        return None
    return {'candidateSection': records[0], 'nativeSection': records[1],
            'evidence': 'Native-equivalent g_hellpod section; both slot names unmapped, exact position/triangle multisets and render fields; descriptor absent from every LOD entry'}


def verify(candidate: Path) -> int:
    loose_proof = P.read(P.ROOT / 'native-loose-archive-pins.json')
    if [P.pin(Path(p['path'])) for p in loose_proof] != loose_proof:
        raise ValueError('Loose native archive input changed')
    preflight = P.load('verify_preflight', 'slim-archive-preflight.py')
    native = P.load('verify_native', 'walker-next-native.py').NativeSlim(P.GAME)
    native.chunk = functools.lru_cache(maxsize=32)(native.chunk)
    audio = P.load('verify_audio', 'rebase-player-audio.py')
    labels = P.load('verify_labels', 'rc-voice-labels.py')
    hashes = P.load('verify_hashes', '../dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d/sdk/tools/hd2_archive.py')
    index = P.read(P.ROOT / 'native-index-25480438.json')
    current = P.read(P.OLD / 'dist/hd2-update-2026-10-29/native-index-v1/native-index.json')
    expected = P.read(P.ROOT / 'expected-resources.json')
    archives, resources, duplicate_keys, roundtrip_errors = [], {}, [], []
    file_records = {p.name: P.pin(p) for p in candidate.iterdir() if p.is_file()}
    for path in sorted(candidate.iterdir()):
        if not P.MAIN.fullmatch(path.name):
            continue
        paths = tuple(path.with_name(path.name + s) for s in P.SUFFIXES)
        check = preflight.inspect_path(path, tuple(p.stat().st_size for p in paths[1:]))
        archives.append({'file': path.name, **check})
        for row in P.directory(path):
            key = row[:2]
            if key in resources:
                duplicate_keys.append(f'{key[0]:016x}.{key[1]:016x}')
            resources[key] = P.Resource(path, row, paths)
            label = f'{key[0]:016x}.{key[1]:016x}'
            if list(map(P.sha, resources[key].parts())) != expected.get(label):
                roundtrip_errors.append(label)
    print('Archive readback', len(archives), len(resources), flush=True)
    geometry = geometry_provider_scan(resources, index)
    P.save('geometry-provider-scan.json', geometry)
    print('Geometry provider residency checked', geometry['unitsScanned'], 'UNITs', len(geometry['findings']), 'findings', flush=True)

    @functools.lru_cache(maxsize=256)
    def stock(key: tuple[int, int]) -> tuple[bytes, ...]:
        provider = index[f'{key[0]:016x}.{key[1]:016x}'][0]
        row = provider['row']
        return tuple(native.read(provider['archive'] + s, row[2+i], row[7+i]) for i, s in enumerate(P.SUFFIXES))

    @functools.lru_cache(maxsize=1024)
    def main_part(key: tuple[int, int]) -> bytes:
        return resources[key].parts()[0] if key in resources else stock(key)[0]

    name_types = defaultdict(set)
    for label in index:
        name, kind = label.split('.')
        name_types[name].add(kind)
    collisions, foreign, missing, native_exceptions = [], [], [], []
    for key, resource in resources.items():
        label = f'{key[0]:016x}.{key[1]:016x}'
        kind = f'{key[1]:016x}'
        collisions.append({'key': label, 'candidateFile': resource.path.name,
                           'classification': 'native-override' if label in index else 'private-addition',
                           'forgeFamilies': '|'.join(p['archive'] for p in index.get(label, [])),
                           'steam26251122Families': '|'.join(p['archive'] for p in current.get(label, [])),
                           'otherVanillaTypesForName': '|'.join(sorted(name_types[f'{key[0]:016x}'] - {kind}))})
        if label not in index and label in current:
            foreign.append(label)

    def dependency(key: tuple[int, int], owner: tuple[int, int], role: str) -> bytes | None:
        label = f'{key[0]:016x}.{key[1]:016x}'
        if key not in resources and label not in index:
            owner_label = f'{owner[0]:016x}.{owner[1]:016x}'
            if role == 'state-machine' and owner_label in index and struct.unpack_from('<Q', stock(owner)[0], 32)[0] == key[0]:
                native_exceptions.append({'owner': owner_label, 'key': label, 'role': role, 'evidence': 'Same unresolved state-machine reference in fresh Forge native UNIT'})
                return None
            missing.append({'owner': f'{owner[0]:016x}.{owner[1]:016x}', 'key': label, 'role': role})
            return None
        return main_part(key)

    surface_checked = set()

    def draw_materials(raw: bytes, key: tuple[int, int], native_only: bool = False) -> set[int]:
        used = set()
        group_id = struct.unpack_from('<Q', raw, 16)[0]
        if group_id:
            gg_key = group_id, 0xc4f0f4be7fb0c8d6
            gg = stock(gg_key)[0] if native_only else dependency(gg_key, key, 'geometry-group')
            if gg is None:
                return used
            n = struct.unpack_from('<I', gg, 8)[0]
            owners = [struct.unpack_from('<Q', gg, 24 + 16 * i)[0] for i in range(n)]
            if owners.count(key[0]) != 1:
                raise ValueError(f'GeometryGroup owner mismatch {key[0]:016x}')
            root = struct.unpack_from('<I', gg, 16 + 16 * n + 4 * owners.index(key[0]))[0]
            mesh_count = struct.unpack_from('<I', gg, root)[0]
            meshes = [root + struct.unpack_from('<I', gg, root + 4 + 4 * mesh_count + 4 * mi)[0] for mi in range(mesh_count)]
            for mesh in meshes:
                nm, mo = struct.unpack_from('<II', gg, mesh + 24)
                materials = struct.unpack_from(f'<{nm}I', gg, mesh + mo)
                ng, go = struct.unpack_from('<II', gg, mesh + 40)
                for gi in range(ng):
                    group = struct.unpack_from('<6I', gg, mesh + go + 24 * gi)
                    if group[4]:
                        used.add(materials[group[0]])
            return used
        for mesh in pointers(raw, 100):
            if struct.unpack_from('<I', raw, mesh + 36)[0] == 0:
                continue
            nm, mo = struct.unpack_from('<II', raw, mesh + 104)
            materials = struct.unpack_from(f'<{nm}I', raw, mesh + mo)
            ng, go = struct.unpack_from('<II', raw, mesh + 120)
            for gi in range(ng):
                group = struct.unpack_from('<6I', raw, mesh + go + 24 * gi)
                if group[4]:
                    used.add(materials[group[0]])
        return used

    def surface(mid: int, owner: tuple[int, int], parent: bool = False) -> None:
        key = mid, MATERIAL
        if (key, parent) in surface_checked:
            return
        surface_checked.add((key, parent))
        raw = dependency(key, owner, 'shader-parent' if parent else 'material')
        if raw is None or parent:
            return
        if len(raw) < 136:
            return  # Shader parents have a different schema.
        count = struct.unpack_from('<I', raw, 64)[0]
        if count > 10000 or 136 + 12 * count > len(raw):
            raise ValueError(f'Invalid material texture table {mid:016x}')
        for i in range(count):
            tid = struct.unpack_from('<Q', raw, 136 + 4 * count + 8 * i)[0]
            if tid:
                dependency((tid, TEXTURE), owner, 'texture')
        parent_id = struct.unpack_from('<Q', raw, 24)[0]
        if parent_id and parent_id != mid:
            surface(parent_id, owner, True)

    for key in resources:
        if key[1] != P.UNIT:
            continue
        raw = main_part(key)
        for field, kind, role in ((8, 0x18dead01056b72e9, 'bones'), (32, 0xa486d4045106165c, 'state-machine'), (16, 0xc4f0f4be7fb0c8d6, 'geometry-group')):
            value = struct.unpack_from('<Q', raw, field)[0]
            if value:
                dependency((value, kind), key, role)
        at = struct.unpack_from('<I', raw, 112)[0]
        if not at:
            continue
        count = struct.unpack_from('<I', raw, at)[0]
        thin = struct.unpack_from(f'<{count}I', raw, at + 4)
        values = struct.unpack_from(f'<{count}Q', raw, at + 4 + 4 * count)
        mapping = dict(zip(thin, values, strict=True))
        used = draw_materials(raw, key)
        for thin_id in used:
            if thin_id not in mapping:
                label = f'{key[0]:016x}.{key[1]:016x}'
                control = stock(key)[0] if label in index else None
                helper = ship_helper_evidence(resources[key].parts(), stock(key), thin_id) if control else None
                if helper:
                    native_exceptions.append({'owner': label, 'key': f'{thin_id:08x}', 'role': 'draw-thin-material', **helper})
                    continue
                if thin_id == 0x093fc934 and control and thin_id in draw_materials(control, key, True):
                    at = struct.unpack_from('<I', control, 112)[0]
                    n = struct.unpack_from('<I', control, at)[0]
                    native_thin = struct.unpack_from(f'<{n}I', control, at + 4)
                    if thin_id not in native_thin:
                        native_exceptions.append({'owner': label, 'key': f'{thin_id:08x}', 'role': 'draw-thin-material', 'evidence': 'Same drawn helper key is unmapped in fresh Forge native UNIT'})
                        continue
                missing.append({'owner': f'{key[0]:016x}', 'key': f'{thin_id:08x}', 'role': 'draw-thin-material'})
            else:
                surface(mapping[thin_id], key)
    print('Graphics dependencies checked', len(surface_checked), 'missing', len(missing), flush=True)
    codec_mismatches, wrappers, text_tables, bank_count = [], 0, 0, 0
    missing_custom_external = []
    inherited_missing_external = 0
    for key in resources:
        if key[1] == P.STREAM:
            parts = resources[key].parts()
            label = f'{key[0]:016x}.{key[1]:016x}'
            if label in index:
                if len(parts[0]) != 12 or parts[0][:8] != stock(key)[0][:8] or struct.unpack_from('<I', parts[0], 8)[0] != len(parts[1]):
                    raise ValueError(f'Stream wrapper native mismatch {label}')
                wrappers += 1
        elif key[1] == P.TEXT:
            actual, _ = labels.parse_strings(main_part(key))
            native_table, _ = labels.parse_strings(stock(key)[0])
            if not native_table.keys() <= actual.keys():
                raise ValueError('Missing Forge text keys')
            text_tables += 1
    bank_keys = {(int(label.split('.')[0], 16), P.BANK) for label in index if label.endswith(f'.{P.BANK:016x}')}
    bank_keys.update(k for k in resources if k[1] == P.BANK)
    for key in sorted(bank_keys):
        chunks = audio.chunks(main_part(key))
        media = audio.media(chunks)
        for mid, (_, sound) in audio.sounds(chunks.get(b'HIRC', bytes(4))).items():
            plugin, mode = struct.unpack_from('<IB', sound)
            acceptable = {0x40001: {0xffff}, 0x10001: {1, 0xfffe}}.get(plugin)
            if acceptable is None:
                continue
            candidates = [(hashes.resource_hash(prefix + str(mid)), P.STREAM) for prefix in ('content/audio/', 'content/audio/us/')]
            streams = [k for k in candidates if k in resources]
            if mode in (1, 2) and not streams and not any(f'{k[0]:016x}.{k[1]:016x}' in index for k in candidates):
                label = f'{key[0]:016x}.{key[1]:016x}'
                native_sounds = audio.sounds(audio.chunks(stock(key)[0]).get(b'HIRC', bytes(4))) if label in index else {}
                inherited = mid in native_sounds and native_sounds[mid][1][:18] == sound[:18]
                if inherited:
                    inherited_missing_external += 1
                else:
                    missing_custom_external.append({'bank': f'{key[0]:016x}', 'media': mid, 'mode': mode})
            wem = resources[streams[0]].parts()[1] if mode in (1, 2) and len(streams) == 1 else media.get(mid)
            if wem and P.fmt(wem) not in acceptable:
                codec_mismatches.append({'bank': f'{key[0]:016x}', 'media': mid, 'mode': mode, 'plugin': plugin, 'format': P.fmt(wem), 'streams': [f'{k[0]:016x}' for k in streams]})
        bank_count += 1
    print('Audio checked', bank_count, 'banks', wrappers, 'wrappers', len(codec_mismatches), 'mismatches', flush=True)
    P.save('archive-verification.json', archives)
    P.save('dependency-findings.json', missing)
    P.save('native-dependency-exceptions.json', native_exceptions)
    P.save('codec-mismatches.json', codec_mismatches)
    P.save('missing-custom-external-media.json', missing_custom_external)
    with (P.ROOT / 'name-type-collisions.csv').open('w', newline='', encoding='utf-8') as handle:
        writer = csv.DictWriter(handle, fieldnames=list(collisions[0]))
        writer.writeheader()
        writer.writerows(collisions)
    baseline_files = {p.name: P.pin(p) for p in P.BASE.iterdir() if p.is_file()}
    delta = []
    for name in sorted(baseline_files.keys() | file_records.keys()):
        before, after = baseline_files.get(name), file_records.get(name)
        state = 'added' if before is None else 'removed' if after is None else 'same' if before['sha256'] == after['sha256'] else 'changed'
        delta.append({'file': name, 'change': state, 'r19Bytes': before['size'] if before else 0, 'r45Bytes': after['size'] if after else 0,
                      'r19Sha256': before['sha256'] if before else '', 'r45Sha256': after['sha256'] if after else ''})
    with (P.ROOT / 'r19-file-diff.csv').open('w', newline='', encoding='utf-8') as handle:
        writer = csv.DictWriter(handle, fieldnames=list(delta[0]))
        writer.writeheader()
        writer.writerows(delta)
    base = P.baseline()
    resource_diff = []
    for key in sorted(base.keys() | resources.keys()):
        before = list(map(P.sha, base[key].parts())) if key in base else None
        after = list(map(P.sha, resources[key].parts())) if key in resources else None
        state = 'added' if before is None else 'removed' if after is None else 'same' if before == after else 'changed'
        resource_diff.append({'key': f'{key[0]:016x}.{key[1]:016x}', 'change': state, 'r19PayloadSha256': before, 'r45PayloadSha256': after})
    P.save('r19-resource-diff.json', resource_diff)
    P.save('candidate-files.json', list(file_records.values()))
    if [P.pin(Path(p['path'])) for p in loose_proof] != loose_proof:
        raise ValueError('Loose native archive input changed during verification')
    passed = all(a['passed'] for a in archives) and geometry['passed'] and not duplicate_keys and not roundtrip_errors and not codec_mismatches and not missing and not missing_custom_external and set(expected) == {f'{k[0]:016x}.{k[1]:016x}' for k in resources}
    report = {'passed': passed, 'candidate': str(candidate), 'gameBuild': '25480438', 'archives': len(archives), 'resources': len(resources),
              'files': len(file_records), 'bytes': sum(r['size'] for r in file_records.values()), 'duplicateKeys': duplicate_keys,
              'roundtripErrors': roundtrip_errors, 'keysNativeOnlyInSteamIndex': foreign,
              'archiveRulesPassed': all(a['passed'] for a in archives), 'audioCodecChecksPassed': not codec_mismatches,
              'dependencyClosurePassed': not missing, 'missingCustomExternalMedia': len(missing_custom_external),
              'geometryProviderClosurePassed': geometry['passed'], 'unresolvedGeometryProviders': len(geometry['findings']),
              'inheritedNativeMissingExternalMedia': inherited_missing_external,
              'nativeOverrides': sum(r['classification'] == 'native-override' for r in collisions),
              'privateAdditions': sum(r['classification'] == 'private-addition' for r in collisions),
              'sameNameOtherNativeTypes': sum(bool(r['otherVanillaTypesForName']) for r in collisions),
              'missingDependencies': len(missing), 'banksChecked': bank_count, 'nativeStreamWrappersChecked': wrappers,
              'nativeDependencyExceptions': len(native_exceptions),
              'codecMismatches': len(codec_mismatches), 'textTablesChecked': text_tables,
              'fileDelta': dict(Counter(r['change'] for r in delta)), 'resourceDelta': dict(Counter(r['change'] for r in resource_diff)),
              'runtimeTested': False, 'tools': [P.pin(Path(__file__)), P.pin(P.ROOT / 'empire_port.py')],
              'nativeEvidence': [P.pin(P.GAME / 'bundles.nxa'), P.pin(P.GAME / 'game/game.dll'), P.pin(P.GAME.parent / 'bin/helldivers2.exe'),
                                 P.pin(P.ROOT / 'native-index-25480438.json'), P.pin(P.ROOT / 'native-loose-archive-pins.json')]}
    P.save('verification-summary.json', report)
    print(json.dumps({k: v for k, v in report.items() if k not in ('nativeEvidence', 'tools', 'keysNativeOnlyInSteamIndex')}))
    return 0 if passed else 1


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--candidate', type=Path, default=P.ROOT / 'empire-forge-r45-v3')
    args = parser.parse_args()
    try:
        return verify(args.candidate.resolve())
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception('Candidate verification failed')
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
