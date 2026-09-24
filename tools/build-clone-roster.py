#!/usr/bin/env python3
"""Stage independent legion materials on audited groups of shared clone units.

Only material/texture references change. Geometry, rigs, shaders and texture pixels
are preserved. The result still requires in-game visual and memory testing.
"""

import argparse
import hashlib
import importlib.util
import json
import logging
import re
import struct
import sys
import zipfile
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('player_visuals', ROOT / 'tools/build-player-visuals.py')
visuals = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = visuals
spec.loader.exec_module(visuals)
UNIT, MATERIAL, TEXTURE = visuals.UNIT, 0xeac0b497876adedf, 0xcd4238c6a0c69e32
LOG = logging.getLogger(__name__)
CHOICES = [
    ('212th P2', 'CM-09 Bonesnapper'),
    ('104th', 'CE-74 Breaker'),
    ('Coruscant Guard P2', 'DP-00 Tactical'),
    ('327th P2', 'CE-81 Juggernaut'),
    ('41st Camo', 'CE-27 Ground Breaker'),
    ('187th', 'DP-53 Savior of the Free'),
    ('Shiny', 'DP-8 Mountain-Scaled'),
    ('Shiny', 'FS-37 Ravager'),
    ('327th P2', 'FS-55 Devastator'),
    ('104th', 'CW-22 Kodiak'),
    ('Coruscant Guard P2', 'CM-21 Trench Paramedic'),
    ('212th P2', 'EX-03 Prototype 3'),
    ('41st Camo', 'B-27 Fortified Commando'),
    ('187th', 'AF-52 Lockdown'),
    ('Shiny', 'AD-11 Livewire'),
]


class Source:
    def __init__(self, path: Path):
        self.zip = zipfile.ZipFile(path)
        self.main = {}
        self.resources = {}
        for name in self.zip.namelist():
            if not re.search(r'\.patch_\d+$', name):
                continue
            blob = self.zip.read(name)
            self.main[name] = blob
            magic, types, count = struct.unpack_from('<III', blob)
            if magic != 0xf0000011:
                raise ValueError('Invalid donor archive')
            for i in range(count):
                row = list(visuals.ENTRY.unpack_from(blob, 72 + 32*types + 80*i))
                self.resources[name, tuple(row[:2])] = row

    def payload(self, locator: tuple[str, tuple[int, int]], full: bool = False):
        name, key = locator
        row = self.resources[locator]
        main = self.main[name][row[2]:row[2]+row[7]]
        if not full:
            return main
        data = [main]
        for suffix, offset, size in zip(visuals.SUFFIXES[1:], row[3:5], row[8:10]):
            if not size:
                data.append(b'')
                continue
            with self.zip.open(name + suffix) as stream:
                stream.seek(offset)
                data.append(stream.read(size))
        if tuple(map(len, data)) != tuple(row[7:10]):
            raise ValueError('Truncated donor')
        return visuals.Resource(row.copy(), tuple(data), name)


