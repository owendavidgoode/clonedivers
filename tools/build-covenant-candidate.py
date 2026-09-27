#!/usr/bin/env python3
"""Build an optional Covenant conversion on r14, with current-game sound routing.

Uses the author's manifest order (textures before models), rebases five banks,
and makes the existing Watcher probe audio mutually exclusive with Covenant.
Creates only a private candidate feed. Never installs or publishes.
"""

import argparse
import copy
import importlib.util
import json
import logging
import os
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def module(name: str, filename: str):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools' / filename)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


R = module('covenant_audio', 'rebase-player-audio.py')
A = module('atst_hash', 'build-atst-candidate.py')
V = R.V
SOURCE_SHA = 'fe3e2727ed13dea3910c23270bdefa8e9ccb2e7ba52885aeb9e05c46ae8569da'


def build(source: Path, archive: Path, current: Path, baseline: Path, output: Path, atst: Path | None = None) -> None:
    source = source.resolve()
    if output.exists():
        raise ValueError('Choose a fresh output directory')
    if V.sha(archive.read_bytes()) != SOURCE_SHA:
        raise ValueError('Covenant archive differs from Nexus 1.34 file 54935')
    manifest = json.loads((source / 'manifest.json').read_text(encoding='utf-8-sig'))
    resources, provenance, overrides = {}, [], []
    header = None
    for option in manifest['Options']:
        for directory in option['Include']:
            folder = (source / directory).resolve()
            if not folder.is_relative_to(source.resolve()):
                raise ValueError('Source manifest escapes its directory')
            paths = sorted(folder.glob('*.patch_*'))
            mains = [p for p in paths if not p.name.endswith(('.stream', '.gpu_resources'))]
            if not mains:
                raise ValueError(f'Missing source bundle: {directory}')
            for path in mains:
                blobs = tuple(Path(str(path) + suffix).read_bytes()
                              if Path(str(path) + suffix).exists() else b'' for suffix in V.SUFFIXES)
                header = blobs[0] if header is None else header
                bundle = V.read_bundle(blobs, str(path))
                provenance.append({'bundle': path.relative_to(source).as_posix(),
                                   'sha256': [V.sha(blob) for blob in blobs]})
                for key, value in bundle.items():
                    if key in resources:
                        overrides.append({'id': f'{key[0]:016x}', 'type': f'{key[1]:016x}',
                                          'winner': path.relative_to(source).as_posix(),
                                          'identical': resources[key].payloads == value.payloads})
                    resources[key] = value
    native = {}
    for path in current.rglob('*.main'):
        relative = path.relative_to(current).as_posix()
        for extension, kind in (('.wwise_bank.main', R.BANK), ('.wwise_dep.main', R.DEP)):
            if relative.endswith(extension):
                native[A.murmur64(relative.removesuffix(extension)), kind] = path.read_bytes()
    audio_reports = []
    for key, resource in list(resources.items()):
        if key[1] == R.BANK:
            main, external, report = R.rebase(resource.payloads[0], native[key])
            if external:
                raise ValueError('Unexpected stream migration requires explicit review')
            resources[key] = R.payload(resource, main)
            audio_reports.append({'id': f'{key[0]:016x}', **report})
        elif key[1] == R.DEP:
            resources[key] = R.payload(resource, native[key])
    if len(audio_reports) != 5:
        raise ValueError('Expected exactly five Covenant audio banks')
    files = V.write_bundle(output / 'bundle/9ba626afa44a3aa3.patch_0', header, resources)
    feed = json.loads(baseline.read_text(encoding='utf-8'))
    if feed['pack']['version'] != '2026.09.25-r14':
        raise ValueError('Reaudit conflicts when the baseline changes')
    feed.pop('app', None)
    pack = feed['pack']
    pack['version'] = '2026.09.26-covenant-preview1'
    pack['statusNotes'] = 'Private candidate: Covenant models and rebased audio require in-game acceptance.'
    pack['options'].append({'id': 'covenant', 'name': 'Covenant squids', 'default': False,
                            'description': 'Covenant models and voices for core Illuminate units. Newer units remain vanilla.'})
    watcher_gated = 0
    for entry in pack['files']:
        if entry['name'].split('.patch_')[-1].split('.')[0] == '314':
            if entry.get('option') or entry.get('unlessOption'):
                raise ValueError('Watcher gating changed; reaudit the conflict')
            entry['unlessOption'] = 'covenant'
            watcher_gated += 1
    if watcher_gated != 3:
        raise ValueError('Expected exactly one Watcher audio bundle')
    assets = output / 'assets'
    assets.mkdir()
    for item in files:
        path = output / 'bundle' / item['name']
        asset = assets / item['sha256']
        if not asset.exists():
            os.link(path, asset)
        pack['files'].append({**item, 'name': item['name'].replace('.patch_0', '.patch_318'),
                              'url': f'http://127.0.0.1:8770/assets/{item["sha256"]}', 'option': 'covenant'})
    if atst is not None:
        atst_report = json.loads((atst / 'report.json').read_text(encoding='utf-8'))
        pack['version'] = '2026.09.26-factions-preview1'
        pack['options'].append({'id': 'atst', 'name': 'AT-ST test', 'default': False,
                                'description': 'Experimental Patriot and Emancipator replacement. EmpireDivers feasibility test.'})
        gated = 0
        for entry in pack['files']:
            if entry['name'].split('.patch_')[-1].split('.')[0] in ('230', '231'):
                if entry.get('option') or entry.get('unlessOption'):
                    raise ValueError('AT-TE gating changed; reaudit the conflict')
                entry['unlessOption'] = 'atst'
                gated += 1
        if gated != 12:
            raise ValueError('Expected Full/Lighter variants of the two AT-TE bundles')
        for item in atst_report['files']:
            path = atst / item['name']
            if path.stat().st_size != item['size'] or V.sha(path.read_bytes()) != item['sha256']:
                raise ValueError('AT-ST bytes differ from the build receipt')
            asset = assets / item['sha256']
            if not asset.exists():
                os.link(path, asset)
            pack['files'].append({**item, 'name': item['name'].replace('.patch_0', '.patch_319'),
                                  'url': f'http://127.0.0.1:8770/assets/{item["sha256"]}', 'option': 'atst'})
        pack['statusNotes'] = 'Private candidate: AT-ST Patriot/Emancipator and Covenant need separate in-game acceptance tests.'
    pack['totalSize'] = sum(entry['size'] for entry in pack['files'])
    (output / 'manifest.json').write_text(json.dumps(feed, indent=2) + '\n', encoding='utf-8')
    report = {'source': 'https://www.nexusmods.com/helldivers2/mods/1670?tab=files&file_id=54935',
              'sourceSha256': SOURCE_SHA, 'sourceBundles': provenance, 'sourceOverrides': overrides,
              'resourceCount': len(resources), 'files': files, 'audio': audio_reports,
              'changedEmbeddedSamples': sum(r['changedEmbeddedSamples'] for r in audio_reports),
              'watcherProbeDisabledOnlyWithCovenant': True, 'musicAdded': False,
              'textureProfiles': 'Same source textures in both profiles; no new reduction claimed.',
              'runtimeTested': False, 'published': False}
    if atst is not None:
        report['atstBuildReport'] = str((atst / 'report.json').resolve())
    (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    logging.info('Built %d resources and five rebased banks; Covenant is off by default', len(resources))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('source', 'archive', 'current', 'baseline', 'out'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--atst', type=Path)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s')
    try:
        build(args.source, args.archive, args.current, args.baseline, args.out, args.atst)
        return 0
    except (OSError, ValueError, KeyError, struct.error):
        logging.exception('Covenant build failed')
        return 1
    except KeyboardInterrupt:
        return 130


if __name__ == '__main__':
    sys.exit(main())
