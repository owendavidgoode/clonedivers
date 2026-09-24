#!/usr/bin/env python3
"""Stage compatible scope selections and four independent Commando helmets.

Repackages source resources without changing their main, GPU, or stream payloads.
Never writes game files or the public manifest.
"""

import argparse
import hashlib
import json
import logging
import re
import struct
import sys
import zipfile
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path

LOG = logging.getLogger(__name__)
ENTRY = struct.Struct('<QQQQQQQIIIIII')
UNIT = 0xe0a48d0be9a7453f
ARCHIVE = '9ba626afa44a3aa3'
SUFFIXES = ('', '.stream', '.gpu_resources')
DONOR_HELMETS = {'Boss': '48e63f84996be394', 'Fixer': '6c5bacf02c5aa0f0',
                 'Scorch': '697d6a57971da101', 'Sev': '651ccf16ab84901a'}
TARGETS = {'Sev': 'de118ad3932e0a23', 'Fixer': '7b23e3c0ab4cf618',
           'Scorch': 'c96cb2e72d7a0525', 'Boss': '781134771dd69fbe'}


@dataclass
class Resource:
    row: list[int]
    payloads: tuple[bytes, bytes, bytes]
    source: str


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_bundle(data: tuple[bytes, bytes, bytes], source: str) -> dict[tuple[int, int], Resource]:
    main = data[0]
    magic, types, count = struct.unpack_from('<III', main)
    if magic != 0xf0000011 or 72 + types * 32 + count * 80 > len(main):
        raise ValueError(f'Invalid archive: {source}')
    resources = {}
    for index in range(count):
        row = list(ENTRY.unpack_from(main, 72 + types * 32 + index * 80))
        key = tuple(row[:2])
        payloads = tuple(blob[offset:offset + size] for blob, offset, size in zip(data, row[2:5], row[7:10]))
        if key in resources or tuple(map(len, payloads)) != tuple(row[7:10]):
            raise ValueError(f'Duplicate or truncated resource: {source}/{key}')
        resources[key] = Resource(row, payloads, source)
    return resources


def read_zip_bundle(archive: zipfile.ZipFile, name: str) -> tuple[tuple[bytes, bytes, bytes], dict[tuple[int, int], Resource]]:
    names = set(archive.namelist())
    data = tuple(archive.read(name + suffix) if name + suffix in names else b'' for suffix in SUFFIXES)
    return data, read_bundle(data, name)


def write_bundle(path: Path, header: bytes, resources: dict[tuple[int, int], Resource]) -> list[dict]:
    types = sorted({key[1] for key in resources})
    keys = sorted(resources, key=lambda key: (types.index(key[1]), key[0]))
    head = bytearray(header[:72])
    struct.pack_into('<II', head, 4, len(types), len(keys))
    table = b''.join(struct.pack('<QQQII', 0, typ, sum(key[1] == typ for key in keys), 16, 64) for typ in types)
    start = 72 + len(table) + 80 * len(keys) + 8
    buffers = [bytearray(), bytearray(), bytearray()]
    directory = bytearray()
    for index, key in enumerate(keys):
        resource = resources[key]
        row = resource.row.copy()
        row[:2] = key
        row[2:7] = [start + len(buffers[0]), len(buffers[1]), len(buffers[2]), 0, 0]
        row[12] = index
        directory.extend(ENTRY.pack(*row))
        for buffer, payload in zip(buffers, resource.payloads):
            buffer.extend(payload + bytes(-len(payload) % 16))
    main = bytes(head) + table + directory + bytes(8) + buffers[0]
    main += bytes(max(0, 256 * len(keys) - len(main)))
    blobs = (bytes(main), bytes(buffers[1]), bytes(buffers[2]))
    check = read_bundle(blobs, str(path))
    if set(check) != set(resources) or any(check[key].payloads != resource.payloads for key, resource in resources.items()):
        raise ValueError('Serialized payload differs from source')
    path.parent.mkdir(parents=True, exist_ok=True)
    files = []
    for suffix, blob in zip(SUFFIXES, blobs):
        target = path.with_name(path.name + suffix)
        target.write_bytes(blob)
        files.append({'name': target.name, 'size': len(blob), 'sha256': sha(blob)})
    return files


