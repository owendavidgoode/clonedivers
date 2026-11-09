#!/usr/bin/env python3
"""Audit a streamed Empire variant, run every candidate rule, and stage its catalog.

Run optimize-pack.ps1 first. Small textures re-encoded by the optimizer are
restored from the verified source before checking every payload independently.
All evidence and hash-named assets stay in the supplied workspace.
"""
from __future__ import annotations

import argparse
import csv
import json
import logging
import os
from pathlib import Path
import shutil
import struct
import sys
from uuid import NAMESPACE_URL, uuid5

import empire_port as P
import verify_candidate as V

TEXTURE = 0xcd4238c6a0c69e32
TAG = 'pack-2026.11.09-r45-v4-lighter-files'


def resources_at(path: Path) -> dict:
    companions = tuple(path.with_name(path.name + s) for s in P.SUFFIXES)
    return {row[:2]: P.Resource(path, row, companions) for row in P.directory(path)}


def install_metadata(source: Path, candidate: Path) -> None:
    for path in source.glob('*.txt'):
        shutil.copyfile(path, candidate / path.name)
    manifest = P.read(source / 'manifest.json')
    manifest.update(Guid=str(uuid5(NAMESPACE_URL, f'clonedivers/{candidate.name}')),
                    Name=f'EmpireDivers Forge {candidate.name.removeprefix("empire-forge-")} (25480438)',
                    Description='Lossless streamed textures. Private lab candidate. Gameplay acceptance pending.')
    (candidate / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')


def texture_streamed(before: tuple[bytes, ...], after: tuple[bytes, ...]) -> None:
    """Prove exact chain/tail bytes and independently rebuild the streaming prefix."""
    cpu, stream, gpu = before
    new_cpu, new_stream, tail = after
    if stream or new_stream != gpu or not tail or not gpu.endswith(tail):
        raise ValueError('Texture chain or resident tail changed')
    if cpu[192:196] != b'DDS ' or len(cpu) != len(new_cpu):
        raise ValueError('Unexpected texture CPU schema')
    height, width = struct.unpack_from('<II', cpu, 204)
    levels = struct.unpack_from('<I', cpu, 220)[0]
    fourcc = cpu[276:280]
    if fourcc == b'DX10':
        fmt = struct.unpack_from('<I', cpu, 320)[0]
        if fmt not in (28, 71, 72, 74, 75, 77, 78, 80, 81, 83, 84, 95, 96, 98, 99):
            raise ValueError(f'Unexpected streamed DXGI format {fmt}')
        block = 8 if fmt in (71, 72, 80, 81) else 16
    elif fourcc in (b'DXT1', b'ATI1', b'BC4U', b'DXT3', b'DXT5', b'ATI2', b'BC5U'):
        block = 8 if fourcc in (b'DXT1', b'ATI1', b'BC4U') else 16
    else:
        raise ValueError(f'Unexpected streamed FourCC {fourcc!r}')
    if not 1 <= levels <= 14:
        raise ValueError('Unexpected mip count')
    dimensions = [(max(1, width >> i), max(1, height >> i)) for i in range(levels)]
    sizes = [max(1, (w + 3) // 4) * max(1, (h + 3) // 4) * block for w, h in dimensions]
    if fourcc == b'DX10' and fmt == 28:
        sizes = [w * h * 4 for w, h in dimensions]
    floor = next(i for i, (w, h) in enumerate(dimensions) if max(w, h) <= 512)
    if floor == 0 or sum(sizes) != len(gpu) or sum(sizes[floor:]) != len(tail):
        raise ValueError('Unexpected streaming floor or mip lengths')
    expected = bytearray(cpu)
    # The optimizer chooses the residency hint from the source texture class.
    hint = struct.unpack_from('<I', new_cpu, 4)[0]
    if hint not in (1, 2):
        raise ValueError('Unexpected texture residency hint')
    struct.pack_into('<III', expected, 4, hint, floor, 0)
    expected[16:192] = bytes(176)
    offset = 0
    for i, ((w, h), size) in enumerate(zip(dimensions, sizes, strict=True)):
        offset += size
        struct.pack_into('<IHHI', expected, 16 + 12 * i, sum(sizes[i:]), w, h,
                         offset if i + 1 < levels else 0)
    # Streamed DDS headers clear DDSD_LINEARSIZE and its resident linear size.
    if not (fourcc == b'DX10' and fmt == 28):
        flags = struct.unpack_from('<I', cpu, 200)[0]
        struct.pack_into('<I', expected, 200, flags & ~0x80000)
        struct.pack_into('<I', expected, 212, 0)
    if bytes(expected) != new_cpu:
        differences = [(i, a, b) for i, (a, b) in enumerate(zip(expected, new_cpu, strict=True)) if a != b]
        raise ValueError(f'Unexpected texture header change {differences[:20]}')


def audit(source: Path, candidate: Path, evidence: Path, source_evidence: Path) -> dict:
    if not P.read(source_evidence / 'verification-summary.json')['passed']:
        raise ValueError('Full source has no passing verification')
    pins = P.read(source_evidence / 'candidate-files.json')
    if [P.pin(Path(p['path'])) for p in pins] != pins:
        raise ValueError('Verified Full source changed')
    original = P.read(source_evidence / 'expected-resources.json')
    history = evidence / 'lossless-summary.json'
    restored = P.read(history)['restoredTextures'] if history.exists() else []
    restored_keys = {r['key'] for r in restored}
    expected, records = {}, []
    bundles = sorted(p for p in source.iterdir() if P.MAIN.fullmatch(p.name))
    names = {p.name + suffix for p in bundles for suffix in P.SUFFIXES}
    present = {p.name for p in candidate.iterdir()}
    metadata = {'manifest.json'} | {p.name for p in source.glob('*.txt')}
    if not names <= present or present - names - metadata:
        raise ValueError('Candidate must contain exactly the source archive triplets')
    for path in bundles:
        target = candidate / path.name
        before, after = resources_at(path), resources_at(target)
        if before.keys() != after.keys():
            raise ValueError(f'Resource membership changed in {path.name}')
        replacements = {}
        for key, resource in before.items():
            label = f'{key[0]:016x}.{key[1]:016x}'
            old, new = resource.parts(), after[key].parts()
            if list(map(P.sha, old)) != original[label]:
                raise ValueError(f'Full resource changed {label}')
            state = 'unchanged'
            if label in restored_keys:
                if old != new:
                    raise ValueError(f'Previously restored texture changed {label}')
                state = 'restored-original'
            if old != new:
                if key[1] != TEXTURE:
                    raise ValueError(f'Non-texture changed {label}')
                if not new[1] and old[1:] != new[1:]:
                    # optimize also compresses small uncompressed surfaces. Keep them exact.
                    replacements[key] = resource
                    new = old
                    state = 'restored-original'
                    restored.append({'archive': path.name, 'key': label})
                else:
                    try:
                        texture_streamed(old, new)
                    except ValueError as error:
                        raise ValueError(f'{label}: {error}') from error
                    state = 'streamed'
            expected[label] = list(map(P.sha, new))
            records.append({'archive': path.name, 'key': label, 'state': state,
                            'beforeSha256': original[label], 'afterSha256': expected[label]})
        misaligned = any(key[1] == TEXTURE and resource.row[8] and resource.row[3] % 64
                         for key, resource in after.items())
        if replacements or misaligned:
            after.update(replacements)
            P.write_archive(target, path.read_bytes()[:72], after, stream_alignment=64)
        for key, resource in resources_at(target).items():
            if list(map(P.sha, resource.parts())) != expected[f'{key[0]:016x}.{key[1]:016x}']:
                raise ValueError('Rewritten archive readback mismatch')
    if set(expected) != set(original):
        raise ValueError('Source resource membership changed')
    if [P.pin(Path(p['path'])) for p in pins] != pins:
        raise ValueError('Full source changed during audit')
    for name in ('native-index-25480438.json', 'native-loose-archive-pins.json'):
        shutil.copyfile(source_evidence / name, evidence / name)
    shutil.copyfile(Path(P.__file__), evidence / 'empire_port.py')
    P.save('expected-resources.json', expected)
    P.save('lossless-resources.json', records)
    return {'archives': len(bundles), 'resources': len(records),
            'streamedTextures': sum(r['state'] == 'streamed' for r in records),
            'restoredTextures': restored, 'sourceFilesUnchanged': True,
            'sourceVerification': P.pin(source_evidence / 'verification-summary.json')}


def catalog(source_catalog: Path, output_catalog: Path, candidate: Path, evidence: Path, tag: str = TAG) -> dict:
    feed = P.read(source_catalog)
    full_rows = feed['pack']['files']
    old_by_hash = {r['sha256']: r for r in full_rows}
    old_by_name = {r['name']: r for r in full_rows}
    lighter, uploads = [], {}
    assets = evidence / 'assets'
    assets.mkdir(exist_ok=True)
    reused = 0
    for path in sorted(p for p in candidate.iterdir() if p.name in old_by_name):
        pin = P.pin(path)
        sha = pin['sha256']
        old = old_by_hash.get(sha)
        row = dict(old_by_name[path.name])
        url = old['url'] if old else f'https://github.com/owendavidgoode/clonedivers/releases/download/{tag}/{sha}'
        row.update(size=pin['size'], sha256=sha, url=url, textureProfiles=['lighter'])
        lighter.append(row)
        if old:
            reused += 1
        elif pin['size']:
            asset = assets / sha
            if not asset.exists():
                os.link(path, asset)
            if P.pin(asset)['sha256'] != sha:
                raise ValueError(f'Staged asset changed {asset}')
            uploads[sha] = {'path': str(asset), 'size': pin['size'], 'sha256': sha, 'tag': tag,
                            'name': sha, 'url': url, 'source': str(path)}
    feed['pack']['files'] = full_rows + lighter
    feed['pack']['textureProfiles'].append({'id': 'lighter', 'name': 'Lighter textures',
                                           'description': 'Full texture detail, streamed as needed.'})
    feed['pack']['notes'] += ' Full or lossless streamed textures.'
    feed['pack']['statusNotes'] += ' Lighter textures are statically verified. Lighter gameplay awaits acceptance.'
    output_catalog.write_text(json.dumps(feed, indent=4) + '\n', encoding='utf-8', newline='\r\n')
    P.save('lighter-catalog-rows.json', lighter)
    rows = list(uploads.values())
    P.save('upload-plan.json', rows)
    with (evidence / 'upload-plan.csv').open('w', encoding='utf-8', newline='') as handle:
        writer = csv.DictWriter(handle, fieldnames=['path', 'size', 'sha256', 'tag', 'name', 'url', 'source'])
        writer.writeheader()
        writer.writerows(rows)
    return {'catalog': P.pin(output_catalog), 'lighterRows': len(lighter), 'reusedRows': reused,
            'newAssets': len(rows), 'uploadBytes': sum(r['size'] for r in rows), 'tag': tag}


def sizes(folder: Path) -> dict[str, int]:
    files = [p for p in folder.iterdir() if p.is_file() and '.patch_' in p.name]
    return {'gpu': sum(p.stat().st_size for p in files if p.name.endswith('.gpu_resources')),
            'stream': sum(p.stat().st_size for p in files if p.name.endswith('.stream')),
            'total': sum(p.stat().st_size for p in files)}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workspace', type=Path, required=True)
    parser.add_argument('--source-catalog', type=Path, required=True)
    parser.add_argument('--output-catalog', type=Path, required=True)
    parser.add_argument('--revision', choices=('r45-v4', 'r46'), default='r45-v4')
    args = parser.parse_args()
    workspace = args.workspace.resolve()
    source = workspace / f'empire-forge-{args.revision}'
    candidate = workspace / f'empire-forge-{args.revision}-lighter'
    evidence = workspace / f'lighter-{args.revision}'
    source_evidence = workspace / ('triage-r45/v4' if args.revision == 'r45-v4' else 'r46/verification')
    try:
        evidence.mkdir(exist_ok=True)
        P.ROOT = evidence
        proof = audit(source, candidate, evidence, source_evidence)
        P.save('lossless-summary.json', proof)
        install_metadata(source, candidate)
        if V.verify(candidate):
            raise ValueError('Full candidate verifier rejected Lighter')
        tag = f'pack-2026.11.09-{args.revision}-lighter-files'
        proof.update(catalog(args.source_catalog, args.output_catalog, candidate, evidence, tag))
        proof.update(before=sizes(source), after=sizes(candidate), runtimeTested=False)
        P.save('build-summary.json', proof)
        print(json.dumps(proof, indent=2))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception('Lighter preparation failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
