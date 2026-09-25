#!/usr/bin/env python3
"""Assemble the pack 2026.09.24-r11 release inputs from the published r10 feed.

Changes against r10 (everything else keeps its r10 name, bytes and URL):
- manifest patch_249/patch_250: Venator fleet opening (video + separate English soundtrack),
  replacing the Republic Commando opening in place.
- manifest patch_251: Delta voice bundle with Temuera Morrison lines filling Boss's non-RC calls.
- manifest patch_318 (new, last, option droids): Spider Droid eye/vent aim markers.

Writes a fresh folder with the candidate manifest (no app block; release.ps1 adds it),
hash-named new assets, hard-linked per-profile source folders and directories.json for
`release.ps1 -ProfileDirectories`. Never installs or publishes.
"""

import argparse
import copy
import hashlib
import json
import os
import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ARCHIVE = '9ba626afa44a3aa3'
VERSION = '2026.09.24-r11'
URL = f'https://github.com/owendavidgoode/clonedivers/releases/download/pack-{VERSION}-files/'
R10_PROFILES = ROOT / 'dist/release-1.6.0-inputs/profiles-verified'
PATCH = re.compile(r'^(?P<arch>.+?)\.patch_(?P<idx>\d+)(?P<comp>\.(?:gpu_resources|stream))?$')

# manifest name -> (expected r10 sha256, replacement source file)
REPLACE = {
    f'{ARCHIVE}.patch_249': ('d54d14de3fbb', 'dist/venator-intro-2026-09-24/video/9ba626afa44a3aa3.patch_0'),
    f'{ARCHIVE}.patch_249.stream': ('3940d47b886d', 'dist/venator-intro-2026-09-24/video/9ba626afa44a3aa3.patch_0.stream'),
    f'{ARCHIVE}.patch_250': ('ffe763b42517', 'dist/venator-intro-2026-09-24/audio/9ba626afa44a3aa3.patch_0'),
    f'{ARCHIVE}.patch_250.stream': ('cfb1ca97348a', 'dist/venator-intro-2026-09-24/audio/9ba626afa44a3aa3.patch_0.stream'),
    f'{ARCHIVE}.patch_251': ('8a507b351491', 'dist/boss-temuera-2026-09-24/v1/rc/9ba626afa44a3aa3.patch_0'),
    f'{ARCHIVE}.patch_251.stream': ('3328f9e1b4dc', 'dist/boss-temuera-2026-09-24/v1/rc/9ba626afa44a3aa3.patch_0.stream'),
    f'{ARCHIVE}.patch_251.gpu_resources': ('e3b0c44298fc', 'dist/boss-temuera-2026-09-24/v1/rc/9ba626afa44a3aa3.patch_0.gpu_resources'),
}
MARKERS = [(f'{ARCHIVE}.patch_318{s}', f'dist/aim-voice-2026-09-24/markers-v4/9ba626afa44a3aa3.patch_0{s}')
           for s in ('', '.gpu_resources', '.stream')]
MARKER_SHA = '3cf552b99700243702c2389da4497f7d2c725ffc8f0a887db7b559c6f18abd43'