def scopes(source: Path, output: Path) -> dict:
    with zipfile.ZipFile(source) as archive:
        # The author's installer manifest contains trailing commas.
        manifest = json.loads(re.sub(r',\s*([}\]])', r'\1', archive.read('manifest.json').decode('utf-8-sig')))
        folders = []
        choices = []
        for option in manifest['Options']:
            name = option['Name']
            children = option.get('SubOptions', [])
            if children:
                wanted = 'Simple floating reticle' if name == 'IRON SIGHT' else 'CLEAR' if name == 'HOLO SCOPE COLOR + SIZE' else 'DEFAULT CLEAR' if 'COLOR' in name else 'DEFAULT'
                matches = [child for child in children if child['Name'].casefold() == wanted.casefold()]
                if len(matches) != 1:
                    raise ValueError(f'Choose an explicit scope option for {name}')
                selected = matches[0]
            elif name.endswith('NO CASING') or name in {'ACCELERATOR SCOPE', 'Hot-Shot SCOPE'}:
                selected = option
            else:
                continue
            folders.extend(selected.get('Include', []))
            choices.append({'group': name, 'selection': selected['Name'], 'folders': selected.get('Include', [])})
        by_archive = defaultdict(dict)
        headers = {}
        overlaps = []
        for folder in dict.fromkeys(folders):
            names = sorted(name for name in archive.namelist() if str(Path(name).parent).replace('\\', '/') == folder and re.search(r'\.patch_\d+$', name))
            if not names:
                raise ValueError(f'Missing selected folder: {folder}')
            for name in names:
                data, resources = read_zip_bundle(archive, name)
                identity = Path(name).name.split('.')[0]
                headers[identity] = data[0][:72]
                for key, resource in resources.items():
                    prior = by_archive[identity].get(key)
                    if prior and prior.payloads != resource.payloads:
                        overlaps.append({'archive': identity, 'resource': f'{key[0]:016x}', 'type': f'{key[1]:016x}', 'before': prior.source, 'after': name})
                    by_archive[identity][key] = resource
        files = []
        for identity, resources in sorted(by_archive.items()):
            if resources:
                files.extend(write_bundle(output / f'{identity}.patch_0', headers[identity], resources))
        return {'source_sha256': sha(source.read_bytes()), 'choices': choices, 'overrides_in_selected_order': overlaps,
                'files': files, 'archives': len([r for r in by_archive.values() if r]),
                'resource_count': sum(map(len, by_archive.values())), 'runtime_tested': False,
                'resources': [{'archive': archive_id, 'resource': f'{key[0]:016x}', 'type': f'{key[1]:016x}', 'source': value.source} for archive_id, resources in by_archive.items() for key, value in resources.items()]}


def helmets(root: Path, output: Path) -> dict:
    starter = root / 'dist/rc-upgrade/starter-commandos/helmet-patch'
    starter_report = json.loads((starter / 'build-report.json').read_text())
    for file in starter_report['files']:
        if sha((starter / file['name']).read_bytes()) != file['sha256']:
            raise ValueError('Starter donor failed its recorded hash')
    data = tuple((starter / f'{ARCHIVE}.patch_0{suffix}').read_bytes() for suffix in SUFFIXES)
    resources = read_bundle(data, 'verified starter helmets')
    old_key = (int('bc20d0b4efff128c', 16), UNIT)
    resources[(int(TARGETS['Sev'], 16), UNIT)] = resources.pop(old_key)
    files = write_bundle(output / 'helmets' / f'{ARCHIVE}.patch_0', data[0], resources)
    kits = json.loads((starter.parent / 'all-kits.json').read_text())
    mapping = []
    for character, target in TARGETS.items():
        consumers = [kit for kit in kits if any(p['unit'] == target for p in kit['pieces'])]
        if len(consumers) != 1 or consumers[0]['kind'] != 1:
            raise ValueError(f'Helmet target is shared: {character}/{target}')
        mapping.append({'character': character, 'unit': target, 'kit': consumers[0]['id'], 'name': consumers[0]['name']})
    bodies = []
    source = root / 'dist/mods/Delta Squad AIO-552-1-1A-1777439542.zip'
    with zipfile.ZipFile(source) as archive:
        removed = set()
        for character, helmet in DONOR_HELMETS.items():
            names = [name for name in archive.namelist() if f'Meshes/{character}/' in name and re.search(r'\.patch_\d+$', name)]
            if len(names) != 1:
                raise ValueError(f'Expected one source mesh bundle: {character}')
            data, resources = read_zip_bundle(archive, names[0])
            key = (int(helmet, 16), UNIT)
            if key not in resources:
                raise ValueError(f'Expected original helmet absent: {character}')
            del resources[key]
            removed.add(helmet)
            body_files = write_bundle(output / 'bodies' / character / f'{ARCHIVE}.patch_0', data[0], resources)
            bodies.append({'character': character, 'removed_helmet_unit': helmet, 'retained_resources': len(resources), 'files': body_files})
        if removed != set(DONOR_HELMETS.values()):
            raise ValueError('Incomplete removal of old helmets')
    return {'mapping': mapping, 'helmets': files, 'bodies': bodies, 'source_sha256': sha(source.read_bytes()),
            'kit_inventory_sha256': sha((starter.parent / 'all-kits.json').read_bytes()), 'kit_inventory_is_cached': True,
            'validation': 'Exactly four target kit consumers in cached definitions. Four old helmet units removed; every retained resource payload byte-identical.', 'runtime_tested': False}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--workspace', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--scopes', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    try:
        if args.out.exists():
            raise ValueError('Choose a fresh output directory')
        args.out.mkdir(parents=True)
        scope_report = scopes(args.scopes, args.out / 'scopes')
        helmet_report = helmets(args.workspace.resolve(), args.out / 'commandos')
        for name, report in [('scopes', scope_report), ('commandos', helmet_report)]:
            (args.out / f'{name}-report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        LOG.info('Built %s scope resources across %s archives and four independent helmet targets.', scope_report['resource_count'], scope_report['archives'])
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, struct.error, zipfile.BadZipFile):
        LOG.exception('Visual candidate build failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
