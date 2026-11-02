#!/usr/bin/env -S uv run
# /// script
# requires-python = ">=3.14"
# dependencies = ["lz4>=4.4"]
# ///
"""Stage a current-HD2 compatibility pack without installing or publishing it.

Rebase public audio and English text, retain authored media, remove unsupported
Lua archives, and prove unrelated resource bytes and manifest gates unchanged.
All inputs are read only; output must be a fresh directory.
"""

import argparse
import copy
import functools
import hashlib
import importlib
import json
import logging
import re
import struct
import subprocess
import sys
import urllib.request
from collections import Counter, defaultdict
from collections.abc import Sequence
from pathlib import Path
from typing import Any

import lz4.block

N = importlib.import_module('hd2-native-archives')
B = importlib.import_module('hd2-bank-rebase')
A = importlib.import_module('rebase-player-audio')
T = importlib.import_module('rc-voice-labels')
P = importlib.import_module('slim-archive-preflight')
ROW = struct.Struct('<7Q6I')
BANK, DEP, STREAM, TEXT, LUA = 0x535a7bd3e650d799, 0xaf32095c82f2b070, 0x504b55235d21440e, 0x0d972bab10b40fd3, 0xa14e8dfa2cd117e2
SUFFIXES = ('', '.stream', '.gpu_resources')
MAIN = re.compile(r'[a-f0-9]{16}\.patch_\d+$')
LOG = logging.getLogger(__name__)


def need(ok: bool, message: str) -> None:
    if not ok:
        raise ValueError(message)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def pin(path: Path) -> dict[str, Any]:
    with path.open('rb') as handle:
        digest = hashlib.file_digest(handle, 'sha256').hexdigest()
    return {'path': str(path.resolve()), 'bytes': path.stat().st_size, 'sha256': digest}


def read(path: Path) -> Any:
    return json.loads(path.read_text(encoding='utf-8-sig'))


def save(path: Path, value: Any) -> None:
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def directory(raw: bytes) -> list[tuple[int, ...]]:
    magic, nt, nf = struct.unpack_from('<III', raw)
    need(magic == 0xf0000011 and 72 + 32*nt + 80*nf <= len(raw), 'Archive directory extent')
    rows = list(ROW.iter_unpack(raw[72+32*nt:72+32*nt+80*nf]))
    need(len({r[:2] for r in rows}) == nf, 'Unique archive resources')
    return rows


def keytext(key: tuple[int, int]) -> str:
    return f'{key[0]:016x}.{key[1]:016x}'


def murmur64(text: str) -> int:
    data = text.encode()
    multiplier, mask = 0xc6a4a7935bd1e995, (1 << 64)-1
    value = len(data)*multiplier & mask
    for offset in range(0, len(data)-len(data)%8, 8):
        word = int.from_bytes(data[offset:offset+8], 'little')*multiplier & mask
        word ^= word >> 47
        value = (value ^ (word*multiplier & mask))*multiplier & mask
    if tail := data[len(data)-len(data)%8:]:
        value = (value ^ int.from_bytes(tail, 'little'))*multiplier & mask
    value = (value ^ (value >> 47))*multiplier & mask
    return value ^ (value >> 47)


def hierarchy(raw: bytes) -> dict[int, tuple[int, bytes]]:
    result, at = {}, 4
    for _ in range(struct.unpack_from('<I', raw)[0]):
        kind, size, identity = struct.unpack_from('<BII', raw, at)
        end = at+5+size
        need(size >= 4 and end <= len(raw) and identity not in result, 'Complete unique HIRC')
        result[identity] = kind, raw[at+9:end]
        at = end
    need(at == len(raw), 'HIRC count consumes payload')
    return result


def wem_format(raw: bytes) -> int | None:
    if raw[:4] != b'RIFF' or raw[8:12] != b'WAVE':
        return None
    at = 12
    while at+8 <= len(raw):
        tag, size = struct.unpack_from('<4sI', raw, at)
        if tag == b'fmt ' and size >= 16 and at+8+size <= len(raw):
            return struct.unpack_from('<H', raw, at+8)[0]
        at += 8+size+size%2
    return None