def sha(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def effective(pack: dict, profile: str, options: dict[str, bool]) -> list[dict]:
    """Port of Pack.EffectiveFiles: gate by mode/profile/options, then renumber gap-free per archive."""
    def enabled(option: str) -> bool:
        if pack.get('combinedRoster') and option.lower() in ('commandos', 'aimpoints'):
            return True
        return profile != 'full' if option == 'skinny' else options.get(option, True)
    mode = 'commandos' if enabled('commandos') else 'clonedivers'
    kept = [copy.deepcopy(f) for f in pack['files']
            if (f.get('modes') is None or mode in f['modes'])
            and (f.get('textureProfiles') is None or profile in f['textureProfiles'])
            and (f.get('option') is None or enabled(f['option']))
            and (f.get('unlessOption') is None or not enabled(f['unlessOption']))]
    names = [f['name'].lower() for f in kept]
    if len(names) != len(set(names)):
        raise ValueError(f'Duplicate effective name in {profile}')
    if len(kept) == len(pack['files']) or not pack.get('isPerFile'):
        return kept
    ranks = {}
    for f in kept:
        if m := PATCH.match(f['name']):
            ranks.setdefault(m['arch'].lower(), set()).add(int(m['idx']))
    ranks = {a: {i: r for r, i in enumerate(sorted(s))} for a, s in ranks.items()}
    for f in kept:
        if m := PATCH.match(f['name']):
            f['name'] = f"{m['arch']}.patch_{ranks[m['arch'].lower()][int(m['idx'])]}{m['comp'] or ''}"
    return kept


def build(out: Path) -> None:
    if out.exists():
        raise ValueError('Choose a fresh output directory')
    feed = json.loads((ROOT / 'manifest-v3.json').read_text(encoding='utf-8'))
    if feed['format'] != 3 or feed['pack']['version'] != '2026.09.24-r10':
        raise ValueError('This build is bound to the published r10 feed')
    r10 = feed['pack']
    # The port must reproduce the verified r10 profile folders exactly (names and sizes).
    for profile in ('full', 'lighter'):
        files = effective(r10, profile, {})
        folder = R10_PROFILES / profile
        on_disk = {p.name: p.stat().st_size for p in folder.iterdir()}
        if on_disk != {f['name']: f['size'] for f in files}:
            raise ValueError(f'Effective-file port disagrees with the r10 {profile} folder')

    pack = copy.deepcopy(r10)
    new_assets, seen = {}, set()
    for f in pack['files']:
        if f['name'] in REPLACE:
            expected, source = REPLACE[f['name']]
            if not f['sha256'].startswith(expected):
                raise ValueError(f'{f["name"]} is not the expected r10 file')
            path = ROOT / source
            f['sha256'], f['size'] = sha(path), path.stat().st_size
            f['url'] = URL + f['sha256'] if f['size'] else ''
            if f['size']:
                new_assets[f['sha256']] = path
            seen.add(f['name'])
    if seen != set(REPLACE):
        raise ValueError(f'Missing r10 entries: {sorted(set(REPLACE) - seen)}')
    if any(PATCH.match(f['name']) and PATCH.match(f['name'])['arch'] == ARCHIVE and int(PATCH.match(f['name'])['idx']) >= 318
           for f in pack['files']):
        raise ValueError('patch_318 is already in use')
    if sha(ROOT / MARKERS[0][1]) != MARKER_SHA:
        raise ValueError('Marker patch is not the validated v4 candidate')
    for name, source in MARKERS:
        path = ROOT / source
        digest, size = sha(path), path.stat().st_size
        pack['files'].append({'name': name, 'url': URL + digest if size else '', 'size': size, 'sha256': digest,
                              'modes': ['clonedivers', 'commandos'], 'textureProfiles': ['full', 'lighter'], 'option': 'droids'})
        if size:
            new_assets[digest] = path
    # The launcher computes sizes from the file list; r10's stored totalSize is informational,
    # so carry it forward by the size change rather than redefining it.
    delta = sum(f['size'] for f in pack['files']) - sum(f['size'] for f in r10['files'])
    pack.update(version=VERSION, totalSize=r10['totalSize'] + delta,
                notes='Clones and commandos, together. Venator fleet opening.',
                statusNotes='r11 opening, Boss/Temuera voice fill and Spider Droid markers are validated offline; '
                            'not yet played in game. Supply FRV and Watcher audio remain experimental.')
    manifest = {'format': 3, 'pack': pack}
    out.mkdir(parents=True)
    (out / 'manifest.json').write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    assets = out / 'assets'
    assets.mkdir()
    for digest, path in new_assets.items():
        shutil.copyfile(path, assets / digest)
        if sha(assets / digest) != digest:
            raise ValueError('Asset copy failed')

    directories, summary = [], {}
    for profile in ('full', 'lighter'):
        old = {(f['name'], f['sha256']) for f in effective(r10, profile, {})}
        folder = out / 'profiles' / profile
        folder.mkdir(parents=True)
        files = effective(pack, profile, {})
        linked = copied = 0
        for f in files:
            target = folder / f['name']
            if (f['name'], f['sha256']) in old:
                os.link(R10_PROFILES / profile / f['name'], target)  # verified r10 bytes, shared read-only
                linked += 1
            else:
                source = assets / f['sha256'] if f['size'] else None
                if source is None:
                    target.write_bytes(b'')
                else:
                    shutil.copyfile(source, target)
                copied += 1
        summary[profile] = {'files': len(files), 'linkedFromR10': linked, 'new': copied,
                            'markerName': next(f['name'] for f in files if f['sha256'] == MARKER_SHA)}
        directories.append({'id': profile, 'directory': str(folder.resolve())})
    (out / 'directories.json').write_text(json.dumps(directories, indent=2) + '\n', encoding='utf-8')
    report = {'version': VERSION, 'baseline': 'manifest-v3.json r10', 'newAssets': len(new_assets),
              'newAssetBytes': sum(p.stat().st_size for p in new_assets.values()), 'profiles': summary,
              'changes': ['Venator fleet opening (patch_249/250)', 'Boss + Temuera voice fill (patch_251)',
                          'Spider Droid eye/vent markers (patch_318, option droids)'],
              'runtimeTested': False}
    (out / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, indent=2))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    try:
        build(args.out)
        return 0
    except (OSError, ValueError, KeyError, StopIteration) as exc:
        print(f'r11 candidate build failed: {exc}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
