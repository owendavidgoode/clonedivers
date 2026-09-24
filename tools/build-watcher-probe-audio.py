#!/usr/bin/env python3
"""Stage probe audio on one current Watcher loop; preserve all event routing.

This is an experimental routing candidate. The loop's exact audible use must be
confirmed in game. Guard Dog restoration is handled by excluding its old override.
"""

import argparse
import importlib.util
import json
import struct
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('watcher_rebase', ROOT / 'tools/rebase-player-audio.py')
A = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = A
spec.loader.exec_module(A)
WATCHER = 0xD8B809D8749C06C0
MEDIA = 591126644


def build(current: Path, mono: Path, output: Path) -> None:
    if output.exists():
        raise ValueError('Choose a fresh output directory')
    output.mkdir(parents=True)
    sys.path.insert(0, str(ROOT / 'dist/rc-upgrade/wwav'))
    from wwav import convert
    sys.path.insert(0, str(ROOT / 'dist/rc-upgrade/hd2-audio-modder'))
    from wwise_hierarchy_154 import WwiseHierarchy_154
    original = current.read_bytes()
    chunks = A.chunks(original)
    hierarchy = WwiseHierarchy_154()
    hierarchy.load(chunks[b'HIRC'])
    loop = hierarchy.entries[87182188]
    if loop.playListSetting.sLoopCount != 0 or loop.children.children != [851622742]:
        raise ValueError('Current single-source loop no longer matches audited routing')
    if hierarchy.entries[454711494].idExt != 87182188 or hierarchy.entries[884380033].idExt != 87182188:
        raise ValueError('Loop play/stop targets changed')
    wem_path = output / 'probe-mono.wem'
    convert(str(mono), str(wem_path), codec='pcm', quiet=True)
    wem = wem_path.read_bytes()
    if wem[:4] != b'RIFF' or struct.unpack_from('<H', wem, 22)[0] != 1:
        raise ValueError('Expected a mono PCM recording for spatial playback')
    old_media = A.media(chunks)
    if MEDIA not in old_media:
        raise ValueError('Watcher loop sample missing')
    new_media = old_media.copy()
    new_media[MEDIA] = wem
    didx, data = bytearray(), bytearray()
    for identity, sample in new_media.items():
        data.extend(bytes(-len(data) % 16))
        didx.extend(struct.pack('<III', identity, len(data), len(sample)))
        data.extend(sample)
    hirc = bytearray(chunks[b'HIRC'])
    position, changes = 4, []
    for _ in range(struct.unpack_from('<I', hirc)[0]):
        kind, length, identity = struct.unpack_from('<BII', hirc, position)
        if kind == 2 and struct.unpack_from('<I', hirc, position+14)[0] == MEDIA:
            if struct.unpack_from('<IB', hirc, position+9) != (0x40001, 0):
                raise ValueError('Watcher source is not embedded Vorbis')
            for offset, value in ((position+9, 0x10001), (position+22, len(wem))):
                changes.append({'offset': offset, 'old': struct.unpack_from('<I', hirc, offset)[0], 'new': value})
                struct.pack_into('<I', hirc, offset, value)
        position += 5+length
    if len(changes) != 2:
        raise ValueError('Expected exactly one Sound source to edit')
    restored = bytearray(hirc)
    for change in changes:
        struct.pack_into('<I', restored, change['offset'], change['old'])
    if bytes(restored) != chunks[b'HIRC']:
        raise ValueError('Unrelated event/routing/property bytes changed')
    edited = {**chunks, b'DIDX': bytes(didx), b'DATA': bytes(data), b'HIRC': bytes(hirc)}
    body = b''.join(struct.pack('<4sI', tag, len(value))+value for tag, value in edited.items())
    wrapper = bytearray(original[:16])
    struct.pack_into('<I', wrapper, 4, len(body))
    bank = bytes(wrapper)+body
    if A.media(A.chunks(bank)) != new_media:
        raise ValueError('Watcher audio serialization failed')
    source = ROOT / 'dist/mods/recovered - RiqCrow - Probe droid Guard Dog.zip'
    with zipfile.ZipFile(source) as archive:
        name = next(name for name in archive.namelist() if name.endswith('.patch_176') or ('.patch_' in name and name.rsplit('_', 1)[-1].isdigit()))
        blobs, resources = A.V.read_zip_bundle(archive, name)
    bank_row = next(value for key, value in resources.items() if key[1] == A.BANK)
    dep_row = next(value for key, value in resources.items() if key[1] == A.DEP)
    dep = current.with_name(current.name.replace('.wwise_bank.main', '.wwise_dep.main')).read_bytes()
    output_resources = {(WATCHER, A.BANK): A.payload(bank_row, bank), (WATCHER, A.DEP): A.payload(dep_row, dep)}
    files = A.V.write_bundle(output / '9ba626afa44a3aa3.patch_0', blobs[0], output_resources)
    report = {'currentBankSha256': A.V.sha(original), 'monoPcmSha256': A.V.sha(mono.read_bytes()),
              'donorZipSha256': A.V.sha(source.read_bytes()), 'bank': f'{WATCHER:016x}',
              'targetMedia': MEDIA, 'sound': 851622742, 'loopContainer': 87182188,
              'playEvent': 1790836314, 'stopEvent': 2157255410,
              'hircEdits': changes, 'otherEmbeddedSamplesUnchanged': len(old_media)-1,
              'files': files, 'runtimeTested': False,
              'routingEvidence': 'One-source infinite-loop container, existing play/stop actions preserved. Exact in-game cue identity remains to be tested.',
              'guardDog': 'Exclude logical bundle 177; current rebased PEW-PEW Guard Dog bank 147 wins.'}
    (output / 'report.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print(f'Built Watcher loop candidate; {len(old_media)-1} other embedded samples and all event routing retained.')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--current', type=Path, required=True)
    parser.add_argument('--mono', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    build(args.current, args.mono, args.out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