def serialize_bank(native: bytes, chunks: dict[bytes, bytes], media: dict[int, bytes]) -> bytes:
    if media:
        table, data = bytearray(), bytearray()
        for identity, payload in media.items():
            data.extend(bytes(-len(data)%16))
            table.extend(struct.pack('<III', identity, len(data), len(payload)))
            data.extend(payload)
        chunks[b'DIDX'], chunks[b'DATA'] = bytes(table), bytes(data)
    body = b''.join(struct.pack('<4sI', tag, len(payload))+payload for tag, payload in chunks.items())
    head = bytearray(native[:16])
    struct.pack_into('<I', head, 4, len(body))
    return bytes(head)+body


class Assets:
    def __init__(self, workspace: Path, files: list[dict[str, Any]], cache: Path) -> None:
        self.workspace = workspace
        self.cache = cache
        self.sources: dict[str, list[Path]] = defaultdict(list)
        self.verified: dict[str, Path] = {}
        self.directories: dict[str, list[tuple[int, ...]]] = {}
        registry = workspace/'dist/hd2-update-2026-10-25/resource-audit-v1/main-sources.json'
        if registry.is_file():
            for digest, row in read(registry).items():
                self.sources[digest].append(Path(row['path']))
        self.inventory_loaded = False

    def path(self, file: dict[str, Any]) -> Path:
        digest = file['sha256']
        if digest in self.verified:
            return self.verified[digest]
        paths = [self.cache/digest, self.workspace/'dist/hd2-update-2026-10-21/feed-vault'/digest, *self.sources[digest]]
        for path in paths:
            if path.is_file() and path.stat().st_size == file['size'] and pin(path)['sha256'] == digest:
                self.verified[digest] = path
                return path
        if not self.inventory_loaded:
            result = subprocess.run(['rg', '--files', '--hidden', '--no-ignore', 'dist'], cwd=self.workspace, capture_output=True, text=True, check=True)
            for name in result.stdout.splitlines():
                path = Path(name)
                if re.fullmatch(r'[a-f0-9]{64}', path.name):
                    self.sources[path.name].append(self.workspace/path)
            self.inventory_loaded = True
            return self.path(file)
        for path in self.sources[digest]:
            if path.is_file() and path.stat().st_size == file['size'] and pin(path)['sha256'] == digest:
                self.verified[digest] = path
                return path
        url = file['url']
        need(url.startswith('https://github.com/owendavidgoode/clonedivers/releases/download/'), 'Only public release assets may be fetched')
        self.cache.mkdir(parents=True, exist_ok=True)
        path = self.cache/digest
        LOG.info('Fetching missing verified public asset %s (%d bytes)', digest, file['size'])
        with urllib.request.urlopen(url, timeout=60) as source, path.open('wb') as target:
            while chunk := source.read(1024*1024):
                target.write(chunk)
        need(path.stat().st_size == file['size'] and pin(path)['sha256'] == digest, 'Downloaded public asset identity')
        self.verified[digest] = path
        return path

    def rows(self, digest: str, path: Path) -> list[tuple[int, ...]]:
        if digest not in self.directories:
            self.directories[digest] = directory(path.read_bytes())
        return self.directories[digest]

    def payload(self, file: dict[str, Any], row: tuple[int, ...], part: int, siblings: list[dict[str, Any]], limit: int | None = None) -> bytes:
        if not row[7+part]:
            return b''
        matches = [f for f in siblings if f['name'] == file['name']+SUFFIXES[part]] if part else [file]
        need(len(matches) == 1, 'Unique resource companion')
        size = min(row[7+part], limit) if limit else row[7+part]
        with self.path(matches[0]).open('rb') as handle:
            handle.seek(row[2+part])
            raw = handle.read(size)
        need(len(raw) == size, 'Complete selected resource')
        return raw


