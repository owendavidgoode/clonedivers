#!/usr/bin/env python3
"""Assemble EmpireDivers and Covenant release inputs without installing or publishing.

Pinned visual sources, current repaired AT-ST, and audited r14 shared assets.
The owner's 1.7 release authorization explicitly waives in-game acceptance.
"""
import argparse
import importlib.util
import json
import logging
import os
import re
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('empire_visuals', ROOT / 'tools/build-vehicle-followup.py')
V = importlib.util.module_from_spec(spec)
spec.loader.exec_module(V)
SOURCES = (
    ('Stormtrooper AIO-2154-1-1A-1777443862.zip', 'ad49fffe49df18cd3201496219057138ba197efabd554a4493f0fb3816f7efeb',
     ('Meshes/B-01', 'Textures'), '2154', '52906'),
    ('Helldiverized Stormtrooper-10666-1-1-1777593104.zip', '0dcc043239649bed8fee9c051cb12ab2359b53bd21b13ee1fed4ee77eff0dc77',
     ('',), '10666', '54395'),
    ('Star Destroyer 3949 1.0.5*.zip', '130953af44cdc1f6598dc33faaaf46cef1a7a13f8eee53c6c2bcbbd1f798ffba',
     ('',), '3949', '58722'),
    ('LAAT Gunship over Pelican 1 - April Update 5778*.zip', '792620a1a2b4c2d5965d7d97838e386a17607f52dbf768acee4e46f6e1c01ec9',
     ('Gray Skin',), '5778', '62049'),
)


