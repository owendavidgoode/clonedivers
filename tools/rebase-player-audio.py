#!/usr/bin/env python3
"""Stage current-game sound banks while retaining replacement audio samples.

Keeps current event/routing/property bytes, replaces embedded media by stable
media ID, and retains existing external streams. Does not install or publish.
"""

import argparse
import copy
import importlib.util
import json
import logging
import re
import struct
import sys
import zipfile
from pathlib import Path
from types import ModuleType

ROOT = Path(__file__).resolve().parents[1]
BANK = 0x535A7BD3E650D799
DEP = 0xAF32095C82F2B070
STREAM = 0x504B55235D21440E
LOG = logging.getLogger(__name__)


def module(name: str, path: Path) -> ModuleType:
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


V = module('audio_archive', ROOT / 'tools/build-player-visuals.py')
RC = module('rc_bank', ROOT / 'tools/build-rc-voices.py')


def chunks(bank: bytes) -> dict[bytes, bytes]:
    if bank[16:20] != b'BKHD' or struct.unpack_from('<I', bank, 4)[0] != len(bank)-16:
        raise ValueError('Invalid bank wrapper')
    result = {}
    position = 16
    while position < len(bank):
        tag, size = struct.unpack_from('<4sI', bank, position)
        end = position + 8 + size
        if tag in result or end > len(bank):
            raise ValueError('Duplicate or truncated bank chunk')
        result[tag] = bank[position+8:end]
        position = end
    if position != len(bank) or struct.unpack_from('<I', result[b'BKHD'])[0] ^ 0x9211BCAC != 154:
        raise ValueError('Unsupported bank version')
    return result


def sounds(hirc: bytes) -> dict[int, tuple[int, bytes]]:
    result = {}
    position = 4
    for _ in range(struct.unpack_from('<I', hirc)[0]):
        kind, size, identity = struct.unpack_from('<BII', hirc, position)
        end = position + 5 + size
        if size < 4 or end > len(hirc):
            raise ValueError('Truncated hierarchy object')
        if kind == 2:
            payload = hirc[position+9:end]
            media = struct.unpack_from('<I', payload, 5)[0]
            # Multiple Sounds may share media; their source settings must agree.
            if media in result and result[media][1][:18] != payload[:18]:
                raise ValueError(f'Inconsistent shared media source: {media}')
            result[media] = identity, payload
        position = end
    if position != len(hirc):
        raise ValueError('Hierarchy count mismatch')
    return result


def media(chunks_: dict[bytes, bytes]) -> dict[int, bytes]:
    result = {}
    if b'DIDX' not in chunks_:
        return result
    for identity, offset, size in struct.iter_unpack('<III', chunks_[b'DIDX']):
        payload = chunks_[b'DATA'][offset:offset+size]
        if len(payload) != size or (identity in result and result[identity] != payload):
            raise ValueError(f'Invalid or conflicting media index: {identity}')
        result[identity] = payload
    return result


