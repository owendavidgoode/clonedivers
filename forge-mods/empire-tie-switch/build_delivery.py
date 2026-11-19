#!/usr/bin/env python3
"""Fold the sealed TIE closure into r46 and verify the complete lab appearance pack."""
from __future__ import annotations

import argparse
from collections import defaultdict
from collections.abc import Sequence
import json
import logging
from pathlib import Path
import shutil
import struct
import sys
import uuid

sys.dont_write_bytecode = True
ROOT = Path('D:/ClaudeWork/empire-forge')
sys.path.insert(0, str(ROOT))
import empire_port as P
import verify_candidate as V

BASE = ROOT / 'empire-forge-r46'
ADDON = ROOT / 'empire-tie-switch-candidate/data'
OUT = ROOT / 'empire-forge-r46-tie'
EVIDENCE = ROOT / 'empire-tie-switch-candidate/pass2'
PACKAGE = (0x2c26bc4c6592fa14, 0xad9c6d9ed1e5e77a)


def require(ok: bool, message: str) -> None:
    if not ok:
        raise ValueError(message)


def resources_at(folder: Path) -> dict:
    resources = {}
    for path in sorted(folder.iterdir()):
        if not P.MAIN.fullmatch(path.name):
            continue
        for row in P.directory(path):
            require(row[:2] not in resources, f'Duplicate resource in {folder}: {row[:2]}')
            resources[row[:2]] = P.Resource(path, row, tuple(path.with_name(path.name+s) for s in P.SUFFIXES))
    return resources


def build() -> None:
    require(not OUT.exists() and not EVIDENCE.exists(), 'Fresh delivery/evidence directories required')
    require(P.read(ROOT / 'r46/verification/verification-summary.json')['passed'], 'r46 verifier failed')
    # Pin the complete r46 input rather than trusting a directory's name.
    base_pins = P.read(ROOT / 'r46/verification/candidate-files.json')
    require([P.pin(Path(p['path'])) for p in base_pins] == base_pins, 'Verified r46 files changed')
    addon_pins = P.read(ROOT / 'empire-tie-switch-candidate/evidence/candidate-files.json')
    require([P.pin(Path(p['path'])) for p in addon_pins] == addon_pins, 'Verified addon files changed')
    base, addon = resources_at(BASE), resources_at(ADDON)
    expected = P.read(ROOT / 'r46/verification/expected-resources.json')
    addon_expected = P.read(ROOT / 'empire-tie-switch-candidate/evidence/expected-resources.json')
    for key, resource in addon.items():
        label = f'{key[0]:016x}.{key[1]:016x}'
        require(list(map(P.sha, resource.parts())) == addon_expected[label], f'Addon payload drift: {label}')
        if key in base and key != PACKAGE:
            require(expected[label] == addon_expected[label], f'Unexpected r46 replacement: {label}')
    original, extended = base[PACKAGE].parts()[0], addon[PACKAGE].parts()[0]
    count = struct.unpack_from('<I', original, 8)[0]
    require(extended[16:len(original)] == original[16:], 'r46 package entries changed or lost')
    require(struct.unpack_from('<I', extended, 8)[0] > count, 'Package closure not extended')
    resources = base | addon
    changed = {key[0] % 32 for key in addon if key not in base or key == PACKAGE}
    buckets = defaultdict(dict)
    for key, resource in resources.items():
        buckets[key[0] % 32][key] = resource
    OUT.mkdir()
    EVIDENCE.mkdir()
    for path in BASE.iterdir():
        if not path.is_file() or path.name == 'manifest.json':
            continue
        main = path.name.removesuffix('.stream').removesuffix('.gpu_resources')
        if P.MAIN.fullmatch(main) and int(main.split('_')[1])-1000 in changed:
            continue
        # Copy unchanged files; the lab's candidate must not share mutable hardlinks.
        shutil.copyfile(path, OUT / path.name)
    with (BASE / '9ba626afa44a3aa3.patch_1000').open('rb') as source:
        header = source.read(72)
    for bucket in sorted(changed):
        P.write_archive(OUT / f'9ba626afa44a3aa3.patch_{1000+bucket}', header, buckets[bucket])
    manifest = P.read(BASE / 'manifest.json')
    manifest['Guid'] = str(uuid.uuid5(uuid.NAMESPACE_URL, 'clonedivers/empire-forge/r46-tie/pass2/2026-11-09'))
    manifest['Name'] = 'EmpireDivers Forge r46 + per-attack TIE closure (lab)'
    manifest['Description'] = 'Complete r46 appearance candidate plus private Fighter/Bomber closures. Requires empire-tie-switch 0.2.0 for per-attack selection. Runtime unverified.'
    (OUT / 'manifest.json').write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
    for name in ('empire_port.py', 'verify_candidate.py', 'native-index-25480438.json', 'native-loose-archive-pins.json'):
        shutil.copyfile(ROOT / name, EVIDENCE / name)
    expected.update(addon_expected)
    (EVIDENCE / 'expected-resources.json').write_text(json.dumps(expected, indent=2)+'\n', encoding='utf-8')
    old_root = P.ROOT
    try:
        P.ROOT = EVIDENCE
        require(V.verify(OUT) == 0, 'Full candidate verifier failed')
    finally:
        P.ROOT = old_root
    require([P.pin(Path(p['path'])) for p in base_pins] == base_pins, 'r46 inputs changed during build')
    receipt = {'candidate': str(OUT), 'guid': manifest['Guid'], 'base': str(BASE),
               'resources': len(resources), 'addedResources': len(set(addon)-set(base)),
               'replacedResources': [f'{PACKAGE[0]:016x}.{PACKAGE[1]:016x}'],
               'r46PackageEntriesPreserved': count, 'rebuiltBuckets': sorted(changed),
               'verificationPassed': True, 'runtimeTested': False,
               'tool': P.pin(Path(__file__)), 'basePins': base_pins, 'addonPins': addon_pins}
    (EVIDENCE / 'delivery-build.json').write_text(json.dumps(receipt, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in receipt.items() if k not in ('basePins', 'addonPins')}))


def main(argv: Sequence[str] | None = None) -> int:
    argparse.ArgumentParser(description=__doc__).parse_args(argv)
    logging.basicConfig(level=logging.INFO)
    try:
        build()
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError):
        logging.exception('TIE delivery refused')
        return 1


if __name__ == '__main__':
    sys.exit(main())
