#!/usr/bin/env python3
"""Independently hash every selected asset and inspect all archive directories.

Uses the actual production selector export, not a second selector implementation.
No game, settings, feed or source asset is changed.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import struct
import subprocess
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
TASK = ROOT / 'dist/empire-complete-2026-10-08'
PATCH = re.compile(r'([0-9a-f]{16})\.patch_(\d+)(\.stream|\.gpu_resources)?$')
SUFFIXES = ('', '.stream', '.gpu_resources')
ROW = struct.Struct('<7Q6I')


def require(ok: Any, why: str) -> None:
    if not ok:
        raise ValueError(why)


def read(path: Path) -> Any:
    return json.loads(path.read_text(encoding='utf-8-sig'))


def pin(path: Path) -> dict[str, Any]:
    with path.open('rb') as handle:
        digest = hashlib.file_digest(handle, 'sha256').hexdigest()
    return {'path': str(path.resolve()), 'bytes': path.stat().st_size, 'sha256': digest}


def load_inputs(manifest: Path) -> Any:
    spec = importlib.util.spec_from_file_location('complete_pack_inputs', ROOT / 'tools/remaining-audio-inputs.py')
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module.Inputs(manifest)


def run(manifest: Path, selector: Path, output: Path) -> None:
    require(output.is_relative_to(TASK) and not output.exists(), 'Fresh scoped QA output required')
    manifest_pin, selector_pin = pin(manifest), pin(selector)
    selection, pack = read(selector), read(manifest)['pack']
    require(selection['passed'] and selection['manifestSha256'] == manifest_pin['sha256'] and
            selection['requestedStates'] == len(selection['selections']) == 96,
            'Actual production selector must bind exact manifest and all96 states')
    require(pack['totalSize'] == sum(row['size'] for row in pack['files']), 'Manifest totalSize exact')
    inputs = load_inputs(manifest)
    preflight_path = ROOT / 'tools/slim-archive-preflight.py'
    preflight_spec = importlib.util.spec_from_file_location('complete_pack_archive_preflight', preflight_path)
    require(preflight_spec is not None and preflight_spec.loader is not None, 'Archive preflight module available')
    preflight = importlib.util.module_from_spec(preflight_spec)
    sys.modules[preflight_spec.name] = preflight
    preflight_spec.loader.exec_module(preflight)
    inventory = subprocess.run(['rg', '--files', '--hidden', 'dist'], cwd=ROOT,
                               capture_output=True, text=True, check=True, timeout=60)
    for relative in inventory.stdout.splitlines():
        path = ROOT / relative
        if re.fullmatch('[0-9a-f]{64}', path.name):
            inputs.hints[path.name].append(path)
    verified: dict[str, dict[str, Any]] = {}
    relocated: dict[int, list[Path]] | None = None
    relocated_pins: dict[Path, dict[str, Any]] = {}
    empty_sha = hashlib.sha256(b'').hexdigest()

    def resolve(row: dict[str, Any], paired: Path | None = None) -> Path | None:
        nonlocal relocated
        digest = row['sha256']
        if not row['size']:
            require(digest == empty_sha and not row['url'], 'Empty companion identity/URL')
            return None
        if digest in verified:
            require(verified[digest]['bytes'] == row['size'], 'Unique hash has consistent size')
            return Path(verified[digest]['path'])
        if paired is not None and paired.is_file() and paired.stat().st_size == row['size']:
            actual = pin(paired)
            if actual['sha256'] == digest:
                verified[digest] = actual
                return paired
        try:
            path = inputs.source(row, fresh=True)
        except ValueError:
            if relocated is None:
                relocated = defaultdict(list)
                game = Path('C:/Program Files (x86)/Steam/steamapps/common/Helldivers 2')
                for folder in (game / 'data', game / 'mods_old', game / 'mods_off',
                               game / 'data/mods_old', game / 'data/mods_off'):
                    if folder.is_dir():
                        for path in folder.iterdir():
                            if path.is_file() and PATCH.fullmatch(path.name):
                                relocated[path.stat().st_size].append(path)
                for relative in inventory.stdout.splitlines():
                    path = ROOT / relative
                    if PATCH.fullmatch(path.name):
                        relocated[path.stat().st_size].append(path)
            found = None
            for path in relocated[row['size']]:
                if path not in relocated_pins:
                    relocated_pins[path] = pin(path)
                if relocated_pins[path]['sha256'] == digest:
                    verified[digest] = relocated_pins[path]
                    found = path
                    break
            require(found is not None, 'No matching immutable selected asset: ' + row['name'] + ' ' + digest)
            return found
        actual = inputs.pins.get(digest) or pin(path)
        require(actual['sha256'] == digest and actual['bytes'] == row['size'], 'Fresh selected asset bytes: ' + row['name'])
        verified[digest] = actual
        return path

    directories: dict[tuple[str, ...], dict[tuple[int, int], tuple[int, ...]]] = {}
    archive_checks, state_checks, footprint = [], [], Counter()
    range_faults: dict[tuple[str, ...], list[dict[str, Any]]] = {}
    rejected_path = ROOT / 'dist/empire-finish-2026-10-08/armor/female-candidate-v2/bundle/9ba626afa44a3aa3.patch_0'
    with rejected_path.open('rb') as handle:
        header = handle.read(72)
        magic, types, count = struct.unpack_from('<3I', header)
        require(magic == 0xf0000011 and count == 86, 'Exact rejected human footprint')
        handle.seek(72 + 32 * types)
        rejected = {ROW.unpack(handle.read(80))[:2] for _ in range(count)}
    unique_sets = selection['fileSets']
    for identity, files in unique_sets.items():
        indexed = {row['name']: row for row in files}
        require(len(indexed) == len(files), 'Unique physical selected filenames')
        mains = sorted((row for row in files if PATCH.fullmatch(row['name']) and not PATCH.fullmatch(row['name'])[3]),
                       key=lambda row: (PATCH.fullmatch(row['name'])[1], int(PATCH.fullmatch(row['name'])[2])))
        require(all(PATCH.fullmatch(row['name']) and
                    row['name'].removesuffix('.stream').removesuffix('.gpu_resources') in indexed
                    for row in files), 'Every selected companion has a MAIN archive')
        winners, providers, winning_triads = {}, defaultdict(list), {}
        for main in mains:
            parts = [indexed.get(main['name'] + suffix,
                     {'name': main['name'] + suffix, 'sha256': empty_sha, 'size': 0, 'url': ''})
                     for suffix in SUFFIXES]
            triad = tuple(row['sha256'] for row in parts)
            if triad not in directories:
                main_path = resolve(main)
                require(main_path is not None, 'Nonempty concrete MAIN archive')
                paths = [main_path] + [resolve(row, Path(str(main_path) + suffix)) for row, suffix in zip(parts[1:], SUFFIXES[1:], strict=True)]
                sizes = [row['size'] for row in parts]
                with main_path.open('rb') as handle:
                    header = handle.read(72)
                    magic, types, count = struct.unpack_from('<3I', header)
                    directory_end = 72 + 32 * types + count * 80
                    require(magic == 0xf0000011 and directory_end <= sizes[0], 'Complete SLIM header/directory')
                    handle.seek(72)
                    type_table = [struct.unpack('<IIQ4I', handle.read(32)) for _ in range(types)]
                    directory, ordinals, type_counts, faults = {}, [], Counter(), []
                    for number in range(count):
                        row = ROW.unpack(handle.read(80))
                        key = row[:2]
                        require(key not in directory, 'No duplicate typed key in single archive')
                        for part, size in enumerate(sizes):
                            offset, length = row[2 + part], row[7 + part]
                            if length and offset + length > size:
                                faults.append({'key': f'{key[0]:016x}.{key[1]:016x}', 'part': part,
                                    'offset': offset, 'length': length, 'selectedSize': size})
                            require(part != 0 or not length or offset >= directory_end, 'MAIN resource does not overlap directory')
                        directory[key] = row
                        ordinals.append(row[12])
                        type_counts[f'{row[1]:016x}'] += 1
                # Every mounted archive's metadata is parsed before overrides;
                # apply layout checks to all layers, including shadowed ones.
                # Companion ranges retain the winning-resource policy below.
                raw_directory = header + b''.join(struct.pack('<IIQ4I', *row) for row in type_table) + b''.join(ROW.pack(*row) for row in directory.values())
                engine_layout = preflight.validate_directory(raw_directory, sizes[0])
                declared_types = {f'{row[2]:016x}': row[3] for row in type_table}
                require(len(declared_types) == types and set(type_counts) <= set(declared_types),
                        'Every resource type has a unique declared type descriptor')
                type_count_differences = {key: {'declared': count, 'actual': type_counts.get(key, 0)}
                                         for key, count in declared_types.items() if count != type_counts.get(key, 0)}
                # Existing archives may use author-specific ordinal values. Preserve
                # them; bounds and unique typed identity, rather than a rewritten
                # ordinal convention, are the structural acceptance criteria.
                archive_checks.append({'identity': list(triad), 'files': [verified[row['sha256']] if row['size'] else {'bytes': 0, 'sha256': empty_sha} for row in parts],
                    'resources': count, 'typeCounts': dict(type_counts), 'resourceRangesComplete': not faults, 'rangeFaults': faults,
                    'engineFacingLayout': engine_layout,
                    'typeDescriptorCountDifferences': type_count_differences,
                    'uniqueTypedKeys': True, 'directoryNotOverlapped': True, 'sequentialOrdinals': ordinals == list(range(count))})
                directories[triad] = directory
                range_faults[triad] = faults
                footprint.update(type_counts)
            directory = directories[triad]
            for key in directory:
                winners[key] = main['sha256']
                winning_triads[key] = triad
                providers[key].append(main['sha256'])
        states = [row for row in selection['selections'] if row['fileSet'] == identity]
        require(states, 'Every file set is used')
        if any(row['effectiveOptions']['empire'] for row in states):
            require(not rejected.intersection(winners), 'Rejected human UNIT/MAT/TEX overrides remain absent from Empire')
        winning_faults = [{**fault, 'archiveIdentity': list(triad)}
                          for triad in set(winning_triads.values()) for fault in range_faults[triad]
                          if winning_triads.get(tuple(int(part, 16) for part in fault['key'].split('.'))) == triad]
        state_checks.append({'fileSet': identity, 'states': len(states), 'selectedFiles': len(files),
            'selectedArchives': len(mains), 'typedWinningResources': len(winners),
            'overlaidTypedResources': sum(len(rows) > 1 for rows in providers.values()),
            'winningPayloadRangeFaults': winning_faults,
            'winningTypeCounts': dict(Counter(f'{key[1]:016x}' for key in winners)),
            'winningResourceProviders': {f'{key[0]:016x}.{key[1]:016x}': value for key, value in sorted(winners.items())},
            'rejectedHumanKitAbsentForEmpire': True if any(row['effectiveOptions']['empire'] for row in states) else None})
        print(json.dumps({'fileSet': identity[:12], 'files': len(files), 'winningResources': len(winners)}), flush=True)
    require(set(unique_sets) == {row['fileSet'] for row in selection['selections']}, 'All production states covered')
    all_winning_faults = sum(len(row['winningPayloadRangeFaults']) for row in state_checks)
    result = {'passed': all_winning_faults == 0, 'meaningOfPassed': 'All selected assets freshly hashed and all raw directories inspected; surviving range faults fail QA. Gameplay behavior remains for user playtest.',
        'utc': datetime.now(timezone.utc).isoformat(), 'manifest': manifest_pin, 'productionSelector': selector_pin,
        'tool': pin(Path(__file__)), 'actualStates': 96, 'actualUniqueFileSets': len(unique_sets),
        'engineFacingArchivePreflight': pin(preflight_path),
        'distinctNonemptyAssetHashes': len(verified), 'freshlyHashedBytes': sum(row['bytes'] for row in verified.values()),
        'uniqueArchiveTriads': len(directories), 'archiveResourceRows': sum(row['resources'] for row in archive_checks),
        'winningPayloadRangeFaultsAcrossFileSets': all_winning_faults,
        'allArchivePayloadRangeFaults': sum(len(row) for row in range_faults.values()),
        'archiveTypeDescriptorCountDiscrepancies': sum(bool(row['typeDescriptorCountDifferences']) for row in archive_checks),
        'primaryArchiveFormat': pin(ROOT / 'dist/rc-upgrade/filediver-source/stingray/archive.go'),
        'archiveTypeCounts': dict(footprint), 'assets': list(verified.values()), 'archives': archive_checks, 'fileSets': state_checks,
        'rejectedHumanFootprint': pin(rejected_path), 'sourceAssetsUnmodified': True, 'gameOrSettingsChanged': False,
        'runtimeAccepted': False, 'limitations': ['Does not prove model visibility, collisions, animation or audio playback in the engine.',
            'Typed semantic checks are recorded separately by armor, audio, vehicle and ship reviewers.']}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'passed': result['passed'], 'report': pin(output), 'assets': len(verified),
        'archiveTriads': len(directories), 'winningRangeFaults': all_winning_faults}), flush=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('selector', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    run(args.manifest.resolve(), args.selector.resolve(), args.output.resolve())


if __name__ == '__main__':
    main()