def build(args: argparse.Namespace) -> None:
    need(not args.out.exists(), 'Fresh output directory required')
    manifest = read(args.manifest)
    selection = read(args.selection)
    need(selection['manifestSha256'] == pin(args.manifest)['sha256'], 'Selection bound to source manifest')
    pins = read(args.native_index.parent/'report.json')
    index = read(args.native_index)
    need(pin(args.native_index) == pins['nativeIndex'], 'Sealed native index')
    for role in ('exe', 'gameDll', 'bundles'):
        need(pin(Path(pins[role]['path'])) == pins[role], 'Native build pin: '+role)
    acf = (args.game.parent.parent/'appmanifest_553850.acf').read_text()
    need(re.search(r'"buildid"\s+"'+pins['build']+'"', acf) is not None, 'Installed native build')
    native = N.NativeSlim(args.game/'data')
    N.lz4_block = lambda data, size: lz4.block.decompress(data, uncompressed_size=size)
    native.chunk = functools.lru_cache(maxsize=16)(native.chunk)
    assets = Assets(args.workspace, manifest['pack']['files'], args.out.parent/'source-cache')

    @functools.lru_cache(maxsize=1024)
    def stock(key: tuple[int, int], part: int = 0, limit: int | None = None) -> bytes:
        provider = index[keytext(key)][0]
        row = provider['row']
        size = min(row[7+part], limit) if limit else row[7+part]
        return native.read(provider['archive']+SUFFIXES[part], row[2+part], size)

    winners, containing = {}, defaultdict(list)
    for set_id, files in selection['fileSets'].items():
        resolved = {}
        for file in sorted((f for f in files if MAIN.fullmatch(f['name'])), key=lambda f: (f['name'][:16], int(f['name'].split('_')[-1]))):
            for row in assets.rows(file['sha256'], assets.path(file)):
                resolved[row[:2]] = file, row
            containing[file['sha256']].append(set_id)
        winners[set_id] = resolved

    def full_stream(media_id: int, context: str, bank_key: tuple[int, int], limit: int) -> bytes:
        values = []
        keys = [(murmur64(prefix+str(media_id)), STREAM) for prefix in ('content/audio/', 'content/audio/us/')]
        for set_id in sorted(set(containing[context])):
            selected = winners[set_id]
            if selected.get(bank_key, ({}, ()))[0].get('sha256') != context:
                continue
            owned = [key for key in keys if key in selected]
            present = [key for key in keys if keytext(key) in index]
            available = owned if owned else present
            if not available:
                values.append(b'')
                continue
            need(len(available) == 1, f'Unambiguous streamed media identity: {media_id}')
            key = available[0]
            if key in selected:
                file, row = selected[key]
                values.append(assets.payload(file, row, 1, selection['fileSets'][set_id], limit))
            else:
                values.append(stock(key, 1, limit))
        if not values:
            # This bank is shadowed in every supported selection; the current
            # native source is the compatibility control for this dormant layer.
            present = [key for key in keys if keytext(key) in index]
            return stock(present[0], 1, limit) if len(present) == 1 else b''
        need(len({sha(v) for v in values}) == 1, f'Context-dependent full media requires separate bank variant: {media_id}')
        return values[0]

    args.out.mkdir(parents=True)
    (args.out/'assets').mkdir()
    replacements, changes, excluded, excluded_hashes, registered = {}, [], set(), set(), {}
    stats = Counter()
    bank_evidence = []

    def store(raw: bytes) -> dict[str, Any]:
        path = args.out/'assets'/sha(raw)
        if not path.exists():
            path.write_bytes(raw)
        proof = pin(path)
        registered[proof['sha256']] = proof
        return proof

    seen = set()
    for file in manifest['pack']['files']:
        if not MAIN.fullmatch(file['name']) or file['sha256'] in seen:
            continue
        seen.add(file['sha256'])
        source_path = assets.path(file)
        raw = source_path.read_bytes()
        rows = directory(raw)
        if any(row[1] == LUA for row in rows):
            need(all(row[1] == LUA for row in rows), 'Mixed Lua archive needs separate conversion')
            excluded.add(file['name'])
            excluded_hashes.add(file['sha256'])
            continue
        updated = bytearray(raw)
        nt = struct.unpack_from('<I', raw, 4)[0]
        table = 72+32*nt
        details, migrations = [], {}
        for row_index, row in enumerate(rows):
            key = row[:2]
            if row[1] == 0x5ee65304478f8db5 and row[8]:
                siblings = [f for f in manifest['pack']['files'] if f['name'] == file['name']+'.stream' and all(f.get(k) == file.get(k) for k in ('modes', 'option', 'unlessOption', 'textureProfiles'))]
                need(len(siblings) == 1, 'Unique video companion')
                sibling = siblings[0]
                if row[3]+row[8] > sibling['size']:
                    with assets.path(sibling).open('rb') as handle:
                        head = handle.read(12)
                    need(row[3] == 0 and head[:3] in (b'BIK', b'KB2') and struct.unpack_from('<I', head, 4)[0]+8 == sibling['size'] and sum(bool(r[8]) for r in rows) == 1, 'Encoded Bink extent proof')
                    corrected = list(row)
                    corrected[8] = sibling['size']
                    ROW.pack_into(updated, table+80*row_index, *corrected)
                    details.append({'key': keytext(key), 'videoDeclaredStreamBefore': row[8], 'videoDeclaredStreamAfter': sibling['size'], 'payloadUnchanged': True})
                    stats['videoExtentsRepaired'] += 1
            if row[1] not in (BANK, DEP, STREAM, TEXT):
                continue
            old = raw[row[2]:row[2]+row[7]]
            if keytext(key) not in index:
                stats['privateResourcesRetained'] += 1
                continue
            fresh = stock(key)
            after, evidence = old, {}
            if row[1] == BANK:
                after, moved, evidence = B.rebase_bank(A, old, fresh)
                for media_id, wem in moved.items():
                    keys = [(murmur64(prefix+str(media_id)), STREAM) for prefix in ('content/audio/', 'content/audio/us/')]
                    keys = [k for k in keys if keytext(k) in index]
                    need(len(keys) == 1, 'Unique embedded-to-stream migration')
                    wrapper = stock(keys[0])
                    need(len(wrapper) == 12 and wem[:4] == b'RIFF' and wem[8:12] == b'WAVE' and struct.unpack_from('<I', wem, 4)[0]+8 == len(wem), 'Complete native wrapper and migrated WEM')
                    migrations[keys[0]] = wrapper[:8]+struct.pack('<I', len(wem)), wem, b''
                chunks = A.chunks(after)
                media = A.media(chunks)
                nh, oh = hierarchy(A.chunks(fresh).get(b'HIRC', bytes(4))), hierarchy(A.chunks(old).get(b'HIRC', bytes(4)))
                missing_events = sorted(k for k, v in nh.items() if v[0] == 4 and k not in oh)
                hirc, at, edits = bytearray(chunks.get(b'HIRC', bytes(4))), 4, []
                native_media = A.media(A.chunks(fresh))
                for _ in range(struct.unpack_from('<I', hirc)[0]):
                    kind, size, sound_id = struct.unpack_from('<BII', hirc, at)
                    if kind == 2:
                        plugin, mode, mid, cacheid, initial, _flags = struct.unpack_from('<IBIIIB', hirc, at+9)
                        embedded_key = cacheid if cacheid in media else mid
                        embedded = media.get(embedded_key, b'')
                        full = full_stream(mid, file['sha256'], key, max(8192, initial)) if mode in (1, 2) else embedded
                        if moved.get(mid) is not None:
                            full = moved[mid][:max(8192, initial)]
                        fmt = wem_format(full)
                        expected = {0x40001: {0xffff}, 0x10001: {1, 0xfffe}}.get(plugin)
                        if full and expected is not None and fmt not in expected:
                            actual_plugin = {0xffff: 0x40001, 1: 0x10001, 0xfffe: 0x10001}.get(fmt)
                            need(actual_plugin is not None, f'Unsupported effective codec: {mid}/{plugin:x}/{fmt}')
                            before = bytes(hirc[at+9:at+13])
                            struct.pack_into('<I', hirc, at+9, actual_plugin)
                            edits.append({'offset': at+9, 'sound': sound_id, 'media': mid, 'beforeHex': before.hex(), 'afterHex': bytes(hirc[at+9:at+13]).hex()})
                        if mode == 1 and embedded_key in native_media and full:
                            need(len(full) >= initial and len(native_media[embedded_key]) == initial, 'Native prefetch contract')
                            media[embedded_key] = full[:initial]
                        if not full:
                            need(nh[sound_id][1] == bytes(hirc[at+9:at+5+size]) and embedded == native_media.get(embedded_key, b''), 'Unresolved source must be native-exact')
                            stats['unchangedNativeUnresolvedSources'] += 1
                        else:
                            stats['sourceFormatsChecked'] += 1
                    at += 5+size
                if b'HIRC' in chunks:
                    chunks[b'HIRC'] = bytes(hirc)
                after = serialize_bank(fresh, chunks, media)
                reverse = bytearray(A.chunks(after).get(b'HIRC', bytes(4)))
                for edit in reversed(evidence['sourceCodecEdits']+edits):
                    reverse[edit['offset']:edit['offset']+4] = bytes.fromhex(edit['beforeHex'])
                need(bytes(reverse) == A.chunks(fresh).get(b'HIRC', bytes(4)), 'Exact current routing after declared codec reversal')
                evidence.update({'missingNativeEventsBefore': missing_events, 'externalSourceCodecEdits': edits, 'currentGraphExactExceptDeclaredCodecs': True})
                stats['restoredNativeEvents'] += len(missing_events)
                stats['banksRebased'] += 1
                bank_evidence.append({'key': keytext(key), 'sourceAsset': file['sha256'], **evidence})
            elif row[1] == DEP:
                after = fresh
                stats['dependenciesRebased'] += 1
            elif row[1] == STREAM:
                need(len(old) == len(fresh) == 12 and struct.unpack_from('<I', old, 8)[0] == row[8], 'Stream wrapper length matches selected WEM')
                after = fresh[:8]+old[8:]
                stats['streamWrappersRebased'] += 1
            else:
                old_native_path = args.workspace/f'dist/empire-boot-2026-10-04/labels/inventory-v1/{row[0]:016x}.us.strings.main'
                old_native, _ = T.parse_strings(old_native_path.read_bytes())
                authored, _ = T.parse_strings(old)
                current, _ = T.parse_strings(fresh)
                overrides = {k: v for k, v in authored.items() if old_native.get(k) != v}
                merged = current | overrides
                keys = sorted(merged)
                text = bytearray(fresh[:16])
                struct.pack_into('<I', text, 8, len(keys))
                text.extend(struct.pack(f'<{len(keys)}I', *keys))
                text.extend(bytes(4*len(keys)))
                for ti, identity in enumerate(keys):
                    struct.pack_into('<I', text, 16+4*len(keys)+4*ti, len(text))
                    text.extend(merged[identity].encode('utf-8')+b'\0')
                after = bytes(text)
                need(T.parse_strings(after)[0] == merged, 'Text merge exact')
                stats['textTablesRebased'] += 1
                stats['newTextKeysPreserved'] += len(current.keys()-authored.keys())
            if after != old:
                updated.extend(bytes(-len(updated)%max(16, row[10])))
                new_row = list(row)
                new_row[2], new_row[7] = len(updated), len(after)
                ROW.pack_into(updated, table+80*row_index, *new_row)
                updated.extend(after)
                details.append({'key': keytext(key), 'beforeSha256': sha(old), 'afterSha256': sha(after)})
        if migrations:
            siblings = [f for f in manifest['pack']['files'] if f['name'] == file['name']+'.stream' and all(f.get(k) == file.get(k) for k in ('modes', 'option', 'unlessOption', 'textureProfiles'))]
            need(len(siblings) == 1, 'Unique migrated STREAM companion')
            sibling = siblings[0]
            stream = bytearray(assets.path(sibling).read_bytes()) if sibling['size'] else bytearray()
            resources = [(list(r), bytes(updated[r[2]:r[2]+r[7]])) for r in directory(updated)]
            for key, parts in migrations.items():
                need(all(r[:2] != list(key) for r, _ in resources), 'Migrated STREAM not already overridden')
                stream.extend(bytes(-len(stream)%16))
                new_row = list(index[keytext(key)][0]['row'])
                new_row[2:7] = [0, len(stream), 0, 0, 0]
                new_row[7:10] = [len(parts[0]), len(parts[1]), 0]
                stream.extend(parts[1])
                resources.append((new_row, parts[0]))
            resources.sort(key=lambda item: (item[0][1], item[0][0]))
            types = sorted({r[1] for r, _ in resources})
            header = bytearray(raw[:72])
            struct.pack_into('<II', header, 4, len(types), len(resources))
            type_table = b''.join(struct.pack('<IIQIIII', 0, 0, kind, sum(r[1] == kind for r, _ in resources), 0, 16, 64) for kind in types)
            main = bytearray((72+len(type_table)+80*len(resources)+8+255)//256*256)
            main[:72], main[72:72+len(type_table)] = header, type_table
            for ordinal, (new_row, payload) in enumerate(resources):
                main.extend(bytes(-len(main)%max(16, new_row[10])))
                new_row[2], new_row[12] = len(main), ordinal
                ROW.pack_into(main, 72+len(type_table)+80*ordinal, *new_row)
                main.extend(payload)
            updated = main
            replacements[sibling['sha256']] = store(bytes(stream))
            stats['embeddedToStreamMigrations'] += len(migrations)
        if details:
            updated.extend(bytes(-len(updated)%16))
            changed_keys = {d['key'] for d in details}
            result_rows = {r[:2]: r for r in directory(updated)}
            for before in rows:
                after_row = result_rows[before[:2]]
                if keytext(before[:2]) not in changed_keys:
                    need(before[:2] == after_row[:2] and before[3:7] == after_row[3:7] and before[8:12] == after_row[8:12] and raw[before[2]:before[2]+before[7]] == updated[after_row[2]:after_row[2]+after_row[7]], 'Unrelated resource preserved byte-exact')
            need(P.inspect_directory(updated, len(updated))['passed'], 'Candidate MAIN structural preflight')
            proof = store(bytes(updated))
            replacements[file['sha256']] = proof
            changes.append({'source': pin(source_path), 'candidate': proof, 'logicalMain': file['name'], 'resourcesChanged': details})
        if len(seen)%40 == 0:
            LOG.info('Checked %d public MAIN variants', len(seen))

    candidate = copy.deepcopy(manifest)
    files = [f for f in candidate['pack']['files'] if not any(f['name'] == name+suffix for name in excluded for suffix in SUFFIXES)]
    for file in files:
        if file['sha256'] in replacements:
            proof = replacements[file['sha256']]
            file.update(sha256=proof['sha256'], size=proof['bytes'], url='https://github.com/owendavidgoode/clonedivers/releases/download/pack-'+args.version+'-files/'+proof['sha256'])
    candidate['pack'].update(files=files, version=args.version, gameBuild=pins['build'], gameDepots=re.findall(r'"manifest"\s+"(\d+)"', acf), totalSize=sum(f['size'] for f in files), notes='HD2 7.2.2 compatibility: current weapon audio events and text, with authored sounds retained.', statusNotes='Compatibility repair; unsupported Lua features excluded. Offline verification does not establish audible gameplay acceptance.')
    save(args.out/'manifest.json', candidate)
    save(args.out/'changes.json', changes)
    save(args.out/'banks.json', bank_evidence)
    save(args.out/'assets.json', {'assets': list(registered.values())})
    for role in ('exe', 'gameDll', 'bundles'):
        need(pin(Path(pins[role]['path'])) == pins[role], 'Native build stable after composition')
    report = {'passed': True, 'installed': False, 'published': False, 'runtimeAccepted': False, 'sourceManifest': pin(args.manifest), 'manifest': pin(args.out/'manifest.json'), 'nativeIndex': pin(args.native_index), 'nativePins': pins, 'counts': dict(stats), 'removedUnsupportedLuaGroups': sorted(excluded), 'changedAssets': len(replacements), 'allOtherResourceBytesExact': True, 'companionsUnchangedExceptDeclaredMigrations': True, 'manifestGatesPreserved': True}
    save(args.out/'report.json', report)
    print(json.dumps({k: v for k, v in report.items() if k != 'nativePins'}))


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, suggest_on_error=True)
    for name in ('workspace', 'game', 'manifest', 'selection', 'native-index', 'out'):
        parser.add_argument('--'+name, type=Path, required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('-v', '--verbose', action='store_true')
    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.DEBUG if args.verbose else logging.INFO, format='%(message)s')
    try:
        build(args)
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, struct.error, subprocess.CalledProcessError):
        LOG.exception('Compatibility staging failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
