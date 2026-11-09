#!/usr/bin/env python3
"""Build and verify an offline Empire cosmetic candidate for Forge 25480438."""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from collections.abc import Sequence
from dataclasses import dataclass
import functools
import hashlib
import importlib.util
import json
import logging
import os
import shutil
from pathlib import Path
import re
import struct
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parent
OLD = Path('C:/Users/goode/OneDrive/Desktop/clonedivers')
AREA = OLD / 'dist/hd2-update-2026-10-25'
BASE = Path('D:/HellForge/mods/empire-r19-qa-20261104')
GAME = Path('D:/HellForge/game/data')
SUFFIXES = ('', '.stream', '.gpu_resources')
ROW = struct.Struct('<7Q6I')
MAIN = re.compile(r'[a-f0-9]{16}\.patch_\d+')
UNIT = 0xe0a48d0be9a7453f
BANK = 0x535a7bd3e650d799
STREAM = 0x504b55235d21440e
TEXT = 0x0d972bab10b40fd3
LUA = 0xa14e8dfa2cd117e2


def read(path: Path) -> dict:
    return json.loads(path.read_text(encoding='utf-8-sig'))


def save(name: str, value: object) -> None:
    (ROOT / name).write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def pin(path: Path) -> dict:
    with path.open('rb') as handle:
        digest = hashlib.file_digest(handle, 'sha256').hexdigest()
    return {'path': str(path), 'size': path.stat().st_size, 'sha256': digest}


SOURCE_PINS = {}


def verify_source(path: Path, expected: dict) -> None:
    identity = str(path), expected['sha256'], expected['size']
    if identity in SOURCE_PINS:
        return
    proof = pin(path)
    if proof['sha256'] != expected['sha256'] or proof['size'] != expected['size']:
        raise ValueError(f'Source hash/size mismatch {path}')
    SOURCE_PINS[identity] = proof


def load(name: str, file: str) -> object:
    spec = importlib.util.spec_from_file_location(name, OLD / 'tools' / file)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def directory(path: Path) -> list[tuple[int, ...]]:
    with path.open('rb') as handle:
        header = handle.read(72)
        magic, nt, nf = struct.unpack_from('<III', header)
        if magic != 0xf0000011 or nt > 100000 or nf > 1000000:
            raise ValueError(f'Invalid bounded archive {path}')
        handle.seek(72 + 32 * nt)
        rows = list(ROW.iter_unpack(handle.read(80 * nf)))
    if len(rows) != nf or len({r[:2] for r in rows}) != nf:
        raise ValueError(f'Incomplete or duplicate directory {path}')
    return rows


@dataclass
class Resource:
    path: Path
    row: tuple[int, ...]
    companions: tuple[Path | None, ...]

    def parts(self) -> tuple[bytes, ...]:
        result = []
        for i, path in enumerate(self.companions):
            if not self.row[7 + i]:
                result.append(b'')
                continue
            with path.open('rb') as handle:
                handle.seek(self.row[2 + i])
                data = handle.read(self.row[7 + i])
            if len(data) != self.row[7 + i]:
                raise ValueError(f'Truncated resource {path}')
            result.append(data)
        return tuple(result)


@dataclass
class Generated:
    payloads: tuple[bytes, ...]

    def parts(self) -> tuple[bytes, ...]:
        return self.payloads


def selected(version: int) -> tuple[dict, dict, dict]:
    doc = read(AREA / f'selection-v{version}/production96.json')
    choice = next(s for s in doc['selections'] if s['mode'] == 'EmpireDivers'
                  and s['profile'] == 'full' and s['requestedOptions']['droids']
                  and not s['requestedOptions']['covenant'] and not s['requestedOptions']['tieBomber'])
    rows = doc['fileSets'][choice['fileSet']]
    registry = read(AREA / f'selection-qa-v{version}-current/asset-registry.json')
    files = {}
    for row in rows:
        if not row['size']:
            files[row['name']] = None
            continue
        path = Path(registry[row['sha256']])
        if not path.is_relative_to(OLD):
            path = OLD / 'dist/hd2-update-2026-10-21/feed-vault' / row['sha256']
        if not path.is_file():
            raise ValueError(f'Missing read-only retained source {row["name"]}: {path}')
        verify_source(path, row)
        files[row['name']] = path
    wins = {}
    for name in sorted((n for n in files if MAIN.fullmatch(n)), key=lambda n: (n[:16], int(n.split('_')[-1]))):
        paths = tuple(files.get(name + suffix) for suffix in SUFFIXES)
        for row in directory(files[name]):
            wins[row[:2]] = Resource(files[name], row, paths)
    return wins, choice, {r['name']: r for r in rows}


