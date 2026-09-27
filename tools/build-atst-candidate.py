#!/usr/bin/env python3
"""Rebase GMrecreation's AT-ST geometry onto current Patriot/Emancipator metadata.

Creates an offline candidate only. Requires raw current walker units from Filediver.
Never installs, launches the game, or changes a published manifest.
"""

import argparse
import copy
import importlib.util
import json
import logging
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('vehicle_followup', ROOT / 'tools/build-vehicle-followup.py')
V = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(V)
SOURCE_SHA = 'a5832895f98356d8bd288b2b1935f7a06518da180b78c0e680645377193668fa'
BODY = 0x79e4b3d2da5e45e3
EMANCIPATOR = 0xc2d449ecf7facab1
RIGHT = 0xff9878576a4c543b
GEOMETRY_FIELDS = (48, 88, 92, 96, 100, 112)
# The old right autocannon uses obsolete mesh-node hashes. Match the eight
# corresponding LOD nodes explicitly, then verify their parents below.
RIGHT_ALIASES = dict(zip(
    (0x4661a3e6, 0x8ccef6f5, 0x51baf226, 0x0fa68f3c,
     0xb9020d72, 0xb939612b, 0x3f71f4ae, 0x0b74b585),
    ('g_combat_walker_autocannon_right_shadow_LOD3',
     'g_combat_walker_autocannon_right_shadow_LOD2',
     'g_combat_walker_autocannon_right_shadow_LOD1',
     'g_combat_walker_autocannon_right_shadow',
     'g_combat_walker_autocannon_right_LOD3',
     'g_combat_walker_autocannon_right_LOD2',
     'g_combat_walker_autocannon_right_LOD1',
     'g_combat_walker_autocannon_right'), strict=True))


def murmur64(text: str) -> int:
    data = text.encode()
    multiplier, mask = 0xc6a4a7935bd1e995, (1 << 64) - 1
    value = len(data) * multiplier & mask
    for offset in range(0, len(data) - len(data) % 8, 8):
        word = int.from_bytes(data[offset:offset + 8], 'little') * multiplier & mask
        word ^= word >> 47
        value = (value ^ (word * multiplier & mask)) * multiplier & mask
    if tail := data[len(data) - len(data) % 8:]:
        value = (value ^ int.from_bytes(tail, 'little')) * multiplier & mask
    value = (value ^ (value >> 47)) * multiplier & mask
    return value ^ (value >> 47)


def parents(main: bytes) -> tuple[int, ...]:
    at = V.u32(main, 52)
    count = V.u32(main, at)
    return tuple(struct.unpack_from('<H', main, at + 16 + 128 * count + 4 * i + 2)[0]
                 for i in range(count))


