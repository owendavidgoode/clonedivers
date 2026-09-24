#!/usr/bin/env python3
"""Extend the sealed local player preview with verified vehicle test assets."""
import argparse
import importlib.util
import json
import os
import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('player_feed', ROOT / 'tools/build-player-update.py')
P = importlib.util.module_from_spec(spec)
spec.loader.exec_module(P)


def build(previous: Path, stage: Path, output: Path) -> None:
    if output.exists():
        raise ValueError('Choose a fresh feed folder')
    sealed = P.load(previous.parent / 'test-readiness.json')
    if P.digest(previous / 'manifest.json') != sealed['manifestSha256']:
        raise ValueError('Previous candidate no longer matches its receipt')
    full, lighter = stage / 'full-ordered', stage / 'lighter-verified'
    variant = P.load(lighter / 'variant-receipt.json')
    if variant['baseInventorySha256'] != P.inventory_hash(full) or variant['inventorySha256'] != P.inventory_hash(lighter):
        raise ValueError('Vehicle texture variant receipt mismatch')
    if variant['settings']['maxSize'] != 0 or variant['settings']['dedup']:
        raise ValueError('Expected resolution-preserving texture streaming')
    report = P.load(previous / 'candidate.json')
    manifest = P.load(previous / 'manifest.json')
    manifest['pack']['version'] = '2026.09.23-player-preview3'
    for option in manifest['pack']['options']:
        if option['id'] == 'aimpoints':
            option['name'] = 'Walker weapons'
            option['description'] = 'Show original MTT cannons and spider-droid weapons. Does not add eye/vent markers or change hitboxes.'
    assets = output / 'assets'
    assets.mkdir(parents=True)
    for sha, item in report['newAssets'].items():
        source = previous / 'assets' / sha
        if source.stat().st_size != item['size'] or P.digest(source) != sha:
            raise ValueError('Existing candidate asset changed')
        try:
            os.link(source, assets / sha)
        except OSError:
            shutil.copyfile(source, assets / sha)
    added = []
    for profile, folder in (('full', full), ('lighter', lighter)):
        for path in sorted(folder.glob('*.patch_*')):
            match = re.fullmatch(r'9ba626afa44a3aa3\.patch_([012])(.*)', path.name)
            if not match:
                raise ValueError('Unexpected vehicle bundle')
            index = int(match[1])+315
            sha, size = P.digest(path), path.stat().st_size
            file = {'name': f'9ba626afa44a3aa3.patch_{index}{match[2]}',
                    'url': f'http://127.0.0.1:8767/assets/{sha}' if size else '',
                    'sha256': sha, 'size': size, 'textureProfiles': [profile]}
            if index == 317:
                file['option'] = 'aimpoints'
            if size and sha not in report['newAssets']:
                shutil.copyfile(path, assets / sha)
                report['newAssets'][sha] = {'source': str(path.resolve()), 'size': size}
            manifest['pack']['files'].append(file)
            added.append(file)
    report.update(vehicleReport=str((stage / 'visuals/report.json').resolve()),
        vehicleReportSha256=P.digest(stage / 'visuals/report.json'),
        previousManifestSha256=sealed['manifestSha256'], vehicleFiles=added,
        aimPointScope='MTT cannon and native spider weapon visibility; eye/vent markers unresolved',
        assetBytes=sum(item['size'] for item in report['newAssets'].values()), runtimeTested=False, published=False)
    P.write(output / 'manifest.json', manifest)
    P.write(output / 'candidate.json', report)
    print(f'Prepared {len(added)} vehicle/profile file entries; no public changes.')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--previous', type=Path, required=True)
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    build(args.previous, args.stage, args.out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