def baseline() -> dict:
    wins = {}
    for path in sorted((p for p in BASE.iterdir() if MAIN.fullmatch(p.name)), key=lambda p: (p.name[:16], int(p.name.split('_')[-1]))):
        paths = tuple(path.with_name(path.name + suffix) for suffix in SUFFIXES)
        for row in directory(path):
            wins[row[:2]] = Resource(path, row, paths)
    return wins


def matching_build() -> tuple[dict, dict]:
    area = OLD / 'dist/production-r41-2026-10-18'
    doc = read(area / 'selection-v1/production96.json')
    choice = next(s for s in doc['selections'] if s['mode'] == 'EmpireDivers'
                  and s['profile'] == 'full' and s['requestedOptions']['droids']
                  and not s['requestedOptions']['covenant'])
    rows = doc['fileSets'][choice['fileSet']]
    registry = {p['sha256']: Path(p['path']) for p in read(area / 'candidate-v1/assets.json')['assets']}
    export = read(BASE / 'clonedivers-export.json')
    registry.update({r['Sha256']: BASE / r['Name'] for r in export['Selection']['Files']})
    files = {}
    missing = []
    for row in rows:
        if not row['size']:
            files[row['name']] = None
            continue
        digest = row['sha256']
        candidates = [area / 'dependency-assets-v1' / digest,
                      OLD / 'dist/hd2-update-2026-10-21/feed-vault' / digest,
                      registry.get(digest, ROOT / 'absent')]
        path = next((p for p in candidates if p.is_file()), None)
        if path is None:
            missing.append(row)
        files[row['name']] = path
    if missing:
        needed = {r['sha256'] for r in missing}
        found = {}
        for parent, folders, names in os.walk(OLD / 'dist'):
            folders[:] = [n for n in folders if n not in ('.git', '__pycache__', 'node_modules', '.venv') and not n.startswith('uv-')]
            for name in needed.intersection(names):
                found[name] = Path(parent) / name
            if needed <= found.keys():
                break
        for row in missing:
            files[row['name']] = found.get(row['sha256'])
        missing = [r for r in missing if files[r['name']] is None]
        if missing:
            save('missing-r41-sources.json', missing)
            raise ValueError(f'{len(missing)} retained r41 sources missing')
    for row in rows:
        if row['size']:
            verify_source(files[row['name']], row)
    wins = {}
    for name in sorted((n for n in files if MAIN.fullmatch(n)), key=lambda n: (n[:16], int(n.split('_')[-1]))):
        paths = tuple(files.get(name + suffix) for suffix in SUFFIXES)
        for row in directory(files[name]):
            if row[1] != LUA:
                wins[row[:2]] = Resource(files[name], row, paths)
    return wins, choice


def fmt(wem: bytes) -> int | None:
    if wem[:4] != b'RIFF' or wem[8:12] != b'WAVE':
        return None
    at = 12
    while at + 8 <= len(wem):
        tag, size = struct.unpack_from('<4sI', wem, at)
        if tag == b'fmt ':
            return struct.unpack_from('<H', wem, at + 8)[0]
        at += 8 + size + size % 2
    return None


def unit_safe(parts: tuple[bytes, ...], stock: bytes) -> bool:
    raw = parts[0]
    if len(raw) < len(stock):
        return False
    normalized = bytearray(raw[:len(stock)])
    for offset in (48, 52, 88, 92, 96, 100, 104, 108, 112):
        normalized[offset:offset+4] = stock[offset:offset+4]
    # Material resource references in the retained native table are visual data.
    at = struct.unpack_from('<I', stock, 112)[0]
    if at:
        count = struct.unpack_from('<I', stock, at)[0]
        start = at + 4 + 4 * count
        end = start + 8 * count
        if count > 10000 or end > len(stock):
            raise ValueError('Native material map bounds')
        normalized[start:end] = stock[start:end]
    return bytes(normalized) == stock


