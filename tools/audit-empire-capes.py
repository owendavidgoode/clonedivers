#!/usr/bin/env python3
"""Audit cape resource precedence and drawable geometry in native launcher selections.

Reads archive directories directly, rather than trusting manifest labels. Resolve
main archives from local caches by their SHA-256; never changes game files.
"""

import argparse
import hashlib
import json
import logging
from pathlib import Path
import re
import struct
import sys

ENTRY = struct.Struct('<QQQQQQQIIIIII')
UNIT = 0xe0a48d0be9a7453f
CAPES = {0x3c33cf10a26cbb3e, 0xea846ef460cc5ba0}
PATCH = re.compile(r'^(.+)\.patch_(\d+)$')


def pointers(data: bytes, field: int) -> list[int]:
    offset = struct.unpack_from('<I', data, field)[0]
    if not offset:
        return []
    count = struct.unpack_from('<I', data, offset)[0]
    if count > 10000:
        raise ValueError('Unexpected model table count')
    return [offset + struct.unpack_from('<I', data, offset + 4 + i * 4)[0]
            for i in range(count)]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('selections', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--source', type=Path, action='append', required=True)
    args = parser.parse_args()
    selections = json.loads(args.selections.read_text())
    selections = [s for s in selections if s['mode'] == 'EmpireDivers']
    wanted = {f['Sha256']: f for s in selections for f in s['files'] if PATCH.match(f['Name'])}
    sizes = {f['Size'] for f in wanted.values()}
    sources: dict[str, Path] = {}
    for root in args.source:
        for path in root.rglob('*'):
            if path.is_file() and path.stat().st_size in sizes:
                with path.open('rb') as stream:
                    digest = hashlib.file_digest(stream, 'sha256').hexdigest()
                if digest in wanted:
                    sources.setdefault(digest, path)
    missing = wanted.keys() - sources.keys()
    if missing:
        raise ValueError(f'Missing {len(missing)} main archives: {sorted(missing)}')
    # Parse only the main archive; companion lengths come from the selection.
    tables = {}
    for digest, path in sources.items():
        data = path.read_bytes()
        magic, types, count = struct.unpack_from('<III', data)
        if magic != 0xf0000011 or 72 + types * 32 + count * 80 > len(data):
            raise ValueError(f'Invalid archive directory: {path}')
        rows = [ENTRY.unpack_from(data, 72 + types * 32 + i * 80) for i in range(count)]
        if len({r[:2] for r in rows}) != count:
            raise ValueError(f'Duplicate resources: {path}')
        capes = {}
        for row in rows:
            if row[0] in CAPES and row[1] == UNIT:
                capes[row[0]] = data[row[2]:row[2] + row[7]]
        tables[digest] = rows, capes
    report = []
    for selection in selections:
        files = {f['Name']: f for f in selection['files']}
        owners: dict[int, list[tuple[str, bytes]]] = {key: [] for key in CAPES}
        resource_count = 0
        for name in sorted(files, key=lambda n: (n.split('.patch_')[0], int(n.split('.patch_')[1].split('.')[0]))):
            match = PATCH.match(name)
            if not match:
                continue
            rows, capes = tables[files[name]['Sha256']]
            lengths = [files.get(name + suffix, {}).get('Size', 0) for suffix in ('', '.stream', '.gpu_resources')]
            for row in rows:
                if any(size and offset + size > length for offset, size, length in zip(row[2:5], row[7:10], lengths)):
                    raise ValueError(f'Truncated resource: {name}/{row[0]:016x}')
            resource_count += len(rows)
            for key, data in capes.items():
                owners[key].append((name, data))
        cape_report = []
        for key, versions in owners.items():
            if not versions or len({name.split('.patch_')[0] for name, _ in versions}) != 1:
                raise ValueError(f'Missing or ambiguous cross-archive cape {key:016x}')
            name, data = versions[-1]
            layouts = pointers(data, 92)
            meshes = pointers(data, 100)
            if not layouts or not meshes:
                raise ValueError(f'Unrecognized cape model: {name}')
            for at in layouts:
                if any(struct.unpack_from('<I', data, at + field)[0] for field in (392, 428)):
                    raise ValueError(f'Cape still has drawable indices: {name}')
            groups = 0
            for at in meshes:
                count, offset = struct.unpack_from('<II', data, at + 120)
                groups += count
                for i in range(count):
                    if struct.unpack_from('<I', data, at + offset + 24 * i + 16)[0]:
                        raise ValueError(f'Cape still has a drawable group: {name}')
            cape_report.append({'unit': f'{key:016x}', 'winner': name, 'overridden': [n for n, _ in versions[:-1]], 'layouts': len(layouts), 'groups': groups, 'drawableIndices': 0})
        report.append({k: v for k, v in selection.items() if k != 'files'} | {'resourcesChecked': resource_count, 'capes': cape_report})
    args.output.write_text(json.dumps({'selections': report, 'gameplayTested': False}, indent=2) + '\n')
    print(f'PASS {len(report)} Empire selections: archive bounds and both final cape models; zero drawable indices.')
    return 0


if __name__ == '__main__':
    logging.basicConfig(level=logging.INFO)
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        sys.exit(130)
    except Exception:
        logging.exception('Cape audit failed')
        sys.exit(1)
