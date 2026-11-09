#!/usr/bin/env python3
"""Inventory read-only Empire inputs and Forge native archive directories."""
import functools
import hashlib
import importlib.util
import json
import struct
import sys
from collections import Counter
from pathlib import Path

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parent
OLD = Path('C:/Users/goode/OneDrive/Desktop/clonedivers')
AREA = OLD / 'dist/hd2-update-2026-10-25'


def read(path: Path) -> dict:
    return json.loads(path.read_text(encoding='utf-8-sig'))


def native_families(archives: dict, data: Path) -> list[str]:
    names = {n for n in archives if len(n) == 16}
    names.update(p.name for p in data.iterdir() if p.is_file() and len(p.name) == 16
                 and all(c in '0123456789abcdef' for c in p.name))
    return sorted(names)


def main() -> int:
    print('Python', sys.version, flush=True)
    print('LZ4', importlib.util.find_spec('lz4'), flush=True)
    for version in (66, 72, 75, 76):
        doc = read(AREA / f'selection-v{version}/production96.json')
        selection = next(s for s in doc['selections'] if s['mode'] == 'EmpireDivers' and s['profile'] == 'full')
        print('selection', version, selection, 'files', len(doc['fileSets'][selection['fileSet']]), flush=True)
    spec = importlib.util.spec_from_file_location('native_reader', OLD / 'tools/walker-next-native.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    try:
        import lz4.block
        module.lz4_block = lambda data, size: lz4.block.decompress(data, uncompressed_size=size)
    except ImportError:
        pass
    native = module.NativeSlim(Path('D:/HellForge/game/data'))
    native.chunk = functools.lru_cache(maxsize=32)(native.chunk)
    index = {}
    names = native_families(native.archives, native.data)
    print('Native base families', len(names), flush=True)
    for i, name in enumerate(names):
        header = native.read(name, 0, 72)
        magic, nt, nf = struct.unpack_from('<III', header)
        if magic != 0xf0000011:
            raise ValueError(name)
        raw = native.read(name, 72 + 32 * nt, 80 * nf)
        for row in module.ROW.iter_unpack(raw):
            key = f'{row[0]:016x}.{row[1]:016x}'
            index.setdefault(key, []).append({'archive': name, 'row': row})
        if i % 500 == 0:
            print('Indexed', i, len(index), flush=True)
    (ROOT / 'native-index-25480438.json').write_text(json.dumps(index), encoding='utf-8')
    loose = []
    for name in names:
        path = native.data / name
        if path.is_file():
            with path.open('rb') as handle:
                digest = hashlib.file_digest(handle, 'sha256').hexdigest()
            loose.append({'path': str(path), 'size': path.stat().st_size, 'sha256': digest})
    (ROOT / 'native-loose-archive-pins.json').write_text(json.dumps(loose, indent=2) + '\n', encoding='utf-8')
    print('Indexed all', len(names), len(index), Counter(k.split('.')[1] for k in index).most_common(20), flush=True)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