def write_archive(path: Path, header: bytes, resources: dict) -> None:
    """Stream the project's archive layout with a fully aligned directory head."""
    kinds = sorted({k[1] for k in resources})
    keys = sorted(resources, key=lambda k: (k[1], k[0]))
    head = bytearray(header)
    struct.pack_into('<II', head, 4, len(kinds), len(keys))
    table = b''.join(struct.pack('<QQQII', 0, kind, sum(k[1] == kind for k in keys), 16, 64) for kind in kinds)
    toc_end = 72 + len(table) + len(keys) * 80
    start = (toc_end + 8 + 255) // 256 * 256
    temporary = [path.with_name(path.name + suffix + '.partial') for suffix in SUFFIXES]
    handles = [p.open('w+b') for p in temporary]
    try:
        handles[0].write(head + table + bytes(start - 72 - len(table)))
        rows = []
        for ordinal, key in enumerate(keys):
            parts = resources[key].parts()
            offsets = []
            for i, handle in enumerate(handles):
                handle.write(bytes(-handle.tell() % (64 if i == 2 else 16)))
                offsets.append(handle.tell())
                handle.write(parts[i])
                if i == 0 and key[1] == STREAM and len(parts[i]) < 16:
                    handle.write(bytes(16 - len(parts[i])))
            rows.append(ROW.pack(*key, *offsets, 0, 0, *map(len, parts), 16, 64, ordinal))
        handles[0].seek(72 + len(table))
        handles[0].write(b''.join(rows))
    finally:
        for handle in handles:
            handle.close()
    for suffix, tmp in zip(SUFFIXES, temporary, strict=True):
        tmp.replace(path.with_name(path.name + suffix))


