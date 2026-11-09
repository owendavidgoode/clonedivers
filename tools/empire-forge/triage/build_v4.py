#!/usr/bin/env python3
"""Repair every unresolved r45 geometry provider and fully verify a fresh v4."""
from __future__ import annotations

from collections import defaultdict
import functools
import json
import logging
from pathlib import Path
import shutil
import struct
import sys
import uuid

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import empire_port as P
import verify_candidate as V

SOURCE = ROOT / 'empire-forge-r45-v3'
FOLDER = ROOT / 'empire-forge-r45-v4'
EVIDENCE = ROOT / 'triage-r45/v4'


def label(key: tuple[int, int]) -> str:
    return f'{key[0]:016x}.{key[1]:016x}'


def resources_at(folder: Path) -> dict:
    resources = {}
    for path in sorted(folder.iterdir()):
        if P.MAIN.fullmatch(path.name):
            for row in P.directory(path):
                if row[:2] in resources:
                    raise ValueError('Duplicate input resource')
                resources[row[:2]] = P.Resource(path, row, tuple(path.with_name(path.name + s) for s in P.SUFFIXES))
    return resources


def main() -> int:
    try:
        if FOLDER.exists() or EVIDENCE.exists():
            raise ValueError('Fresh v4 output folders required')
        source_pins = P.read(ROOT / 'candidate-files.json')
        if [P.pin(Path(p['path'])) for p in source_pins] != source_pins:
            raise ValueError('v3 input changed since original verification')
        resources = resources_at(SOURCE)
        index = P.read(ROOT / 'native-index-25480438.json')
        before_scan = V.geometry_provider_scan(resources, index)
        if before_scan['passed']:
            raise ValueError('Expected unresolved v3 geometry providers')
        baseline = P.baseline()
        native = P.load('v4_native', 'walker-next-native.py').NativeSlim(P.GAME)
        native.chunk = functools.lru_cache(maxsize=32)(native.chunk)
        replacements, repairs = {}, []
        for finding in before_scan['findings']:
            key = tuple(int(v, 16) for v in finding['owner'].split('.'))
            provider_key = tuple(int(v, 16) for v in finding['key'].split('.'))
            old = baseline.get(key)
            parts = old.parts() if old else None
            if key[1] == P.UNIT and parts and not struct.unpack_from('<Q', parts[0], 16)[0] and parts[2] and V.pointers(parts[0], 92):
                replacements[key] = old
                action = 'restore complete r19 inline UNIT'
                source = str(old.path)
            elif finding['key'] in index:
                provider = index[finding['key']][0]
                row = provider['row']
                parts = tuple(native.read(provider['archive'] + s, row[2+i], row[7+i]) for i, s in enumerate(P.SUFFIXES))
                replacements[provider_key] = P.Generated(parts)
                action = 'include exact native geometry provider'
                source = provider['archive']
            else:
                if finding['owner'] not in index:
                    raise ValueError(f'Cannot drop private resource with no native fallback {key}')
                replacements[key] = None
                action = 'drop override with no available geometry provider'
                source = 'native fallback'
            repairs.append({**finding, 'action': action, 'source': source})
        expected = P.read(ROOT / 'expected-resources.json')
        changes = []
        for key, resource in replacements.items():
            before = expected.get(label(key))
            if resource is None:
                del resources[key]
                del expected[label(key)]
                after = None
            else:
                resources[key] = resource
                after = list(map(P.sha, resource.parts()))
                expected[label(key)] = after
            changes.append({'key': label(key), 'before': before, 'after': after})
        after_scan = V.geometry_provider_scan(resources, index)
        if not after_scan['passed']:
            raise ValueError(f'Unresolved repaired geometry {after_scan["findings"]}')
        FOLDER.mkdir()
        EVIDENCE.mkdir()
        changed_buckets = {key[0] % 32 for key in replacements}
        buckets = defaultdict(dict)
        for key, resource in resources.items():
            buckets[key[0] % 32][key] = resource
        header = next(SOURCE.glob('*.patch_1000')).read_bytes()[:72]
        for path in SOURCE.iterdir():
            if path.name == 'manifest.json':
                continue
            main_name = path.name.split('.stream')[0].split('.gpu_resources')[0]
            if P.MAIN.fullmatch(main_name) and int(main_name.split('_')[1]) - 1000 in changed_buckets:
                continue
            shutil.copyfile(path, FOLDER / path.name)
        for bucket in sorted(changed_buckets):
            P.write_archive(FOLDER / f'9ba626afa44a3aa3.patch_{1000 + bucket}', header, buckets[bucket])
        manifest = P.read(SOURCE / 'manifest.json')
        manifest['Guid'] = str(uuid.uuid5(uuid.NAMESPACE_URL, 'clonedivers/empire-forge/r45-v4/2026-11-08'))
        manifest['Name'] = 'EmpireDivers Forge r45 v4 inline cape repair (25480438)'
        manifest['Description'] = 'Private offline candidate. Restores complete r19 inline medium and shock-trooper cape UNITs. Runtime and multiplayer acceptance pending.'
        (FOLDER / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
        for name, value in (('expected-resources.json', expected), ('geometry-scan-v3.json', before_scan),
                            ('geometry-scan-repaired.json', after_scan)):
            (EVIDENCE / name).write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')
        for name in ('native-index-25480438.json', 'native-loose-archive-pins.json', 'empire_port.py'):
            shutil.copyfile(ROOT / name, EVIDENCE / name)
        receipt = {'folder': str(FOLDER), 'guid': manifest['Guid'], 'resourceCount': len(resources),
                   'repairs': repairs, 'changes': changes, 'rebuiltBuckets': sorted(changed_buckets),
                   'runtimeTested': False, 'sourceFilesUnchanged': True,
                   'tools': [P.pin(Path(__file__)), P.pin(ROOT / 'verify_candidate.py'), P.pin(ROOT / 'empire_port.py')]}
        (EVIDENCE / 'build.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
        print('Built v4', len(resources), 'resources', flush=True)
        try:
            P.ROOT = EVIDENCE
            code = V.verify(FOLDER)
        finally:
            P.ROOT = ROOT
        if code:
            raise ValueError('Full v4 verification failed')
        if [P.pin(Path(p['path'])) for p in source_pins] != source_pins:
            raise ValueError('v3 changed during repair')
        receipt['verificationPassed'] = True
        receipt['verification'] = P.pin(EVIDENCE / 'verification-summary.json')
        (EVIDENCE / 'build.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception('Offline v4 build failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