def graft(donor: bytes, native: bytes, identity: int) -> tuple[bytes, dict]:
    source, target = V.joints(donor), V.joints(native)
    if len(set(target)) != len(target):
        raise ValueError('Current joints are not unique')
    aliases = {key: murmur64(name) >> 32 for key, name in RIGHT_ALIASES.items()} if identity == RIGHT else {}
    mapping = {}
    for index, name in enumerate(source):
        resolved = aliases.get(name, name)
        if resolved not in target:
            raise ValueError(f'Unmapped joint {name:08x} in {identity:016x}')
        mapping[index] = target.index(resolved)
    # The newly inserted game_mesh is a grouping node. Every old joint must
    # still reach its old parent, allowing only newly inserted native nodes.
    old_parents, new_parents = parents(donor), parents(native)
    mapped = set(mapping.values())
    for index, old_parent in enumerate(old_parents):
        parent = new_parents[mapping[index]]
        visited = set()
        while parent not in mapped:
            if parent in visited:
                raise ValueError('Cycle in native joint hierarchy')
            visited.add(parent)
            parent = new_parents[parent]
        if parent != mapping[old_parent]:
            raise ValueError(f'Joint hierarchy changed at {index}')
    edited = bytearray(donor)
    # August 2025 enum values precede four added vertex formats. Restrict the
    # migration to this donor's exact 40-byte layout: float3, packed normal,
    # three half2 UVs, half4 weights, byte4 joint indices. GPU bytes stay intact.
    old_layout = [(0, 2), (1, 26), (4, 29), (4, 29), (4, 29), (7, 31), (6, 24)]
    new_formats = (2, 30, 33, 33, 33, 35, 28)
    layouts = V.pointers(donor, 92)
    for at in layouts:
        items = [(V.u32(donor, at + 8 + 20 * i), V.u32(donor, at + 12 + 20 * i))
                 for i in range(V.u32(donor, at + 328))]
        if items != old_layout or V.u32(donor, at + 356) != 40:
            raise ValueError('Unexpected legacy vertex layout')
        for index, kind in enumerate(new_formats):
            struct.pack_into('<I', edited, at + 12 + 20 * index, kind)
    references = 0
    for at in V.pointers(donor, 100):
        for field in (44, 48):
            struct.pack_into('<I', edited, at + field, mapping[V.u32(donor, at + field)])
            references += 1
        name = V.u32(donor, at + 40)
        struct.pack_into('<I', edited, at + 40, aliases.get(name, name))
    for at in V.pointers(donor, 88):
        count, _, offset, _ = struct.unpack_from('<IIII', donor, at)
        for index in range(count):
            field = at + offset + 4 * index
            struct.pack_into('<I', edited, field, mapping[V.u32(donor, field)])
            references += 1
    result = bytearray(native)
    result.extend(bytes(-len(result) % 16))
    base = len(result)
    result.extend(edited)
    for field in GEOMETRY_FIELDS:
        pointer = V.u32(donor, field)
        struct.pack_into('<I', result, field, base + pointer if pointer else 0)
    restored = bytearray(result[:len(native)])
    for field in GEOMETRY_FIELDS:
        restored[field:field + 4] = native[field:field + 4]
    if restored != native or V.joints(result) != target:
        raise ValueError('Native metadata was not preserved')
    return bytes(result), {
        'id': f'{identity:016x}', 'donorJoints': len(source), 'currentJoints': len(target),
        'remappedReferences': references, 'renamedMeshNodes': len(aliases),
        'currentMetadataPreserved': True, 'nativeSha256': V.V.sha(native),
        'donorSha256': V.V.sha(donor), 'candidateSha256': V.V.sha(result),
        'nativeHeaderFlags': V.u32(native, 44), 'donorHeaderFlags': V.u32(donor, 44),
        'vertexLayoutsMigrated': len(layouts),
    }


def build(source: Path, current: Path, output: Path) -> None:
    if output.exists():
        raise ValueError('Choose a fresh output directory')
    if V.V.sha(source.read_bytes()) != SOURCE_SHA:
        raise ValueError('AT-ST archive differs from Nexus file 29306')
    blobs, resources = V.read_zip(source)
    expected_units = {BODY, EMANCIPATOR, RIGHT, 0x08f6089289c83d22,
                      0x824b7e0c4c879eb5, 0xe5f64dcc3bfe9dd1}
    if {identity for identity, kind in resources if kind == V.UNIT} != expected_units:
        raise ValueError('Expected the two AT-ST bodies and four weapon hiders')
    native = {}
    for path in current.rglob('*.unit.main'):
        data = path.read_bytes()
        native[struct.unpack_from('<Q', data, 8)[0]] = data
    if BODY not in native:
        raise ValueError('Missing current combat_walker.unit.main')
    reports = []
    for (identity, kind), resource in list(resources.items()):
        if kind != V.UNIT:
            if kind not in (0xcd4238c6a0c69e32, 0xeac0b497876adedf):
                raise ValueError('Unexpected nonvisual resource')
            continue
        # The Emancipator body is a runtime variant of the shared walker rig.
        template = native[BODY if identity == EMANCIPATOR else identity]
        main, report = graft(resource.payloads[0], template, identity)
        updated = copy.deepcopy(resource)
        updated.payloads = (main, b'', resource.payloads[2])
        updated.row[7:10] = [len(main), 0, len(resource.payloads[2])]
        resources[identity, kind] = updated
        report['gpuUnchanged'] = True
        reports.append(report)
    files = V.V.write_bundle(output / '9ba626afa44a3aa3.patch_0', blobs[0], resources)
    report = {
        'source': 'https://www.nexusmods.com/helldivers2/mods/5745?tab=files&file_id=29306',
        'sourceSha256': SOURCE_SHA, 'units': reports, 'files': files,
        'scope': ['Patriot', 'Emancipator'], 'runtimeTested': False, 'published': False,
        'remaining': 'Startup, walking, both weapons, aiming, entry/exit, damage and multiplayer visual test. Lumberer and Bastion are not ported.',
    }
    (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    logging.info('Built %d repaired units; gameplay acceptance remains required', len(reports))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--current', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s')
    try:
        build(args.source, args.current, args.out)
        return 0
    except (OSError, ValueError, KeyError, struct.error):
        logging.exception('AT-ST build failed')
        return 1
    except KeyboardInterrupt:
        return 130


if __name__ == '__main__':
    sys.exit(main())