def rebase(old: bytes, current: bytes) -> tuple[bytes, dict[int, bytes], dict]:
    old_chunks, current_chunks = chunks(old), chunks(current)
    old_media, current_media = media(old_chunks), media(current_chunks)
    old_sounds, current_sounds = sounds(old_chunks[b'HIRC']), sounds(current_chunks[b'HIRC'])
    external = {}
    for identity in old_sounds.keys() & current_sounds.keys():
        old_source, current_source = old_sounds[identity][1], current_sounds[identity][1]
        old_codec, old_stream = struct.unpack_from('<IB', old_source)
        codec, stream = struct.unpack_from('<IB', current_source)
        if old_codec != codec:
            raise ValueError(f'Codec migration requires review: {identity}')
        if old_stream != stream:
            if (old_stream, stream, codec) != (0, 2, 0x40001) or identity not in old_media:
                raise ValueError(f'Unsupported stream migration: {identity}/{old_stream}/{stream}')
            external[identity] = old_media[identity]
    merged = {identity: old_media.get(identity, payload) for identity, payload in current_media.items()}
    updated = current_chunks.copy()
    if merged:
        table, audio = bytearray(), bytearray()
        for identity, payload in merged.items():
            audio.extend(bytes(-len(audio) % 16))
            table.extend(struct.pack('<III', identity, len(audio), len(payload)))
            audio.extend(payload)
        updated[b'DIDX'], updated[b'DATA'] = bytes(table), bytes(audio)
    body = b''.join(struct.pack('<4sI', tag, len(payload)) + payload for tag, payload in updated.items())
    wrapper = bytearray(current[:16])
    struct.pack_into('<I', wrapper, 4, len(body))
    result = bytes(wrapper) + body
    check = chunks(result)
    if media(check) != merged or any(check[tag] != value for tag, value in current_chunks.items() if tag not in (b'DIDX', b'DATA')):
        raise ValueError('Rebase changed routing/properties or lost intended audio')
    report = {'currentSha256': V.sha(current), 'oldSha256': V.sha(old), 'rebasedSha256': V.sha(result),
              'hircSha256': V.sha(check[b'HIRC']), 'currentHierarchyPreserved': True,
              'oldEmbeddedSamplesRetained': len(old_media.keys() & merged.keys()),
              'newEmbeddedSamplesPreserved': len(merged.keys() - old_media.keys()),
              'changedEmbeddedSamples': sum(merged[key] != current_media[key] for key in merged),
              'embeddedMovedToStream': sorted(external),
              'obsoleteMediaNotCarried': sorted(old_media.keys() - merged.keys() - external.keys()),
              'obsoleteSoundSources': sorted(old_sounds.keys() - current_sounds.keys())}
    return result, external, report


