#!/usr/bin/env python3
"""Port the shipped shared setup to Empire without changing asset bytes or Clones.

The r17 baseline is pinned. Explicit faction exclusions make omissions reviewable;
scope ownership is checked against the original 90-archive build receipt.
"""
import argparse
import copy
import hashlib
import json
import logging
import re
import sys
from pathlib import Path

ARCHIVE = '9ba626afa44a3aa3'
BASELINE_SHA = '20e929f1961e49d0c24be084c206a8389a6c223737165079221725c6c8168b57'
SCOPES_SHA = '926ea9ac48773dae1e39c29e29d3665262a6cf11a3289dbbbaf2b6d9265d688b'
SHARED = {
    **dict.fromkeys(range(8, 15), 'backpacks'),
    161: 'stratagem input sounds', 164: 'autocannon and LAAT audio',
    165: 'Eagle weapon audio', 166: 'CIS enemy audio',
    **dict.fromkeys(range(167, 177), 'orbital and sentry audio'),
    **dict.fromkeys(range(224, 227), 'Y-Wing Eagle'),
    227: 'walker audio', 228: 'LAAT/c transport', 229: 'LAAT/c transport',
    236: 'transport weapon cleanup', 237: 'transport thruster cleanup',
    **dict.fromkeys(range(239, 242), 'TX-130 FRV'),
    242: 'GNK hellbomb', 245: 'invisible capes',
    315: 'Falchion Bastion', 316: 'Supply FRV port',
}
EXCLUSIVE = {
    **dict.fromkeys(range(0, 8), 'Clone armor, Republic opening and title screen'),
    **dict.fromkeys(range(15, 20), 'Clone armor accessories and Republic decals'),
    159: 'Temuera officer voices', 160: 'Clone player voices',
    162: 'Clone officers', 163: 'Clone SEAF voices',
    **dict.fromkeys(range(178, 181), 'Clone SEAF models'),
    **dict.fromkeys(range(214, 220), 'Venator and Republic background ships'),
    221: 'Republic LAAT livery (Empire retains gray)',
    **dict.fromkeys(range(230, 236), 'AT-TE models and textures (Empire retains AT-ST)'),
    238: 'AT-TE-specific animation edits',
    243: 'Clone Eagle pilot voice', 244: 'Clone Pelican pilot voice',
    **dict.fromkeys(range(246, 249), 'Republic map presentation'),
    249: 'Venator video', 250: 'Venator soundtrack', 251: 'Commando voices',
    252: 'Clone armor and voice labels',
    **dict.fromkeys(range(253, 262), 'Commando armor and helmets'),
    312: 'mixed Clone armor roster',
}


def sha(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main_name(name: str) -> str:
    return re.sub(r'\.(stream|gpu_resources)$', '', name)


def build(baseline: Path, scopes_path: Path, output: Path, version: str) -> None:
    if output.exists():
        raise ValueError('Choose a fresh output directory')
    if sha(baseline) != BASELINE_SHA or sha(scopes_path) != SCOPES_SHA:
        raise ValueError('Source changed: re-audit the mode inventory')
    original = json.loads(baseline.read_text(encoding='utf-8'))
    candidate = copy.deepcopy(original)
    scopes = json.loads(scopes_path.read_text(encoding='utf-8'))
    if scopes['archives'] != 90 or scopes['resource_count'] != 288:
        raise ValueError('Incomplete scope setup')
    scope_names = set()
    for file in scopes['files']:
        name = file['name'].replace(f'{ARCHIVE}.patch_0', f'{ARCHIVE}.patch_313')
        matches = [f for f in original['pack']['files'] if f['name'] == name]
        if len(matches) != 1 or any(matches[0][key] != file[key] for key in ('size', 'sha256')):
            raise ValueError(f'Scope source mismatch: {name}')
        scope_names.add(main_name(name))
    promoted, excluded, seen = [], {}, set()
    for file in candidate['pack']['files']:
        modes = file.get('modes', [])
        if 'empire' in modes or not ({'clonedivers', 'commandos'} & set(modes)):
            continue
        name = main_name(file['name'])
        if name in scope_names:
            category = 'complete scope/ADS setup'
        else:
            match = re.fullmatch(ARCHIVE + r'\.patch_(\d+)', name)
            if not match:
                raise ValueError(f'Unclassified archive: {name}')
            index = int(match[1])
            if index in EXCLUSIVE:
                excluded[name] = EXCLUSIVE[index]
                continue
            if index not in SHARED:
                raise ValueError(f'Unclassified Clone-only bundle: {name}')
            category = SHARED[index]
            seen.add(index)
        file['modes'].append('empire')
        promoted.append({'name': file['name'], 'sha256': file['sha256'], 'size': file['size'],
                         'profiles': file.get('textureProfiles'), 'category': category})
    if seen != SHARED.keys():
        raise ValueError(f'Shared bundle missing: {sorted(SHARED.keys() - seen)}')
    if {main_name(f['name']) for f in promoted if f['category'] == 'complete scope/ADS setup'} != scope_names:
        raise ValueError('Partial scope promotion')
    pack = candidate['pack']
    pack['version'] = version
    pack['notes'] = 'EmpireDivers now includes the complete Clonedivers weapon/ADS setup, shared backpacks, stratagem and sentry sounds, transports, tanks, GNK hellbomb and cape changes. Clone voices stay exclusive to Clonedivers.'
    pack['statusNotes'] = 'Shared assets retain their shipped bytes. Mode and resource checks pass offline; gameplay remains unverified. Empire armor, AT-STs, ships and intro retained.'
    next(o for o in pack['options'] if o['id'] == 'empire')['description'] = 'Imperial armor, Star Destroyers, gray LAAT, AT-STs and battle intro with the shared Clonedivers weapon and vehicle setup.'
    output.mkdir(parents=True)
    (output / 'manifest.json').write_text(json.dumps(candidate, indent=2) + '\n', encoding='utf-8')
    report = {'baselineSha256': BASELINE_SHA, 'baselineVersion': original['pack']['version'],
              'version': version, 'scopeReceiptSha256': SCOPES_SHA, 'scopeArchives': len(scope_names),
              'scopeResources': scopes['resource_count'], 'newAssetBytes': 0,
              'promotedBundles': len({main_name(f['name']) for f in promoted}),
              'promotedFiles': promoted, 'cloneOnlyBundles': excluded,
              'cloneVoicesExclusive': True, 'clonediversUnchanged': True, 'gameplayTested': False}
    (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    logging.info('Promoted %s bundles (%s file variants), including all 90 scope archives; no new asset bytes', report['promotedBundles'], len(promoted))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', type=Path, required=True)
    parser.add_argument('--scopes', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--version', default='2026.09.27-r18')
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s')
    try:
        build(args.baseline, args.scopes, args.out, args.version)
        return 0
    except (OSError, ValueError, KeyError):
        logging.exception('Empire parity build failed')
        return 1
    except KeyboardInterrupt:
        return 130


if __name__ == '__main__':
    sys.exit(main())
