#!/usr/bin/env python3
"""Stage Falchion, a Supply FRV visual port, and restored spider-droid weapons.

Preserves gameplay physics; generated candidates require gameplay acceptance.
"""
import argparse
import copy
import importlib.util
import json
import re
import struct
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('vehicle_visuals', ROOT / 'tools/build-player-visuals.py')
V = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = V
spec.loader.exec_module(V)
UNIT = V.UNIT
SUPPLY = 0x9B2140378640432E
FRV = 0xCC21C7FFD3EBEFB9


def u32(blob: bytes, at: int) -> int:
    return struct.unpack_from('<I', blob, at)[0]


def pointers(blob: bytes, field: int) -> list[int]:
    at = u32(blob, field)
    if not at:
        return []
    count = u32(blob, at)
    if count > 10000:
        raise ValueError('Unexpected table count')
    return [at + u32(blob, at+4+4*i) for i in range(count)]


def joints(blob: bytes) -> tuple[int, ...]:
    at = u32(blob, 52)
    count = u32(blob, at)
    return struct.unpack_from('<'+'I'*count, blob, at+16+132*count)


def read_zip(path: Path, name: str | None = None) -> tuple:
    with zipfile.ZipFile(path) as archive:
        if name is None:
            mains = [name for name in archive.namelist() if re.search(r'\.patch_\d+$', name)]
            if len(mains) != 1:
                raise ValueError('Expected one source bundle')
            name = mains[0]
        return V.read_zip_bundle(archive, name)


def with_data(resource: object, main: bytes, gpu: bytes) -> object:
    result = copy.deepcopy(resource)
    result.payloads = (main, b'', gpu)
    result.row[7:10] = [len(main), 0, len(gpu)]
    return result


def build(stage: Path, output: Path) -> None:
    if output.exists():
        raise ValueError('Choose a fresh output directory')
    current = stage / 'current'
    falchion = stage / 'source/falchion-1.0.1.zip'
    blobs, resources = read_zip(falchion)
    if V.sha(falchion.read_bytes()) != '7ab635e3c83fee43c4f24213f42e866dc37073afb9f305a16903817e063ea7ec':
        raise ValueError('Falchion differs from downloaded v1.0.1')
    forbidden = {0x5f7203c8f280dab8, 0x2a690fd348fe9ac5}
    if any(key[1] in forbidden for key in resources):
        raise ValueError('Unexpected gameplay resource in visual donor')
    tank_files = V.write_bundle(output / 'falchion/9ba626afa44a3aa3.patch_0', blobs[0], resources)

    # The supply variant shares the FRV's animation/bone resource identities,
    # but its UNIT contains 201 joints rather than the donor's 194. Graft only
    # geometry tables and remap their joint references by stable bone hashes.
    blobs, resources = read_zip(ROOT / 'dist/mods/recovered - TX-130 FRV.zip', 'TX-130 Mesh/9ba626afa44a3aa3.patch_240')
    donor = resources[FRV, UNIT]
    main, _, gpu = donor.payloads
    native = (current / 'content/fac_helldivers/vehicles/frv_supply/frv_supply.unit.main').read_bytes()
    source_joints, target_joints = joints(main), joints(native)
    if len(target_joints) != 201 or len(set(target_joints)) != len(target_joints):
        raise ValueError('Supply rig changed since audit')
    mapping = {i: target_joints.index(key) for i, key in enumerate(source_joints) if key in target_joints}
    edited = bytearray(main)
    remaps = []
    for at in pointers(main, 100):
        for field in (44, 48):
            old = u32(main, at+field)
            if old not in mapping:
                raise ValueError('A mesh needs a donor-only bone')
            struct.pack_into('<I', edited, at+field, mapping[old])
            remaps.append((at+field, old, mapping[old]))
    for at in pointers(main, 88):
        count, _, offset, _ = struct.unpack_from('<IIII', main, at)
        for i in range(count):
            field = at+offset+4*i
            old = u32(main, field)
            if old not in mapping:
                raise ValueError('Skin requires a donor-only bone')
            struct.pack_into('<I', edited, field, mapping[old])
            remaps.append((field, old, mapping[old]))
    result = bytearray(native)
    result.extend(bytes(-len(result) % 16))
    base = len(result)
    result.extend(edited)
    fields = (48, 88, 92, 96, 100, 112)
    for field in fields:
        value = u32(main, field)
        struct.pack_into('<I', result, field, base+value if value else 0)
    # Restore redirected header fields: all original native bytes must remain.
    restored = bytearray(result[:len(native)])
    for field in fields:
        restored[field:field+4] = native[field:field+4]
    if bytes(restored) != native or joints(result) != target_joints:
        raise ValueError('Native rig or attachment metadata changed')
    if result[8:48] != native[8:48]:
        raise ValueError('Native bones/state-machine binding changed')
    supply_files = V.write_bundle(output / 'supply-frv/9ba626afa44a3aa3.patch_0', blobs[0],
        {(SUPPLY, UNIT): with_data(donor, bytes(result), gpu)})

    # Restore native weapon units hidden by the spider replacement. This is NOT
    # a weak-point marker patch: the main body and its changed rig are retained.
    blobs, resources = read_zip(ROOT / 'dist/mods/recovered - Automaton to CIS Overhaul.zip', 'War Strider Spider Droid/9ba626afa44a3aa3.patch_192')
    restored_weapons = {}
    types = {UNIT: 'unit', 0x18dead01056b72e9: 'bones', 0xa486d4045106165c: 'state_machine'}
    for identity in (0x8372619b2702d743, 0x9ab036439f74c115):
        for kind, label in types.items():
            path = current / f'0x{identity:016x}.{label}'
            main = Path(str(path)+'.main').read_bytes()
            gpu_path = Path(str(path)+'.gpu')
            gpu = gpu_path.read_bytes() if gpu_path.exists() else b''
            restored_weapons[identity, kind] = with_data(resources[identity, kind], main, gpu)
    weapon_files = V.write_bundle(output / 'spider-weapons/9ba626afa44a3aa3.patch_0', blobs[0], restored_weapons)
    report = {'falchionSource': 'https://www.nexusmods.com/helldivers2/mods/11329?tab=files&file_id=54986',
        'falchionSha256': V.sha(falchion.read_bytes()), 'falchionFiles': tank_files,
        'supplyFiles': supply_files, 'supplyNativeSha256': V.sha(native), 'supplyJointsPreserved': len(target_joints),
        'supplyRemappedReferences': len(remaps), 'supplyPhysicsStateMachineAndRack': 'No resource overrides; native data remains',
        'supplyRisk': 'Experimental geometry graft; donor bind matrices retained. Inspect deformation, seating, turret and supply access in game.',
        'spiderWeaponsFiles': weapon_files, 'spiderWeakPointMarkers': False,
        'spiderRisk': 'Native weapons may be obscured or displaced by the modified body rig; test before release.',
        'runtimeTested': False, 'published': False}
    (output / 'report.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print('Staged Falchion, Supply FRV geometry candidate and native spider weapons; gameplay acceptance required.')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    build(args.stage, args.out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
