#!/usr/bin/env python3
"""Stage Empire r46 CIS/native-Automaton choices without changing other resources.

The retained production selector renumbers patches, so classification compares
content hashes, not filenames. Resources also supplied with droids off belong
to the shared pack and are deliberately retained. All inputs are read-only.
"""
from __future__ import annotations

import argparse
from collections.abc import Sequence
import json
import logging
import os
from pathlib import Path
import shutil
import sys
from uuid import NAMESPACE_URL, uuid5

import empire_port as P
import verify_candidate as V

TAG = 'pack-2026.11.09-r46-droids-files'


def label(key: tuple[int, int]) -> str:
    return f'{key[0]:016x}.{key[1]:016x}'


def option_keys(selector: Path, registry_path: Path) -> tuple[set, dict]:
    document, registry = P.read(selector), P.read(registry_path)
    choices = [s for s in document['selections'] if s['mode'] == 'EmpireDivers'
               and s['profile'] == 'full' and not s['requestedOptions']['covenant']
               and not s['requestedOptions'].get('tieBomber', False)]
    on = document['fileSets'][next(s for s in choices if s['requestedOptions']['droids'])['fileSet']]
    off = document['fileSets'][next(s for s in choices if not s['requestedOptions']['droids'])['fileSet']]
    off_hashes = {r['sha256'] for r in off}
    pins, on_keys, off_keys = {}, set(), set()
    for rows, destination in ((on, on_keys), (off, off_keys)):
        for row in rows:
            if not P.MAIN.fullmatch(row['name']):
                continue
            # Common archives cannot introduce an option-only identity.
            if destination is on_keys and row['sha256'] in off_hashes:
                continue
            path = Path(registry[row['sha256']])
            if not path.is_relative_to(P.OLD):
                path = P.OLD / 'dist/hd2-update-2026-10-21/feed-vault' / row['sha256']
            P.verify_source(path, row)
            pins[str(path)] = P.pin(path)
            destination.update(r[:2] for r in P.directory(path))
    exclusive = on_keys - off_keys
    return exclusive, {'selector': P.pin(selector), 'registry': P.pin(registry_path),
                       'sources': list(pins.values()), 'optionOnly': sorted(map(label, exclusive)),
                       'sharedRetained': sorted(map(label, on_keys & off_keys))}


def resources(folder: Path) -> dict:
    result = {}
    for path in sorted(folder.iterdir()):
        if P.MAIN.fullmatch(path.name):
            companions = tuple(path.with_name(path.name + s) for s in P.SUFFIXES)
            for row in P.directory(path):
                if row[:2] in result:
                    raise ValueError(f'Duplicate resource {label(row[:2])}')
                result[row[:2]] = P.Resource(path, row, companions)
    return result


def build_variant(source: Path, output: Path, removed: set, identity: str) -> None:
    output.mkdir()
    for path in source.iterdir():
        if path.is_file() and path.name != 'manifest.json':
            os.link(path, output / path.name)
    for path in sorted(source.iterdir()):
        if not P.MAIN.fullmatch(path.name):
            continue
        rows = P.directory(path)
        if not any(r[:2] in removed for r in rows):
            continue
        companions = tuple(path.with_name(path.name + s) for s in P.SUFFIXES)
        keep = {r[:2]: P.Resource(path, r, companions) for r in rows if r[:2] not in removed}
        with path.open('rb') as handle:
            header = handle.read(72)
        # write_archive replaces links atomically; it never edits source inodes.
        P.write_archive(output / path.name, header, keep)
    manifest = P.read(source / 'manifest.json')
    manifest.update(Guid=str(uuid5(NAMESPACE_URL, identity)), Name=identity,
                    Description='Private r46 option candidate. Runtime acceptance pending.')
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')