def build() -> None:
    out = ROOT / 'empire-forge-r45-v3'
    if out.exists():
        raise ValueError('Candidate output already exists')
    old, old_choice = matching_build()
    latest, choice, _ = selected(76)
    torso, _, _ = selected(75)
    for identity in (0x2d9ce590011788ac, 0x18902007139a6060):
        latest[identity, UNIT] = torso[identity, UNIT]
    index = read(ROOT / 'native-index-25480438.json')
    newer = read(OLD / 'dist/hd2-update-2026-10-29/native-index-v1/native-index.json')
    native = load('forge_build_native', 'walker-next-native.py').NativeSlim(GAME)
    native.chunk = functools.lru_cache(maxsize=32)(native.chunk)
    audio = load('forge_build_audio', 'rebase-player-audio.py')
    labels = load('forge_build_labels', 'rc-voice-labels.py')
    hash_module = load('forge_hash', '../dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d/sdk/tools/hd2_archive.py')

    @functools.lru_cache(maxsize=512)
    def stock(key: tuple[int, int]) -> tuple[bytes, ...]:
        provider = index[f'{key[0]:016x}.{key[1]:016x}'][0]
        row = provider['row']
        return tuple(native.read(provider['archive'] + suffix, row[2+i], row[7+i]) for i, suffix in enumerate(SUFFIXES))

    expectations = defaultdict(set)
    for label in index:
        key = tuple(int(v, 16) for v in label.split('.'))
        if key[1] != BANK:
            continue
        bank = audio.chunks(old[key].parts()[0] if key in old else stock(key)[0])
        for mid, (_, sound) in audio.sounds(bank.get(b'HIRC', bytes(4))).items():
            plugin, mode = struct.unpack_from('<IB', sound)
            if mode not in (1, 2) or plugin not in (0x40001, 0x10001):
                continue
            for prefix in ('content/audio/', 'content/audio/us/'):
                expectations[hash_module.resource_hash(prefix + str(mid)), STREAM].add(plugin)
    print('Native sound contracts indexed', len(expectations), flush=True)
    result = dict(old)
    decisions = []
    counts = Counter()
    for key, resource in latest.items():
        label = f'{key[0]:016x}.{key[1]:016x}'
        kind = key[1]
        parts = resource.parts()
        if key in old and parts == old[key].parts():
            counts['r41Exact'] += 1
            continue
        reason = 'authored-asset'
        selected_resource = resource
        if label in newer and label not in index:
            reason = 'excluded-newer-native-identity'
        elif kind == UNIT and label in index and not unit_safe(parts, stock(key)[0]):
            reason = 'retained-r41-native-control-needs-rebuild'
        elif kind == BANK:
            reason = 'retained-r41-matching-build-bank'
        elif kind == 0xaf32095c82f2b070:
            reason = 'retained-r41-sound-dependencies'
        elif kind == STREAM:
            if label not in index:
                reason = 'retained-r41-private-stream'
            else:
                allowed = {0x40001: {0xffff}, 0x10001: {1, 0xfffe}}
                if any(fmt(parts[1]) not in allowed[p] for p in expectations.get(key, ())):
                    reason = 'retained-r41-stream-codec'
                elif len(stock(key)[0]) != 12 or fmt(parts[1]) is None:
                    reason = 'retained-r41-stream-wrapper'
                else:
                    selected_resource = Generated((stock(key)[0][:8] + struct.pack('<I', len(parts[1])), parts[1], b''))
                    reason = 'rebuilt-stream-native-wrapper'
        elif kind == TEXT:
            source_table, _ = labels.parse_strings(parts[0])
            stock_table, _ = labels.parse_strings(stock(key)[0])
            merged = {k: source_table.get(k, v) for k, v in stock_table.items()}
            keys = sorted(merged)
            raw = bytearray(stock(key)[0][:16])
            struct.pack_into('<I', raw, 8, len(keys))
            raw.extend(struct.pack(f'<{len(keys)}I', *keys) + bytes(4 * len(keys)))
            for i, text_key in enumerate(keys):
                struct.pack_into('<I', raw, 16 + 4 * len(keys) + 4 * i, len(raw))
                raw.extend(merged[text_key].encode('utf-8') + b'\0')
            assert labels.parse_strings(bytes(raw))[0] == merged
            selected_resource = Generated((bytes(raw), b'', b''))
            reason = 'rebuilt-text-forge-key-set'
        if not reason.startswith(('retained-', 'excluded-')):
            result[key] = selected_resource
        counts[reason] += 1
        decisions.append({'key': label, 'decision': reason, 'source': str(resource.path),
                          'sourcePayloadSha256': list(map(sha, parts)),
                          'finalPayloadSha256': list(map(sha, result[key].parts())) if key in result else None})
    # Retain Forge's wrappers even for unchanged r41 audio, and reject only a
    # demonstrably incompatible selected stream in favor of native fallback.
    for key in list(result):
        label = f'{key[0]:016x}.{key[1]:016x}'
        if key[1] in (BANK, 0xaf32095c82f2b070) and label not in index and label in newer:
            del result[key]
            decisions.append({'key': label, 'decision': 'excluded-obsolete-bank-family'})
            counts['excluded-obsolete-bank-family'] += 1
            continue
        if key[1] != STREAM or label not in index:
            continue
        parts = result[key].parts()
        allowed = {0x40001: {0xffff}, 0x10001: {1, 0xfffe}}
        if any(fmt(parts[1]) not in allowed[p] for p in expectations.get(key, ())):
            del result[key]
            decisions.append({'key': label, 'decision': 'native-fallback-codec-incompatible'})
            counts['native-fallback-codec-incompatible'] += 1
        else:
            result[key] = Generated((stock(key)[0][:8] + struct.pack('<I', len(parts[1])), parts[1], b''))
    save('port-decisions.json', {'counts': dict(counts), 'r41Choice': old_choice, 'latestChoice': choice, 'resources': decisions})
    save('source-pins.json', list(SOURCE_PINS.values()))
    save('expected-resources.json', {f'{k[0]:016x}.{k[1]:016x}': list(map(sha, r.parts())) for k, r in result.items()})
    print('Port decisions', dict(counts), 'final winners', len(result), flush=True)
    out.mkdir()
    header = next(BASE.glob('*.patch_0')).read_bytes()[:72]
    buckets = defaultdict(dict)
    for key, resource in result.items():
        buckets[key[0] % 32][key] = resource
    for bucket, resources in sorted(buckets.items()):
        write_archive(out / f'9ba626afa44a3aa3.patch_{1000+bucket}', header, resources)
        print('Written bucket', bucket, len(resources), flush=True)
    manifest = {'Version': 1, 'Guid': '3098c19e-46e7-4779-8c1e-020106452438',
                'Name': 'EmpireDivers Forge r45 offline candidate (25480438)',
                'Description': 'Private Full appearance candidate. R41 matching-build base with qualified r44/75/76 assets. Runtime and multiplayer validation pending.',
                'Options': []}
    (out / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    for path in (OLD / 'dist/production-r41-2026-10-18/candidate-v1').glob('*.txt'):
        shutil.copyfile(path, out / path.name)
    for path in (AREA / 'candidate-v76').glob('*.txt'):
        shutil.copyfile(path, out / path.name)
    records = [pin(p) for p in sorted(out.iterdir()) if p.is_file()]
    save('build-result.json', {'build': '25480438', 'resources': len(result), 'counts': dict(counts),
                               'files': records, 'runtimeTested': False, 'luaFeaturesIncluded': False})


def inventory() -> None:
    wins, choice, files = selected(76)
    base = baseline()
    index = read(ROOT / 'native-index-25480438.json')
    current = read(OLD / 'dist/hd2-update-2026-10-29/native-index-v1/native-index.json')
    native = load('empire_native', 'walker-next-native.py').NativeSlim(GAME)
    native.chunk = functools.lru_cache(maxsize=32)(native.chunk)
    same, changed, new = Counter(), Counter(), Counter()
    unknown, collisions, prefixes = [], [], []
    for key, res in wins.items():
        label = f'{key[0]:016x}.{key[1]:016x}'
        kind = f'{key[1]:016x}'
        parts = res.parts()
        if key in base:
            if parts == base[key].parts():
                same[kind] += 1
                continue
            changed[kind] += 1
        else:
            new[kind] += 1
        providers = index.get(label, [])
        if not providers and label in current:
            unknown.append({'key': label, 'source': str(res.path), 'currentFamilies': [p['archive'] for p in current[label]]})
        if providers and label not in current:
            collisions.append({'key': label, 'source': str(res.path)})
        if key[1] == UNIT and providers:
            provider = providers[0]
            nr = provider['row']
            stock = native.read(provider['archive'], nr[2], nr[7])
            # Existing builders append visuals and retain native nonvisual prefix.
            mask = {48, 52, 88, 92, 96, 100, 104, 108, 112}
            fields = [i for i in range(0, min(116, len(stock), len(parts[0])), 4)
                      if i not in mask and stock[i:i+4] != parts[0][i:i+4]]
            normalized = bytearray(parts[0][:len(stock)])
            for offset in mask:
                normalized[offset:offset+4] = stock[offset:offset+4]
            mismatch = [i for i, (a, b) in enumerate(zip(normalized, stock)) if a != b]
            prefixes.append({'key': label, 'source': str(res.path), 'forgeNativeBytes': len(stock),
                             'mainBytes': len(parts[0]), 'headerDifferencesOutsideVisualPointers': fields,
                             'nativePrefixExactMasked': bytes(normalized) == stock,
                             'nativePrefixMismatchCount': len(mismatch), 'firstMismatch': mismatch[:12],
                             'forgeFamily': provider['archive']})
    summary = {'choice': choice, 'files': len(files), 'winners': len(wins), 'r19Winners': len(base),
               'same': dict(same), 'changed': dict(changed), 'added': dict(new),
               'newerNativeKeysAbsentOnForge': unknown, 'newPrivateNamesCollidingOnForge': collisions,
               'unitHeaders': prefixes}
    save('input-inventory.json', summary)
    print(json.dumps({k: v for k, v in summary.items() if k not in ('unitHeaders', 'newerNativeKeysAbsentOnForge', 'newPrivateNamesCollidingOnForge')}, indent=2))
    print('Absent newer-native identities', len(unknown), 'private collisions', len(collisions),
          'unit header differences', Counter(tuple(p['headerDifferencesOutsideVisualPointers']) for p in prefixes),
          'exact masked prefixes', sum(p['nativePrefixExactMasked'] for p in prefixes), flush=True)


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('inventory', 'build'))
    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.INFO)
    try:
        if args.command == 'inventory':
            inventory()
        elif args.command == 'build':
            build()
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception('Empire offline operation failed')
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
