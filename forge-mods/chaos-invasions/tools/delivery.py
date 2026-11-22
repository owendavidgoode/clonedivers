#!/usr/bin/env python3
"""Prepare the private Lua archive and verify the chaos-invasions-0.3.3 ZIP artifact."""
from __future__ import annotations

import argparse
from collections.abc import Sequence
import hashlib
import json
import logging
from pathlib import Path
import struct
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
MIXING = Path('D:/ClaudeWork/mixing')
ZIP = ROOT / 'dist/chaos-invasions-0.3.3.zip'
REFERENCE = MIXING / 'heavy-seaf-src/heavy-seaf-presence'
ROW = struct.Struct('<7Q6I')
LUA_TYPE = 0xa14e8dfa2cd117e2
ARCHIVE = '9ba626afa44a3aa3.patch_910'


def pin(path: Path) -> dict[str, object]:
    return {'path': str(path), 'bytes': path.stat().st_size,
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}


def save(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + '.partial')
    temp.write_text(json.dumps(value, indent=2, ensure_ascii=True) + '\n', encoding='utf-8')
    temp.replace(path)


def resource_hash(name: str) -> int:
    """Stingray MurmurHash64A, seed zero; checked against the base addon identity."""
    data = name.encode('utf-8')
    mask, multiplier, shift = (1 << 64) - 1, 0xc6a4a7935bd1e995, 47
    value = len(data) * multiplier & mask
    end = len(data) // 8 * 8
    for offset in range(0, end, 8):
        part = int.from_bytes(data[offset:offset + 8], 'little') * multiplier & mask
        part ^= part >> shift
        part = part * multiplier & mask
        value = (value ^ part) * multiplier & mask
    if end < len(data):
        value = (value ^ int.from_bytes(data[end:], 'little')) * multiplier & mask
    value ^= value >> shift
    value = value * multiplier & mask
    return value ^ (value >> shift)


def prepare() -> None:
    """Build the standalone single-Lua source archive for Chaos Invasions."""
    template = (REFERENCE / 'data/9ba626afa44a3aa3.patch_900').read_bytes()
    if struct.unpack_from('<3I', template) != (0xf0000011, 1, 1):
        raise ValueError('Changed Heavy SEAF archive framing')
    row = ROW.unpack_from(template, 104)
    old = template[row[2]:row[2] + row[7]]
    if row[1] != LUA_TYPE or struct.unpack_from('<2I', old) != (len(old) - 8, 2):
        raise ValueError('Expected one source-text Lua resource')
    if row[0] != resource_hash('mods/kohl/heavy_seaf_presence'):
        raise ValueError('Base addon path/hash contract changed')
    resource = resource_hash('mods/clonedivers/chaos_invasions')
    source = (ROOT / 'src/menu_bridge.lua').read_bytes()
    payload = struct.pack('<2I', len(source), 2) + source
    head = bytearray(template[:192])
    values = list(row)
    values[0], values[2], values[7] = resource, 192, len(payload)
    ROW.pack_into(head, 104, *values)
    raw = head + payload
    raw += bytes(-len(raw) % 16)
    struct.pack_into('<Q', raw, 32, len(raw))
    data = ROOT / 'data'
    data.mkdir(exist_ok=True)
    (data / ARCHIVE).write_bytes(raw)
    for suffix in ('.stream', '.gpu_resources'):
        (data / (ARCHIVE + suffix)).write_bytes(b'')
    save(ROOT / 'build/chaos-source-evidence.json', {
        'build': '25480438',
        'sdk': 16,
        'version': '0.3.3',
        'lua_resource': f'{resource:016x}',
        'lua_source_exact': True,
        'template_lua_resource': f'{row[0]:016x}',
    })
    print(f'Prepared unique Lua archive {ARCHIVE} ({len(raw)} bytes)')


def verify() -> None:
    if not ZIP.is_file():
        raise ValueError(f'Missing package ZIP: {ZIP}')
    count = 0
    with zipfile.ZipFile(ZIP) as archive:
        files = [name for name in archive.namelist() if not name.endswith('/')]
        for name in files:
            relative = Path(name).relative_to('chaos-invasions')
            source = ROOT / relative
            if not source.is_file() or archive.read(name) != source.read_bytes():
                raise ValueError(f'ZIP source mismatch: {name}')
            count += 1
        for required in ('hellforge-mod.json', 'options.ini', 'src/chaos_invasions.c',
                         'src/policy.h', 'src/clonedivers_host_options.h',
                         'data/' + ARCHIVE, 'src/menu_bridge.lua', 'CREDITS.md'):
            if 'chaos-invasions/' + required not in files:
                raise ValueError(f'Missing artifact member {required}')

    manifest = json.loads((ROOT / 'hellforge-mod.json').read_text(encoding='utf-8'))
    if (manifest.get('id') != 'clonedivers.chaos-invasions' or
            manifest.get('name') != 'Chaos Invasions' or
            manifest.get('version') != '0.3.3' or
            manifest.get('gameplay') is not True or
            manifest.get('code') != {'dll': 'chaos_invasions.dll', 'sdk': 16, 'game': '25480438'}):
        raise ValueError(f'Invalid hellforge-mod.json metadata: {manifest}')
    count += 1

    opts = (ROOT / 'options.ini').read_text(encoding='utf-8')
    for expected in ('enabled=1', 'cooldown_seconds=150', 'landing_grace_seconds=45',
                     'total_cap=12', 'faction_cap=6', 'squad_size=4', 'mission_budget=36',
                     'droid_weight=100', 'squid_weight=80', 'bug_weight=100'):
        if expected not in opts.splitlines():
            raise ValueError(f'Missing default in options.ini: {expected}')
    count += 1

    raw = (ROOT / 'data' / ARCHIVE).read_bytes()
    row = ROW.unpack_from(raw, 104)
    payload = raw[row[2]:row[2] + row[7]]
    if row[0] != resource_hash('mods/clonedivers/chaos_invasions'):
        raise ValueError('Lua path hash mismatch')
    if payload[8:] != (ROOT / 'src/menu_bridge.lua').read_bytes():
        raise ValueError('Lua source differs from packaged archive')
    if struct.unpack_from('<2I', payload) != (len(payload) - 8, 2):
        raise ValueError('Invalid Lua source wrapper')
    count += 3

    save(ROOT / 'build/chaos-delivery-verification.json', {
        'passed': True,
        'checks': count,
        'zip': pin(ZIP),
        'dll': pin(ROOT / 'chaos_invasions.dll'),
        'source_members_exact': files,
        'lua_exact': True,
    })
    print(f'PASS {count} delivery verification checks on {ZIP.name}')


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('prepare', 'verify'))
    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s')
    try:
        prepare() if args.command == 'prepare' else verify()
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, zipfile.BadZipFile):
        logging.exception('Offline delivery failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