def stage_catalog(source_catalog: Path, output_catalog: Path, variants: dict, assets: Path) -> list:
    feed = P.read(source_catalog)
    rows, uploads = [], {}
    for original in feed['pack']['files']:
        profile = original['textureProfiles'][0]
        on, off = variants[profile]
        path = off / original['name']
        pin = P.pin(path)
        if P.pin(on / original['name'])['sha256'] != original['sha256']:
            raise ValueError('CIS-on must exactly preserve the r46 catalog')
        if pin['sha256'] == original['sha256']:
            rows.append(original)
            continue
        rows.append({**original, 'option': 'droids'})
        sha = pin['sha256']
        url = f'https://github.com/owendavidgoode/clonedivers/releases/download/{TAG}/{sha}' if pin['size'] else ''
        rows.append({**original, 'unlessOption': 'droids', 'size': pin['size'], 'sha256': sha, 'url': url})
        if pin['size'] and sha not in uploads:
            asset = assets / sha
            if not asset.exists():
                os.link(path, asset)
            uploads[sha] = {'path': str(asset), 'name': sha, 'size': pin['size'], 'sha256': sha, 'url': url, 'tag': TAG}
    feed['pack'].update(files=rows, version='2026.11.09-r46-droids',
                        notes='Empire r46. CIS droids on by default; turn off for native Automaton models. Enemy audio and shared effects stay unchanged. Covenant disabled.',
                        statusNotes='CIS-on preserves the r46 archive payloads. Both option settings statically verified; visual and runtime acceptance pending.')
    output_catalog.write_text(json.dumps(feed, indent=4) + '\n', encoding='utf-8', newline='\r\n')
    return list(uploads.values())


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workspace', type=Path, required=True)
    parser.add_argument('--repository', type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        workspace, repository = args.workspace.resolve(), args.repository.resolve()
        candidate = workspace / 'empire-forge-r46-droids'
        removed, classification = option_keys(P.AREA / 'selection-v76/production96.json',
                                             P.AREA / 'selection-qa-v76-current/asset-registry.json')
        source = workspace / 'empire-forge-r46'
        expected = P.read(workspace / 'r46/verification/expected-resources.json')
        if not removed or not set(map(label, removed)) <= expected.keys():
            raise ValueError('Option classification does not match r46')
        candidate.mkdir()
        variants = {'full': (candidate / 'full-cis', candidate / 'native-automatons'),
                    'lighter': (candidate / 'lighter', candidate / 'lighter-native-automatons')}
        build_variant(source, variants['full'][0], set(), 'Empire r46 droids Full ON')
        evidence = candidate / 'evidence'
        evidence.mkdir()
        (evidence / 'classification.json').write_text(json.dumps(classification, indent=2) + '\n', encoding='utf-8')
        deltas, summaries = [], []
        for profile, (on, off) in variants.items():
            source = workspace / ('empire-forge-r46' if profile == 'full' else 'empire-forge-r46-lighter')
            source_evidence = workspace / ('r46/verification' if profile == 'full' else 'lighter-r46')
            if profile == 'lighter':
                build_variant(source, on, set(), 'Empire r46 droids Lighter ON')
            build_variant(source, off, removed, f'Empire r46 droids {profile} OFF')
            for enabled, folder in ((True, on), (False, off)):
                proof = evidence / f'{profile}-{str(enabled).lower()}'
                proof.mkdir()
                for name in ('native-index-25480438.json', 'native-loose-archive-pins.json'):
                    shutil.copyfile(source_evidence / name, proof / name)
                shutil.copyfile(Path(P.__file__), proof / 'empire_port.py')
                hashes = P.read(source_evidence / 'expected-resources.json')
                wanted = hashes if enabled else {k: v for k, v in hashes.items() if k not in set(map(label, removed))}
                P.ROOT = proof
                P.save('expected-resources.json', wanted)
                if V.verify(folder):
                    raise ValueError(f'Candidate rejected: {folder}')
                summaries.append({'profile': profile, 'droids': enabled, 'folder': str(folder), 'verification': str(proof / 'verification-summary.json')})
                if not enabled:
                    deltas.extend({'identity': k, 'profile': profile, 'action': 'remove-when-droids-off',
                                   'oldPayloadSha256': hashes[k], 'newPayloadSha256': None,
                                   'reason': 'CIS-only source resource; native Automaton fallback'} for k in sorted(set(hashes) - set(wanted)))
        assets = evidence / 'assets'
        assets.mkdir()
        uploads = {}
        for suffix in ('', '-lighter'):
            catalog = repository / f'Clonedivers/Resources/hellforge-empire-r46{suffix}.json'
            for row in stage_catalog(catalog, catalog, variants, assets):
                uploads[row['sha256']] = row
        (evidence / 'upload-plan.json').write_text(json.dumps(list(uploads.values()), indent=2) + '\n', encoding='utf-8')
        (evidence / 'build-summary.json').write_text(json.dumps(summaries, indent=2) + '\n', encoding='utf-8')
        delta_folder = workspace / 'deltas'
        delta_folder.mkdir(exist_ok=True)
        (delta_folder / 'droids.json').write_text(json.dumps({'base': 'empire-forge-r46', 'candidate': str(candidate),
            'classification': str(evidence / 'classification.json'), 'runtimeTested': False,
            'onPayloadsUnchanged': True, 'changes': deltas, 'requests': []}, indent=2) + '\n', encoding='utf-8')
        print(json.dumps({'removedPerProfile': len(removed), 'variants': summaries, 'newAssets': len(uploads)}))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception('Droid option preparation failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
