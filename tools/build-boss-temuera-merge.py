#!/usr/bin/env python3
"""Stage Boss (voice 4) with Temuera Morrison clone lines filling his non-RC calls.

Boss's RC recordings are Temuera Morrison. His voice slot (helldiver_purist) has 785
Sounds; the RC map covers 301. Every other Sound plays whichever bundle last supplies its
stream `content/audio/us/<media>`: the Full Clone Voice Conversion (Battlefront clone
lines, or silence for lines it could not match). The Temuera Morrison Voice Overhaul
(already in the pack, loaded before Full Clone Voice so it only wins DO/Mission Control)
has real Temuera recordings for many of those Sounds.

This adds Temuera's recording for each non-RC Boss Sound where Temuera has a line, so
Boss stays Temuera throughout. Where Temuera has only silence, the current clone line (or
silence) stays. Boss's bank, his 301 RC recordings, and Sev/Fixer/Scorch are unchanged:
the r10 Delta bundle bytes are kept and only stream resources are added. The rebased banks
keep the current game's hierarchy, so the added streams need the bank's codec (Vorbis),
which is asserted.

Outputs (fresh folder): `rc/` ship form (replaces the Delta bundle, manifest patch_251) and
`overlay/` (only the added streams, for an additive local test after the Delta bundle).
Never installs or publishes.
"""

