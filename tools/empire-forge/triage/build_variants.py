#!/usr/bin/env python3
"""Build four offline cape/text controls and run the unchanged r45 verifier in isolation."""
from __future__ import annotations

import argparse
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
OUT = ROOT / 'triage-r45'
MEDIUM = 0x3c33cf10a26cbb3e, P.UNIT
SHOCK = 0xea846ef460cc5ba0, P.UNIT
CAPE_GROUP = 0xc6a69260cc389bcd, 0xc4f0f4be7fb0c8d6


def label(key: tuple[int, int]) -> str:
    return f'{key[0]:016x}.{key[1]:016x}'


def build(variant: str) -> dict:
    resources = {}
    for path in sorted(SOURCE.iterdir()):
        if P.MAIN.fullmatch(path.name):
            for row in P.directory(path):
                key = row[:2]
                if key in resources:
                    raise ValueError('Duplicate input key')
                resources[key] = P.Resource(path, row, tuple(path.with_name(path.name + s) for s in P.SUFFIXES))
    expected = P.read(ROOT / 'expected-resources.json')
    before_files = P.read(ROOT / 'candidate-files.json')
    if [P.pin(Path(p['path'])) for p in before_files] != before_files:
        raise ValueError('Input candidate changed since verified receipt')
    descriptions = {
        'a-medium-r19': 'Replace only medium_cape UNIT with its complete r19 inline MAIN/STREAM/GPU payloads.',
        'b-native-capes': 'Remove both cape UNIT overrides so the native archive supplies medium and shock trooper capes.',
        'c-cape-geometry': 'Keep every r45 resource and add the exact Forge-native shared cape GeometryGroup, including GPU bytes.',
        'd-native-text': 'Remove all thirteen English STRINGS overrides so Forge-native text tables supply the values.',
    }
    folder = ROOT / f'empire-forge-r45-triage-{variant}'
    evidence = OUT / variant
    if folder.exists() or evidence.exists():
        raise ValueError(f'Fresh output required for {variant}')
    changes, replacements = [], {}
    if variant == 'a-medium-r19':
        replacements[MEDIUM] = P.baseline()[MEDIUM]
    elif variant == 'b-native-capes':
        replacements = {MEDIUM: None, SHOCK: None}
    elif variant == 'c-cape-geometry':
        if CAPE_GROUP in resources:
            raise ValueError('Expected a new cape GeometryGroup')
        index = P.read(ROOT / 'native-index-25480438.json')
        provider = index[label(CAPE_GROUP)][0]
        native = P.load('triage_variant_native', 'walker-next-native.py').NativeSlim(P.GAME)
        native.chunk = functools.lru_cache(maxsize=32)(native.chunk)
        row = provider['row']
        parts = tuple(native.read(provider['archive'] + s, row[2+i], row[7+i]) for i, s in enumerate(P.SUFFIXES))
        replacements[CAPE_GROUP] = P.Generated(parts)
    elif variant == 'd-native-text':
        replacements = {k: None for k in resources if k[1] == P.TEXT}
        if len(replacements) != 13:
            raise ValueError('Expected thirteen text overrides')
    else:
        raise ValueError(f'Unknown variant {variant}')
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
        changes.append({'key': label(key), 'before': before, 'after': after,
                        'source': str(resource.path) if isinstance(resource, P.Resource) else 'Forge-native GeometryGroup 18235e0c9ec0e636' if resource else 'native fallback'})
    folder.mkdir()
    evidence.mkdir()
    changed_buckets = {k[0] % 32 for k in replacements}
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
        shutil.copyfile(path, folder / path.name)
    for bucket in sorted(changed_buckets):
        P.write_archive(folder / f'9ba626afa44a3aa3.patch_{1000 + bucket}', header, buckets[bucket])
    manifest = P.read(SOURCE / 'manifest.json')
    manifest['Guid'] = str(uuid.uuid5(uuid.NAMESPACE_URL, 'clonedivers/empire-forge/triage-r45/2026-11-08/' + variant))
    manifest['Name'] = f'EmpireDivers r45 triage {variant} (25480438)'
    manifest['Description'] = descriptions[variant] + ' Offline bisection control. Runtime acceptance pending.'
    (folder / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    (evidence / 'expected-resources.json').write_text(json.dumps(expected, indent=2) + '\n', encoding='utf-8')
    for name in ('native-index-25480438.json', 'native-loose-archive-pins.json', 'empire_port.py'):
        shutil.copyfile(ROOT / name, evidence / name)
    receipt = {'variant': variant, 'folder': str(folder), 'guid': manifest['Guid'], 'description': descriptions[variant],
               'resourceCount': len(resources), 'changes': changes, 'rebuiltBuckets': sorted(changed_buckets),
               'sourceFilesUnchanged': [P.pin(Path(p['path'])) for p in before_files] == before_files,
               'tools': [P.pin(Path(__file__)), P.pin(ROOT / 'verify_candidate.py'), P.pin(ROOT / 'empire_port.py')],
               'runtimeTested': False}
    if not receipt['sourceFilesUnchanged']:
        raise ValueError('Source candidate mutated during build')
    (evidence / 'build.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    print('Built', variant, len(resources), 'resources', flush=True)
    # The verifier reads its native pins/expected payloads and writes receipts through
    # P.ROOT. Redirect that directory only, preserving all validation logic and rules.
    previous_root = P.ROOT
    try:
        P.ROOT = evidence
        code = V.verify(folder)
    finally:
        P.ROOT = previous_root
    if code:
        raise ValueError(f'Verification failed for {variant}')
    report = P.read(evidence / 'verification-summary.json')
    if not report['passed'] or report['roundtripErrors'] or report['missingDependencies']:
        raise ValueError('Expected full verifier acceptance')
    receipt['verificationPassed'] = True
    receipt['verification'] = P.pin(evidence / 'verification-summary.json')
    (evidence / 'build.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    return receipt


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('variants', nargs='*', default=['a-medium-r19', 'b-native-capes', 'c-cape-geometry', 'd-native-text'])
    args = parser.parse_args()
    try:
        for variant in args.variants:
            build(variant)
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception('Offline variant build failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