def payload(resource: object, data: bytes) -> object:
    result = copy.deepcopy(resource)
    result.payloads = (data, b'', b'')
    result.row[7:10] = [len(data), 0, 0]
    return result


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def build(current_index: Path, output: Path) -> None:
    if output.exists():
        raise ValueError('Choose a fresh output directory')
    output.mkdir(parents=True)
    index = json.loads(current_index.read_text(encoding='utf-8'))
    current = {int(key, 16): ROOT / path for key, path in index.items()}
    ownership = json.loads((ROOT / 'build-inputs/r8/deploy-report.json').read_text(encoding='utf-8'))
    reports, bundles, generic_banks = [], [], {}
    sys.path.insert(0, str(ROOT / 'dist/rc-upgrade/hd2-audio-modder'))
    from util import murmur64_hash
    selections = [('PEW-PEW Republic', None), ('Full Clone Voice Conversion', None),
                  ('RiqCrow - Republic cruiser + LAAT audio', {0x0914072dbec9d227}),
                  ('RiqCrow - Probe droid Guard Dog', {0xd1549e61c0ba6864}),
                  ('AT-TE exosuits + LAAT/c', {0x9f67023d6191941c})]
    for label, selected_banks in selections:
        archive_path = ROOT / 'dist/mods' / f'recovered - {label.replace("/", "_")}.zip'
        archive_sha = V.sha(archive_path.read_bytes())
        with zipfile.ZipFile(archive_path) as archive:
            for name in archive.namelist():
                if not re.search(r'\.patch_\d+$', name):
                    continue
                # Read the directory before large companions; only rebase the
                # known later winners of the audited weapon banks.
                main = archive.read(name)
                entries = RC.archive_entries(main)
                if selected_banks is not None and not any(key[1] == BANK and key[0] in selected_banks for key in entries):
                    continue
                folder = Path(name).parent.as_posix()
                folder = '' if folder == '.' else folder
                owners = [row for row in ownership if row['mod'] == label and row['folder'] == folder]
                if len(owners) != 1:
                    raise ValueError(f'Ownership ambiguity: {name}')
                target_name = owners[0]['file']
                data, original = V.read_zip_bundle(archive, name)
                resources = copy.deepcopy(original)
                for key, resource in original.items():
                    if key[1] != BANK or (selected_banks is not None and key[0] not in selected_banks):
                        continue
                    current_path = current[key[0]]
                    rebased, external, report = rebase(resource.payloads[0], current_path.read_bytes())
                    resources[key] = payload(resource, rebased)
                    dep_key = key[0], DEP
                    dep_path = current_path.with_name(current_path.name.replace('.wwise_bank.main', '.wwise_dep.main'))
                    resources[dep_key] = payload(resources[dep_key], dep_path.read_bytes())
                    for identity, audio in external.items():
                        stream_id = murmur64_hash(f'content/audio/us/{identity}'.encode())
                        stream_key = stream_id, STREAM
                        template = next(value for item, value in original.items() if item[1] == STREAM)
                        replacement = copy.deepcopy(template)
                        replacement.row[:2] = stream_key
                        replacement.row[7:10] = [12, len(audio), 0]
                        replacement.payloads = (bytes.fromhex('D82F767800000000') + struct.pack('<I', len(audio)), audio, b'')
                        resources[stream_key] = replacement
                    report.update(bank=f'{key[0]:016x}', name=current_path.stem, mod=label, patch=target_name)
                    reports.append(report)
                    if label == 'Full Clone Voice Conversion':
                        generic_banks[key] = resources[key]
                        generic_banks[dep_key] = resources[dep_key]
                # Every pre-existing stream and unrelated resource remains unchanged,
                # except a specifically documented embedded-to-stream migration.
                for key, resource in original.items():
                    if key[1] not in (BANK, DEP) and resources[key].payloads != resource.payloads:
                        raise ValueError(f'Unrelated resource modified: {name}/{key}')
                files = V.write_bundle(output / 'base' / target_name, data[0], resources)
                bundles.append({'name': target_name, 'source': name, 'sourceZipSha256': archive_sha, 'files': files})
    expansion = ROOT / 'dist/rc-voice-expansion-2026-09-23/patch/9ba626afa44a3aa3.patch_0'
    data = tuple(expansion.with_name(expansion.name + suffix).read_bytes() if expansion.with_name(expansion.name + suffix).exists() else b'' for suffix in V.SUFFIXES)
    original = V.read_bundle(data, str(expansion))
    resources = copy.deepcopy(original)
    mapping_path = ROOT / 'dist/rc-voice-expansion-2026-09-23/mapping.json'
    mapping = json.loads(mapping_path.read_text(encoding='utf-8'))
    rc_reports = []
    for slot in mapping['slots']:
        identity = RC.BANK_IDS[int(slot['slot'])]
        key = identity, BANK
        current_bank = generic_banks[key].payloads[0]
        targets = {int(row['hd2_media_id']) for row in slot['rows']}
        edited, changes = RC.patch_bank(current_bank[16:], targets)
        resources[key] = payload(generic_banks[key], current_bank[:16] + edited)
        resources[identity, DEP] = generic_banks[identity, DEP]
        rc_reports.append({'character': slot['character'], 'mediaCount': len(targets), 'changes': changes,
                           'currentBankSha256': V.sha(current_bank), 'editedBankSha256': V.sha(resources[key].payloads[0])})
    if any(resources[key].payloads != value.payloads for key, value in original.items() if key[1] == STREAM):
        raise ValueError('RC recording payload changed')
    rc_files = V.write_bundle(output / 'rc/9ba626afa44a3aa3.patch_0', data[0], resources)
    write_json(output / 'report.json', {'gameBuild': '25327279', 'runtimeTested': False, 'published': False,
               'banks': reports, 'bundles': bundles, 'rc': rc_reports, 'rcFiles': rc_files,
               'mappingSha256': V.sha(mapping_path.read_bytes()), 'samplePolicy': 'Retain matching old embedded media and all existing streams; preserve current-only media, routing, timing and volume properties. Removed source IDs are not reintroduced.'})
    LOG.info('Rebased %s source banks in %s bundles plus all 4 Delta banks; RC samples unchanged.', len(reports), len(bundles))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--current-index', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    try:
        build(args.current_index, args.out)
        return 0
    except (OSError, ValueError, KeyError, struct.error):
        LOG.exception('Audio rebase failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