def build(candidate: Path, downloads: Path, output: Path, version: str) -> None:
    if output.exists():
        raise ValueError('Use a fresh release input directory')
    resources, receipts, header = {}, [], None
    for pattern, expected, folders, mod, file_id in SOURCES:
        paths = list(downloads.glob(pattern))
        if len(paths) != 1 or V.V.sha(paths[0].read_bytes()) != expected:
            raise ValueError(f'Missing or changed pinned input: {pattern}')
        with zipfile.ZipFile(paths[0]) as archive:
            selected = [name for name in archive.namelist() if re.search(r'\.patch_\d+$', name)
                        and str(Path(name).parent).replace('\\', '/') in tuple(f or '.' for f in folders)]
            if len(selected) != len(folders):
                raise ValueError('Unexpected archive bundle membership')
            for name in selected:
                blobs, bundle = V.V.read_zip_bundle(archive, name)
                header = blobs[0] if header is None else header
                for key, value in bundle.items():
                    if key[1] not in (V.UNIT, 0x18dead01056b72e9, 0xcd4238c6a0c69e32, 0xeac0b497876adedf):
                        raise ValueError(f'Unexpected nonvisual resource {key}')
                    if key in resources and resources[key].payloads != value.payloads:
                        raise ValueError(f'Conflicting Imperial source resources: {key}')
                    resources[key] = value
        receipts.append({'archive': paths[0].name, 'sha256': expected, 'selected': selected,
                         'source': f'https://www.nexusmods.com/helldivers2/mods/{mod}?tab=files&file_id={file_id}'})
    for (identity, kind), value in resources.items():
        if kind == V.UNIT and V.u32(value.payloads[0], 44) != 10800438:
            raise ValueError(f'Unexpected legacy unit format: {identity:016x}')
    files = V.V.write_bundle(output / 'empire/9ba626afa44a3aa3.patch_0', header, resources)
    feed = json.loads(candidate.read_text())
    pack = feed['pack']
    if pack['version'] != '2026.09.26-factions-preview1':
        raise ValueError('Expected the audited combined candidate')
    rows = json.loads((ROOT / 'dist/deploy-report.json').read_text(encoding='utf-8-sig'))
    shared_mods = {'Clone Blasters', 'Blue Overhaul', 'Custom Projectiles (blue bolts)',
                   'No Bullet Casings', 'PEW-PEW Republic', 'Attachment Remover (Custom Scopes Compendium)'}
    shared = {r['file'] for r in rows if r['mod'] in shared_mods}
    shared.update(f'9ba626afa44a3aa3.patch_{index}' for index in (220, 222, 223, 314))
    pack['options'] = [o for o in pack['options'] if o['id'] != 'atst']
    pack['options'].append({'id': 'empire', 'name': 'EmpireDivers', 'default': False,
                            'description': 'Imperial armor, Star Destroyers, gray LAAT and AT-ST exosuits.'})
    assets = output / 'assets'
    assets.mkdir()
    shared_files = []
    for entry in pack['files']:
        main = re.sub(r'\.(gpu_resources|stream)$', '', entry['name'])
        if entry.get('unlessOption') == 'atst':
            del entry['unlessOption']
        if entry.get('option') == 'atst':
            del entry['option']
            entry['modes'] = ['empire']
        elif entry.get('option') == 'covenant':
            entry['modes'] = ['clonedivers', 'commandos', 'empire']
        elif main in shared or entry.get('option') == 'droids' or entry.get('option') == 'aimpoints':
            entry['modes'] = list(dict.fromkeys(entry.get('modes', ['clonedivers', 'commandos']) + ['empire']))
            shared_files.append(entry['name'])
        elif 'modes' not in entry:
            entry['modes'] = ['clonedivers', 'commandos']
        if entry['url'].startswith('http://127.0.0.1:8770/'):
            src = candidate.parent / 'assets' / entry['sha256']
            if V.V.sha(src.read_bytes()) != entry['sha256']:
                raise ValueError('Candidate asset hash mismatch')
            target = assets / entry['sha256']
            if not target.exists():
                os.link(src, target)
            entry['url'] = asset_url(version, entry) if entry['size'] else ''
    for entry in files:
        src = output / 'empire' / entry['name']
        if not (assets / entry['sha256']).exists():
            os.link(src, assets / entry['sha256'])
        pack['files'].append({**entry, 'name': entry['name'].replace('.patch_0', '.patch_320'),
                              'modes': ['empire'], 'url': asset_url(version, entry) if entry['size'] else ''})
    pack['version'] = version
    pack['name'] = 'Clonedivers and EmpireDivers'
    pack['notes'] = 'EmpireDivers mode and optional Covenant squids. Update the launcher to 1.7.0 first.'
    pack['statusNotes'] = 'EmpireDivers and Covenant are experimental; released at owner request without a gameplay test. See 1.7.0 notes.'
    pack['totalSize'] = sum(f['size'] for f in pack['files'])
    (output / 'manifest.json').write_text(json.dumps(feed, indent=2) + '\n')
    report = {'sources': receipts, 'files': files, 'visualResources': len(resources),
              'sharedFiles': shared_files, 'runtimeTested': False, 'musicAdded': False,
              'releaseAuthorization': 'Owner: just assume it will work and ship 1.7',
              'scope': 'B-01 Brawny and Bloodhound Lean armor, Star Destroyers, gray LAAT, Patriot/Emancipator AT-ST; shared blasters and optional enemies. Native voices/opening in EmpireDivers.'}
    (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    logging.info('Built Empire mode with %d visual resources; in-game acceptance waived, not claimed', len(resources))


def asset_url(version: str, entry: dict) -> str:
    return f'https://github.com/owendavidgoode/clonedivers/releases/download/pack-{version}-files/{entry["sha256"]}'


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--candidate', type=Path, required=True)
    parser.add_argument('--downloads', type=Path, default=Path.home() / 'Downloads')
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--version', default='2026.09.26-r15')
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s')
    try:
        build(args.candidate, args.downloads, args.out, args.version)
        return 0
    except (OSError, ValueError, KeyError, zipfile.BadZipFile):
        logging.exception('Empire release preparation failed')
        return 1
    except KeyboardInterrupt:
        return 130


if __name__ == '__main__':
    sys.exit(main())