import argparse
import hashlib
import importlib.util
import json
import logging
import struct
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GAME = Path(r'C:\Program Files (x86)\Steam\steamapps\common\Helldivers 2\data')
ARCHIVE = '9ba626afa44a3aa3'
BANK, DEP, STREAM = 0x535A7BD3E650D799, 0xAF32095C82F2B070, 0x504B55235D21440E
VORBIS = 0x40001
R10_RC = {'': '8a507b3514914bb71210179070e4bff5a032deb47551f63256a0aafba6c95eb5',
          '.stream': '3328f9e1b4dcd2e76cdda23b5da4833ccce1a9d9bffa691e134b79854521d7a4',
          '.gpu_resources': 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'}
# Installed r10 bundles (hash-checked below): Temuera Morrison VO, then Full Clone Voice.
TEMUERA, CLONE = f'{ARCHIVE}.patch_159', f'{ARCHIVE}.patch_160'
SILENCE_REPEATS = 100  # a payload reused this often across a mod is its silence placeholder
LOG = logging.getLogger(__name__)


def module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


V = module('audio_archive', ROOT / 'tools/build-player-visuals.py')
RC = module('rc_bank', ROOT / 'tools/build-rc-voices.py')
RB = module('rebase_audio', ROOT / 'tools/rebase-player-audio.py')


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def murmur64(text: str) -> int:
    m, mask = 0xc6a4a7935bd1e995, (1 << 64) - 1
    data = text.encode()
    h = (len(data) * m) & mask
    for i in range(0, len(data) - len(data) % 8, 8):
        k = int.from_bytes(data[i:i + 8], 'little') * m & mask
        k ^= k >> 47
        h = (h ^ (k * m & mask)) * m & mask
    if tail := data[len(data) - len(data) % 8:]:
        h = (h ^ int.from_bytes(tail, 'little')) * m & mask
    h = (h ^ (h >> 47)) * m & mask
    return h ^ (h >> 47)


def stream_key(media: int) -> tuple[int, int]:
    return murmur64(f'content/audio/us/{media}'), STREAM


def load(name: str) -> tuple[tuple[bytes, bytes, bytes], dict]:
    path = GAME / name
    data = tuple(Path(str(path) + s).read_bytes() if Path(str(path) + s).exists() else b'' for s in V.SUFFIXES)
    return data, V.read_bundle(data, str(path))


def silence(bundle: dict) -> set[str]:
    counts = Counter(sha(r.payloads[1]) for k, r in bundle.items() if k[1] == STREAM)
    return {digest for digest, n in counts.items() if n >= SILENCE_REPEATS}


def build(output: Path, rebase_report: Path, manifest: Path) -> None:
    if output.exists():
        raise ValueError('Choose a fresh output directory')
    pack = json.loads(manifest.read_text(encoding='utf-8'))['pack']
    shipped = {f['sha256'] for f in pack['files']}
    rc_name = next(p.name for p in GAME.glob(f'{ARCHIVE}.patch_*')
                   if p.suffix not in ('.stream', '.gpu_resources') and p.stat().st_size == 861312 and sha(p.read_bytes()) == R10_RC[''])
    rc_data, rc = load(rc_name)
    for suffix, blob in zip(V.SUFFIXES, rc_data):
        if sha(blob) != R10_RC[suffix]:
            raise ValueError(f'Installed Delta bundle {suffix or "main"} differs from r10')
    (_, temuera), (_, clone) = load(TEMUERA), load(CLONE)
    for name in (TEMUERA, CLONE):
        if sha((GAME / name).read_bytes()) not in shipped:
            raise ValueError(f'{name} is not an r10 pack file')

    report = json.loads(rebase_report.read_text(encoding='utf-8'))
    if report['rcFiles'][0]['sha256'] != R10_RC['']:
        raise ValueError('Rebase report does not describe the r10 Delta bundle')
    boss_mapped = {c['media_id'] for c in next(s for s in report['rc'] if s['character'] == 'Boss')['changes']}
    boss_bank = RC.BANK_IDS[4]
    assert RC.CHARACTERS[4] == 'Boss'
    sounds = RB.sounds(RB.chunks(rc[boss_bank, BANK].payloads[0])[b'HIRC'])
    if not boss_mapped <= sounds.keys():
        raise ValueError('RC Boss mapping is not in the Boss bank')

    quiet_t, quiet_c = silence(temuera), silence(clone)
    added, tally, rows = {}, Counter(), []
    for media, (_, source) in sorted(sounds.items()):
        if media in boss_mapped:
            tally['rc'] += 1
            continue
        key = stream_key(media)
        if key in rc:
            raise ValueError(f'Unmapped Boss media {media} is already in the Delta bundle')
        t, c = temuera.get(key), clone.get(key)
        t_line = t is not None and sha(t.payloads[1]) not in quiet_t
        c_state = 'none' if c is None else 'silent' if sha(c.payloads[1]) in quiet_c else 'line'
        if not t_line:
            tally[f'kept clone {c_state}'] += 1
            continue
        codec = struct.unpack_from('<I', source)[0]
        if codec != VORBIS or struct.unpack_from('<H', t.payloads[1], 20)[0] != 0xFFFF:
            raise ValueError(f'Temuera recording for {media} does not match the bank codec')
        added[key] = t
        tally['temuera (clone was ' + c_state + ')'] += 1
        rows.append({'media': media, 'replaced': c_state, 'temueraSha256': sha(t.payloads[1])})
    if sum(tally.values()) != len(sounds):
        raise ValueError('Boss Sounds were not all classified')

    merged = {**rc, **added}
    files = V.write_bundle(output / 'rc' / f'{ARCHIVE}.patch_0', rc_data[0], merged)
    overlay = V.write_bundle(output / 'overlay' / f'{ARCHIVE}.patch_0', rc_data[0], added)
    check = V.read_bundle(tuple((output / 'rc' / f'{ARCHIVE}.patch_0{s}').read_bytes() for s in V.SUFFIXES), 'check')
    if any(check[k].payloads != v.payloads for k, v in rc.items()):
        raise ValueError('An r10 Delta resource changed')
    result = {'purpose': 'Boss keeps his RC lines; Temuera Morrison lines fill his other calls',
              'bossSounds': len(sounds), 'tally': dict(tally), 'addedStreams': len(added),
              'distinctTemueraRecordings': len({r['temueraSha256'] for r in rows}),
              'sources': {'delta': rc_name, 'temuera': TEMUERA, 'clone': CLONE}, 'files': files, 'overlay': overlay,
              'added': rows, 'installed': False, 'published': False, 'gameplayVerified': False}
    (output / 'report.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    LOG.info('Boss: %s', ', '.join(f'{k} {v}' for k, v in tally.items()))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--rebase-report', type=Path, default=ROOT / 'dist/player-update-2026-09-23/audio-7.1-v4/report.json')
    parser.add_argument('--manifest', type=Path, default=ROOT / 'manifest-v3.json')
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    try:
        build(args.out, args.rebase_report, args.manifest)
        return 0
    except (OSError, ValueError, KeyError, StopIteration, struct.error):
        LOG.exception('Boss/Temuera merge failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
