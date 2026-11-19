#!/usr/bin/env python3
"""Replay patch staging offline and check the packaged driver and appearance closure."""
from __future__ import annotations

import argparse
from collections.abc import Sequence
import hashlib
import json
import logging
from pathlib import Path
import re
import shutil
import subprocess
import sys
import uuid
import zipfile

MOD = Path(__file__).resolve().parent
ROOT = Path('D:/ClaudeWork/empire-forge')
EVIDENCE = ROOT / 'empire-tie-switch-candidate/pass2'
PATCH = re.compile(r'([a-f0-9]{16})\.patch_(\d+)(\.stream|\.gpu_resources)?')
SESSION = Path('D:/ClaudeWork/harness-home/runs/20261109-145731-mods-hellforge/hellforge-session/session-mods.json')


def require(ok: bool, message: str) -> None:
    if not ok:
        raise ValueError(message)


def sha(path: Path) -> str:
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def read(path: Path) -> dict:
    return json.loads(path.read_text(encoding='utf-8-sig'))


def folders_for(record: dict, appearance: Path) -> list[Path]:
    if record['id'].startswith('pack.df8782b2-'):
        return [appearance]
    folder = Path(record['folder'])
    if (folder / 'hellforge-mod.json').exists():
        return [folder / 'data'] if (folder / 'data').is_dir() else []
    folders = [folder]
    manifest = folder / 'manifest.json'
    if manifest.exists():
        off, _, sub = record.get('choice', ';').partition(';')
        require(not sub, f'Unsupported recorded sub-option choice: {sub}')
        for option in read(manifest).get('Options', []):
            if option['Name'] in off.split(','):
                continue
            folders.extend(folder / name for name in option.get('Include', []) if name)
            children = option.get('SubOptions', [])
            if children:
                folders.extend(folder / name for name in children[0].get('Include', []) if name)
    return list(dict.fromkeys(folders))


def stage(folders: list[Path], destination: Path) -> list[dict]:
    """Replay ModStaging.Plan's per-family numeric renumbering into an empty sandbox."""
    destination.mkdir(parents=True)
    next_index = {}
    plan = []
    for folder in folders:
        groups = {}
        for path in folder.iterdir():
            match = PATCH.fullmatch(path.name)
            if path.is_file() and match:
                groups.setdefault((match[1], int(match[2])), []).append((path, match[3] or ''))
        for (family, _), files in sorted(groups.items()):
            index = next_index.get(family, 0)
            next_index[family] = index+1
            for path, suffix in sorted(files):
                target = destination / f'{family}.patch_{index}{suffix}'
                # Read-only test inputs: no fixture mutation uses these hardlinks.
                if path.is_relative_to(Path('D:/HellForge')):
                    shutil.copyfile(path, target)
                else:
                    target.hardlink_to(path)
                plan.append({'source': str(path), 'target': str(target)})
    return plan


def scan(folder: Path, passed: bool) -> dict:
    result = subprocess.run([str(MOD / 'build/tie_test.exe'), '--scan', str(folder)],
                            capture_output=True, text=True, check=False)
    require((result.returncode == 0) == passed, f'Unexpected scan result: {result.stdout} {result.stderr}')
    return {'directory': str(folder), 'exitCode': result.returncode, 'output': result.stdout.strip(),
            'stderr': result.stderr.strip()}


def check() -> None:
    package = Path('D:/ClaudeWork/forge-sdk/dist/empire-tie-switch-0.2.0.zip')
    with zipfile.ZipFile(package) as archive:
        files = archive.namelist()
        require(not any('/data/' in name for name in files), 'Driver ZIP still contains overriding data')
        manifest = json.loads(archive.read('empire-tie-switch/hellforge-mod.json'))
        require(manifest['version'] == '0.2.0', 'Driver version')
        for file in files:
            local = MOD / file.removeprefix('empire-tie-switch/')
            require(archive.read(file) == local.read_bytes(), f'Packaged source differs: {file}')
    with zipfile.ZipFile(package.with_name('empire-tie-switch-0.1.0.zip')) as archive:
        old_data = [name for name in archive.namelist() if '/data/' in name]
        require(len(old_data) == 3, 'Original ZIP did not carry closure triplet')
        for name in old_data:
            with archive.open(name) as source:
                digest = hashlib.file_digest(source, 'sha256').hexdigest()
            require(digest == sha(MOD / 'build/asset-input/data' / Path(name).name), 'Old ZIP data differs')
    work = MOD / 'build' / ('staging-pass2-' + uuid.uuid4().hex)
    old_base = ROOT / 'empire-forge-r45-v4'
    addon = MOD / 'build/asset-input/data'
    old_plan = stage([addon, old_base], work / 'old-order')
    old_result = scan(work / 'old-order', False)
    require('resource=2c26bc4c6592fa14 type=ad9c6d9ed1e5e77a part=0' in old_result['output'],
            'Original order did not reproduce Eagle package mismatch')
    repaired_plan = stage([old_base, addon], work / 'old-last')
    repaired_result = scan(work / 'old-last', True)
    records = read(SESSION)['mods']
    folders, skipped = [], []
    for record in records:
        if record['id'] == 'clonedivers.empire-tie-switch':
            continue  # New code-only driver contributes no staged files.
        for folder in folders_for(record, ROOT / 'empire-forge-r46-tie'):
            if not folder.is_dir():
                skipped.append({'id': record['id'], 'folder': str(folder), 'reason': 'recorded temporary input no longer exists'})
            else:
                folders.append(folder)
    new_plan = stage(folders, work / 'new-preset')
    new_result = scan(work / 'new-preset', True)
    require(read(EVIDENCE / 'verification-summary.json')['passed'], 'Candidate verifier did not pass')
    report = {'runtimeTested': False, 'officialStagingExecuted': False,
              'method': 'Offline replay of documented ModStaging.Plan numeric renumbering, then real C closure guard',
              'sessionEvidence': str(SESSION), 'package': str(package), 'sha256': sha(package),
              'packageFiles': files, 'codeOnly': True, 'oldZipDataPresentAndExact': True,
              'oldOrder': old_result, 'oldLast': repaired_result, 'newPreset': new_result,
              'skippedTemporaryInputs': skipped, 'plans': {'oldOrder': old_plan, 'oldLast': repaired_plan, 'newPreset': new_plan}}
    (EVIDENCE / 'staging-check.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k not in ('plans', 'packageFiles')}))


def main(argv: Sequence[str] | None = None) -> int:
    argparse.ArgumentParser(description=__doc__).parse_args(argv)
    logging.basicConfig(level=logging.INFO)
    try:
        check()
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, zipfile.BadZipFile):
        logging.exception('Delivery check failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
