#!/usr/bin/env -S uv run
# /// script
# requires-python = ">=3.14"
# dependencies = ["lz4>=4.4"]
# ///
"""Verify serialized compatibility assets against native data in all selections."""

import argparse
import functools
import importlib
import json
import logging
import struct
import sys
from collections import Counter
from pathlib import Path

import lz4.block

U = importlib.import_module('update-hd2-compatibility')
LOG = logging.getLogger(__name__)


def verify(args: argparse.Namespace) -> None:
    U.need(not args.out.exists(), 'Fresh verification output')
    manifest = U.read(args.stage/'manifest.json')
    selection = U.read(args.stage/'production96.json')
    U.need(selection['manifestSha256'] == U.pin(args.stage/'manifest.json')['sha256'], 'Production selector binds serialized manifest')
    U.need(selection['requestedStates'] == 96, 'Every requested production selection covered')
    build = U.read(args.stage/'report.json')
    index = U.read(Path(build['nativeIndex']['path']))
    U.need(U.pin(Path(build['nativeIndex']['path'])) == build['nativeIndex'], 'Native index unchanged')
    for role in ('exe', 'gameDll', 'bundles'):
        U.need(U.pin(Path(build['nativePins'][role]['path'])) == build['nativePins'][role], 'Current native build stable')
    assets = U.Assets(args.workspace, manifest['pack']['files'], args.stage.parent/'source-cache')
    for proof in U.read(args.stage/'assets.json')['assets']:
        U.need(U.pin(Path(proof['path'])) == proof, 'Fresh serialized asset hash')
        assets.sources[proof['sha256']].insert(0, Path(proof['path']))
    native = U.N.NativeSlim(args.game/'data')
    U.N.lz4_block = lambda data, size: lz4.block.decompress(data, uncompressed_size=size)
    native.chunk = functools.lru_cache(maxsize=12)(native.chunk)

    @functools.lru_cache(maxsize=24000)
    def stock(key: tuple[int, int], part: int = 0, limit: int | None = None) -> bytes:
        provider = index[U.keytext(key)][0]
        row = provider['row']
        return native.read(provider['archive']+U.SUFFIXES[part], row[2+part], min(row[7+part], limit) if limit else row[7+part])

    @functools.lru_cache(maxsize=24000)
    def prefix(digest: str, path: Path, offset: int, size: int) -> bytes:
        with path.open('rb') as handle:
            handle.seek(offset)
            value = handle.read(size)
        U.need(len(value) == size, 'Complete selected source prefix')
        return value

    changed = {p['candidate']['sha256']: p for p in U.read(args.stage/'changes.json')}
    counts, issues, cases = Counter(), [], []
    for set_id, files in selection['fileSets'].items():
        by_name = {f['name']: f for f in files}
        U.need(len(by_name) == len(files), 'No selected duplicate file names')
        winners = {}
        for file in sorted((f for f in files if U.MAIN.fullmatch(f['name'])), key=lambda f: (f['name'][:16], int(f['name'].split('_')[-1]))):
            path = assets.path(file)
            sizes = tuple(by_name.get(file['name']+suffix, {}).get('size', 0) for suffix in ('.stream', '.gpu_resources'))
            check = U.P.inspect_path(path, sizes)
            U.need(check['passed'], f'All-layer archive extent/type preflight: {file["name"]}: {check["issues"]}')
            for row in assets.rows(file['sha256'], path):
                U.need(row[1] != U.LUA, 'No unsupported Lua resources')
                winners[row[:2]] = file, row
            counts['archiveContexts'] += 1

        def selected(key: tuple[int, int], part: int = 0, limit: int | None = None, *, resolved: dict = winners, companions: dict = by_name) -> bytes:
            file, row = resolved[key]
            companion = companions[file['name']+U.SUFFIXES[part]]
            return prefix(companion['sha256'], assets.path(companion), row[2+part], min(row[7+part], limit) if limit else row[7+part])

        for key, (file, row) in winners.items():
            if U.keytext(key) not in index:
                counts['privateResources'] += 1
                continue
            if row[1] == U.DEP:
                U.need(selected(key) == stock(key), 'Selected dependency current-native exact')
                counts['dependencies'] += 1
            elif row[1] == U.STREAM:
                main = selected(key)
                U.need(len(main) == 12 and main[:8] == stock(key)[:8] and struct.unpack_from('<I', main, 8)[0] == row[8], 'Selected stream header/length current')
                counts['streamWrappers'] += 1
            elif row[1] == U.TEXT:
                actual = U.T.parse_strings(selected(key))[0]
                current = U.T.parse_strings(stock(key))[0]
                U.need(current.keys() <= actual.keys(), 'All new native text entries present')
                counts['textTables'] += 1
            elif row[1] == U.BANK:
                actual, current = U.A.chunks(selected(key)), U.A.chunks(stock(key))
                ah, nh = U.hierarchy(actual.get(b'HIRC', bytes(4))), U.hierarchy(current.get(b'HIRC', bytes(4)))
                am, nm = U.A.media(actual), U.A.media(current)
                if ah.keys() != nh.keys():
                    issues.append({'set': set_id, 'key': U.keytext(key), 'fault': 'event or hierarchy object coverage'})
                    continue
                for tag, payload in current.items():
                    if tag not in (b'HIRC', b'DIDX', b'DATA'):
                        U.need(actual[tag] == payload, 'Current native bank metadata exact')
                for identity, (kind, raw) in ah.items():
                    nk, nr = nh[identity]
                    U.need(kind == nk and (raw[4:] == nr[4:] if kind == 2 else raw == nr), 'Current native routing/properties exact outside declared source plugin')
                    if kind != 2:
                        continue
                    plugin, mode, mid, cacheid, initial, _flags = struct.unpack_from('<IBIIIB', raw)
                    embedded = am.get(cacheid, am.get(mid))
                    full = embedded or b''
                    full_key = None
                    if mode in (1, 2):
                        candidates = [(U.murmur64(p+str(mid)), U.STREAM) for p in ('content/audio/', 'content/audio/us/')]
                        owned = [k for k in candidates if k in winners]
                        available = owned or [k for k in candidates if U.keytext(k) in index]
                        U.need(len(available) <= 1, 'Unambiguous selected media identity')
                        full_key = available[0] if available else None
                        limit = max(8192, initial, len(embedded or b''))
                        full = (selected(full_key, 1, limit) if full_key in winners else stock(full_key, 1, limit)) if full_key else b''
                    expected = {0x40001: {0xffff}, 0x10001: {1, 0xfffe}}.get(plugin)
                    if full and expected and U.wem_format(full) not in expected:
                        issues.append({'set': set_id, 'key': U.keytext(key), 'sound': identity, 'media': mid, 'fault': 'codec/WEM mismatch'})
                    if full and mode == 1 and embedded is not None and embedded != full[:len(embedded)]:
                        inherited = raw == nr and embedded == nm.get(cacheid, nm.get(mid)) and full_key not in winners
                        if not inherited:
                            issues.append({'set': set_id, 'key': U.keytext(key), 'media': mid, 'fault': 'prefetch/full stream mismatch'})
                        else:
                            counts['nativeInheritedPrefetchBoundaries'] += 1
                    if not full:
                        U.need(raw == nr and embedded == nm.get(cacheid, nm.get(mid)), 'Unresolved sources inherited unchanged from native')
                        counts['nativeUnresolvedSources'] += 1
                    else:
                        counts['availableSources'] += 1
                        counts['qualifiedPcmOrVorbisSources' if expected else 'otherNativePluginSources'] += 1
                if file['sha256'] in changed:
                    original = Path(changed[file['sha256']]['source']['path'])
                    old_raw = original.read_bytes()
                    old_row = next(r for r in U.directory(old_raw) if r[:2] == key)
                    old_media = U.A.media(U.A.chunks(old_raw[old_row[2]:old_row[2]+old_row[7]]))
                    for identity, (kind, raw) in ah.items():
                        if kind == 2 and raw[4] == 0:
                            mid = struct.unpack_from('<I', raw, 5)[0]
                            if mid in old_media and mid in am:
                                U.need(am[mid] == old_media[mid], 'Authored fully embedded media retained byte-exact')
                                counts['authoredEmbeddedSamplesRetained'] += 1
                counts['bankContexts'] += 1
        LOG.info('Verified selected file set %d/%d', len(cases)+1, len(selection['fileSets']))
        cases.append({'fileSet': set_id, 'selectedFiles': len(files), 'winnerResources': len(winners)})
    for role in ('exe', 'gameDll', 'bundles'):
        U.need(U.pin(Path(build['nativePins'][role]['path'])) == build['nativePins'][role], 'Native build stable after verification')
    args.out.mkdir(parents=True)
    report = {'passed': not issues, 'manifest': U.pin(args.stage/'manifest.json'), 'productionSelection': U.pin(args.stage/'production96.json'), 'states': 96, 'fileSets': cases, 'counts': dict(counts), 'issues': issues, 'runtimeAccepted': False, 'installed': False, 'published': False}
    U.save(args.out/'report.json', report)
    print(json.dumps({k: v for k, v in report.items() if k != 'fileSets'}))
    U.need(not issues, 'Selected output verification has faults; report retained')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, suggest_on_error=True)
    for name in ('workspace', 'game', 'stage', 'out'):
        parser.add_argument('--'+name, type=Path, required=True)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    try:
        verify(args)
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, struct.error):
        LOG.exception('Serialized compatibility verification failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