def build(output: Path, legions: Path, kit_path: Path) -> None:
    if output.exists():
        raise ValueError('Choose a fresh output directory')
    source = Source(ROOT / 'dist/mods/recovered - Clone Armory - Main Mod.zip')
    alternate = Source(legions)
    # Follow the author's Include order. The old deployed order incorrectly put
    # Body Expansion after Material Reset, overriding the legion-ready body material.
    winning, white = {}, {}
    folders = ['Body Main', 'Body Expansion', 'Material Reset', 'Phase 2 Helmets', 'Legion Textures/501st P2']
    def order(locator):
        folder = str(Path(locator[0]).parent).replace('\\', '/')
        return folders.index(folder) if folder in folders else len(folders)
    for locator in sorted(source.resources, key=order):
        name, key = locator
        winning[key] = locator
        if not name.startswith('Legion Textures/'):
            white[key] = locator
    colors = {key for name, key in source.resources if name.startswith('Legion Textures/') and key[1] == TEXTURE}
    kits = json.loads(kit_path.read_text())
    body_kits = [k for k in kits if k['kind'] == 0]
    units = {k['id']: {int(p['unit'], 16) for p in k['pieces'] if (int(p['unit'], 16), UNIT) in winning} for k in body_kits}
    parent = {u: u for group in units.values() for u in group}

    def find(unit: int) -> int:
        while parent[unit] != unit:
            parent[unit] = parent[parent[unit]]
            unit = parent[unit]
        return unit

    for group in units.values():
        if group:
            first = next(iter(group))
            for unit in group:
                parent[find(unit)] = find(first)
    components = defaultdict(set)
    for unit in parent:
        components[find(unit)].add(unit)
    output_resources, changes, mapping = {}, [], []
    reserved = {key[0] for key in winning}
    inventory = (ROOT / 'dist/rc-upgrade/all-assets-routing.txt').read_text(encoding='utf-8-sig')
    reserved.update(int(value, 16) for value in re.findall(r'0x([0-9a-f]{16})\.', inventory))
    generated = {}

    def identity(legion: str, key: tuple[int, int]) -> tuple[int, int]:
        token = (legion, key)
        if token not in generated:
            value = int.from_bytes(hashlib.sha256(f'clonedivers/roster/v1/{legion}/{key[0]:016x}/{key[1]:016x}'.encode()).digest()[:8], 'little')
            if value in reserved:
                raise ValueError('Generated resource ID collision')
            reserved.add(value)
            generated[token] = (value, key[1])
        return generated[token]

    claimed_units = set()
    protected = {int(value, 16) for value in visuals.TARGETS.values()}
    # The roster loads after Delta meshes. Never replace a Commando body unit
    # with an ordinary clone merely because its stock armor shares another piece.
    delta_source = Source(ROOT / 'dist/mods/Delta Squad AIO-552-1-1A-1777439542.zip')
    protected.update(key[0] for name, key in delta_source.resources if key[1] == UNIT)
    delta_source.zip.close()
    for legion, anchor in CHOICES:
        anchor_kits = [k for k in body_kits if k['name'] == anchor and units[k['id']]]
        if not anchor_kits:
            raise ValueError(f'No clone body for {anchor}')
        roots = {find(next(iter(units[k['id']]))) for k in anchor_kits}
        selected_units = set().union(*(components[root] for root in roots))
        names = {k['name'] for k in body_kits if units[k['id']] & selected_units}
        helmets = [k for k in kits if k['kind'] == 1 and k['name'] in names]
        selected_units.update(int(p['unit'],16) for k in helmets for p in k['pieces'] if (int(p['unit'],16),UNIT) in winning)
        selected_units -= protected
        if claimed_units & selected_units:
            raise ValueError('Legion groups share a unit')
        claimed_units.update(selected_units)
        legion_textures = {key: locator for locator in alternate.resources for name, key in [locator]
                           if name.startswith(f'Legion Textures/{legion}/') and key[1] == TEXTURE}
        if legion != 'Shiny' and not colors <= legion_textures.keys():
            raise ValueError(f'{legion} lacks a required Phase 2 texture')
        mapped_materials = {}

        def texture(key: tuple[int, int]) -> tuple[int, int]:
            if key not in colors:
                return key
            new = identity(legion, key)
            if new not in output_resources:
                locator = white[key] if legion == 'Shiny' else legion_textures[key]
                donor = source if legion == 'Shiny' else alternate
                output_resources[new] = donor.payload(locator, full=True)
            return new

        def material(key: tuple[int, int], stack: frozenset = frozenset()) -> tuple[int, int]:
            if key in mapped_materials:
                return mapped_materials[key]
            if key not in winning:
                return key
            if key in stack:
                raise ValueError('Material inheritance cycle')
            original = source.payload(winning[key])
            edited = bytearray(original)
            count = struct.unpack_from('<I', original, 64)[0]
            if 136 + count*12 > len(original):
                raise ValueError('Invalid material texture table')
            offsets = [136 + count*4 + i*8 for i in range(count)]
            for offset in offsets:
                old = struct.unpack_from('<Q', original, offset)[0]
                struct.pack_into('<Q', edited, offset, texture((old, TEXTURE))[0])
            base = struct.unpack_from('<Q', original, 24)[0]
            if base:
                struct.pack_into('<Q', edited, 24, material((base, MATERIAL), stack | {key})[0])
            new = key
            if edited != original:
                new = identity(legion, key)
                resource = source.payload(winning[key], full=True)
                resource.payloads = (bytes(edited), *resource.payloads[1:])
                output_resources[new] = resource
                changes.append({'legion': legion, 'type': 'material', 'source': f'{key[0]:016x}', 'target': f'{new[0]:016x}', 'changedOffsets': [i for i, (a,b) in enumerate(zip(original,edited)) if a!=b]})
            mapped_materials[key] = new
            return new

        changed_units = set()
        for unit in sorted(selected_units):
            key = unit, UNIT
            original = source.payload(winning[key])
            edited = bytearray(original)
            offset = struct.unpack_from('<I', original, 112)[0]
            count = struct.unpack_from('<I', original, offset)[0]
            if count > 100 or offset + 4 + count*12 > len(original):
                raise ValueError('Invalid unit material table')
            for i in range(count):
                field = offset + 4 + count*4 + i*8
                old = struct.unpack_from('<Q', original, field)[0]
                struct.pack_into('<Q', edited, field, material((old, MATERIAL))[0])
            if original != edited:
                resource = source.payload(winning[key], full=True)
                resource.payloads = (bytes(edited), *resource.payloads[1:])
                output_resources[key] = resource
                changed_units.add(unit)
                changes.append({'legion': legion, 'type': 'unit', 'source': f'{unit:016x}', 'target': f'{unit:016x}', 'changedOffsets': [i for i,(a,b) in enumerate(zip(original,edited)) if a!=b]})
        if not any(units[k['id']] & changed_units for k in anchor_kits):
            raise ValueError(f'No body recolored for {legion}; helmet-only output is incomplete')
        consumers = [k for k in kits if any(int(p['unit'],16) in changed_units for p in k['pieces'])]
        mapping.append({'legion': legion, 'anchor': anchor, 'unitsChanged': len(changed_units),
                        'items': [{'name': k['name'], 'kit': k['id'], 'kind': k['kind']} for k in consumers]})
        LOG.info('%s: %s units, %s item definitions', legion, len(changed_units), len(consumers))
    header = next(iter(source.main.values()))[:72]
    files = visuals.write_bundle(output / f'{visuals.ARCHIVE}.patch_0', header, output_resources)
    (output / 'roster-report.json').write_text(json.dumps({'mapping': mapping, 'changes': changes,
        'files': files, 'kitInventorySha256': hashlib.sha256(kit_path.read_bytes()).hexdigest(),
        'kitInventorySource': str(kit_path.resolve()), 'runtimeTested': False, 'baseLegion': '501st',
        'sourceOrder': folders,
        'protectedCommandoUnits': [f'{unit:016x}' for unit in sorted(protected)],
        'validation': 'Byte-identical geometry, rigs, textures and shaders. Only unit material IDs and material texture/base IDs changed; serialized payloads round-trip.'}, indent=2) + '\n', encoding='utf-8')
    source.zip.close()
    alternate.zip.close()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--legions', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--kits', type=Path, default=ROOT / 'dist/rc-upgrade/starter-commandos/all-kits.json')
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    try:
        build(args.out, args.legions, args.kits)
        return 0
    except (OSError, ValueError, KeyError, struct.error, zipfile.BadZipFile):
        LOG.exception('Clone roster build failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
