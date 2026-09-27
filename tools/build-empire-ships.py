#!/usr/bin/env python3
"""Rebase the detailed Star Destroyer donor onto current-game ship metadata.

Uses Kboy/Irastris's Nexus 52 geometry and textures; preserves current rig,
physics and event metadata. Migrates the donor's old vertex format numbers.
Creates an offline candidate and receipt, never installs or publishes.
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
spec = importlib.util.spec_from_file_location('ship_vehicle', ROOT / 'tools/build-vehicle-followup.py')
V = importlib.util.module_from_spec(spec)
spec.loader.exec_module(V)
SOURCE_SHA = '8c8a38ded81dd179ada41e5512708badd08e8eba8b22a03b1d80461067385194'
SHIPS = {0xbb3881db2f8d8164, 0x0c6567f9e37920e1, 0x7dcb6f44af36ada1}
FIELDS = (48, 88, 92, 96, 100, 112)
FORMATS = {2: 2, 26: 30, 29: 33, 31: 35, 24: 28}
# Donor copies of stock streamed textures: resolve from the current game instead.
NATIVE_TEXTURES = {0x09f9aaa5b7b5e872, 0x2145cc6c16fe306f, 0x21febe3fab02797b,
                   0x27afe0de8043d31b, 0x354d1e93ad48975c}


def graft(donor: bytes, native: bytes) -> tuple[bytes, dict]:
    old, new = V.joints(donor), V.joints(native)
    # This donor has exactly the same 139/65-joint rig as the current ships.
    # Do not silently approximate a future rig change.
    if old != new or V.u32(native, 44) != 10800438:
        raise ValueError('Current ship rig changed; re-audit before building')
    edited = bytearray(donor)
    layouts = V.pointers(donor, 92)
    for at in layouts:
        for i in range(V.u32(donor, at + 328)):
            field = at + 12 + 20 * i
            kind = V.u32(donor, field)
            if kind not in FORMATS:
                raise ValueError(f'Unreviewed vertex format: {kind}')
            struct.pack_into('<I', edited, field, FORMATS[kind])
    result = bytearray(native)
    result.extend(bytes(-len(result) % 16))
    base = len(result)
    result.extend(edited)
    for field in FIELDS:
        pointer = V.u32(donor, field)
        struct.pack_into('<I', result, field, base + pointer if pointer else 0)
    restored = bytearray(result[:len(native)])
    for field in FIELDS:
        restored[field:field+4] = native[field:field+4]
    if restored != native or V.joints(result) != new:
        raise ValueError('Native ship metadata was changed')
    return bytes(result), {'nativeSha256': V.V.sha(native), 'donorSha256': V.V.sha(donor),
                           'joints': len(new), 'layoutsMigrated': len(layouts),
                           'nativeMetadataPreserved': True, 'gpuUnchanged': True}


def build(source: Path, current: Path, output: Path) -> None:
    if output.exists():
        raise ValueError('Use a fresh output directory')
    if V.V.sha(source.read_bytes()) != SOURCE_SHA:
        raise ValueError('Detailed ship source differs from the audited archive')
    blobs, resources = V.read_zip(source)
    if {k[0] for k in resources if k[1] == V.UNIT} != SHIPS:
        raise ValueError('Ship source membership changed')
    reports = []
    for key, resource in list(resources.items()):
        if key[1] == 0xcd4238c6a0c69e32 and key[0] in NATIVE_TEXTURES:
            del resources[key]
            continue
        if key[1] == V.UNIT:
            native = (current / f'0x{key[0]:016x}.unit.main').read_bytes()
            main, receipt = graft(resource.payloads[0], native)
            resources[key] = V.with_data(resource, main, resource.payloads[2])
            reports.append({'id': f'{key[0]:016x}', **receipt})
        elif key[1] not in (0xcd4238c6a0c69e32, 0xeac0b497876adedf):
            raise ValueError(f'Unexpected nonvisual ship resource: {key}')
    files = V.V.write_bundle(output / '9ba626afa44a3aa3.patch_0', blobs[0], resources)
    report = {'source': 'https://www.nexusmods.com/helldivers2/mods/52',
              'authors': ['Kboy', 'Irastris'], 'sourceSha256': SOURCE_SHA,
              'units': reports, 'files': files, 'resources': len(resources),
              'stockTextureFallbacks': [f'{key:016x}' for key in sorted(NATIVE_TEXTURES)],
              'gameplayTested': False}
    (output / 'report.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    logging.info('Rebased %d detailed ship models', len(reports))


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
    except (OSError, ValueError, KeyError):
        logging.exception('Ship build failed')
        return 1
    except KeyboardInterrupt:
        return 130


if __name__ == '__main__':
    sys.exit(main())
