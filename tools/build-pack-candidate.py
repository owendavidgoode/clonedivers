#!/usr/bin/env python3
"""Assemble pack release inputs from the previous published feed (r11, r12 specs below).

r11 against r10 (everything else keeps its previous name, bytes and URL):
- manifest patch_249/patch_250: Venator fleet opening (video + separate English soundtrack),
  replacing the Republic Commando opening in place.
- manifest patch_251: Delta voice bundle with Temuera Morrison lines filling Boss's non-RC calls.
- manifest patch_318 (new, last, option droids): Spider Droid eye/vent aim markers.
r12 against r11: patch_249/patch_250 replaced by the opening trimmed before the Coastlake logo.

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
URL = 'https://github.com/owendavidgoode/clonedivers/releases/download/pack-{version}-files/'
PATCH = re.compile(r'^(?P<arch>.+?)\.patch_(?P<idx>\d+)(?P<comp>\.(?:gpu_resources|stream))?$')
V = 'dist/venator-intro-2026-09-24'
R11_NOTES = ('r11 opening, Boss/Temuera voice fill and Spider Droid markers are validated offline; '
             'not yet played in game. Supply FRV and Watcher audio remain experimental.')
# Per release: base feed version, verified base profile folders, manifest name ->
# (expected base sha256 prefix, replacement source), and appended entries.
RELEASES = {
    '2026.09.24-r11': {
        'base': '2026.09.24-r10', 'profiles': 'dist/release-1.6.0-inputs/profiles-verified',
        'replace': {
            f'{ARCHIVE}.patch_249': ('d54d14de3fbb', f'{V}/video/9ba626afa44a3aa3.patch_0'),
            f'{ARCHIVE}.patch_249.stream': ('3940d47b886d', f'{V}/video/9ba626afa44a3aa3.patch_0.stream'),
            f'{ARCHIVE}.patch_250': ('ffe763b42517', f'{V}/audio/9ba626afa44a3aa3.patch_0'),
            f'{ARCHIVE}.patch_250.stream': ('cfb1ca97348a', f'{V}/audio/9ba626afa44a3aa3.patch_0.stream'),
            f'{ARCHIVE}.patch_251': ('8a507b351491', 'dist/boss-temuera-2026-09-24/v1/rc/9ba626afa44a3aa3.patch_0'),
            f'{ARCHIVE}.patch_251.stream': ('3328f9e1b4dc', 'dist/boss-temuera-2026-09-24/v1/rc/9ba626afa44a3aa3.patch_0.stream'),
            f'{ARCHIVE}.patch_251.gpu_resources': ('e3b0c44298fc', 'dist/boss-temuera-2026-09-24/v1/rc/9ba626afa44a3aa3.patch_0.gpu_resources'),
        },
        'append': [(f'{ARCHIVE}.patch_318{s}', f'dist/aim-voice-2026-09-24/markers-v4/9ba626afa44a3aa3.patch_0{s}', 'droids')
                   for s in ('', '.gpu_resources', '.stream')],
        'appendSha': '3cf552b99700243702c2389da4497f7d2c725ffc8f0a887db7b559c6f18abd43',
        'notes': 'Clones and commandos, together. Venator fleet opening.', 'statusNotes': R11_NOTES,
        'changes': ['Venator fleet opening (patch_249/250)', 'Boss + Temuera voice fill (patch_251)',
                    'Spider Droid eye/vent markers (patch_318, option droids)'],
    },
    '2026.09.24-r12': {
        'base': '2026.09.24-r11', 'profiles': 'dist/release-r11-inputs/profiles',
        'replace': {
            f'{ARCHIVE}.patch_249': ('f07bb0be7309', f'{V}/trim/video/9ba626afa44a3aa3.patch_0'),
            f'{ARCHIVE}.patch_249.stream': ('efd4d27e74b8', f'{V}/trim/video/9ba626afa44a3aa3.patch_0.stream'),
            f'{ARCHIVE}.patch_250': ('13803d0f8ed7', f'{V}/trim/audio/9ba626afa44a3aa3.patch_0'),
            f'{ARCHIVE}.patch_250.stream': ('8f7e49de03a8', f'{V}/trim/audio/9ba626afa44a3aa3.patch_0.stream'),
        },
        'append': [], 'appendSha': None,
        'notes': 'Clones and commandos, together. Venator fleet opening.',
        'statusNotes': 'r12 trims the Coastlake logo from the opening. ' + R11_NOTES.replace('r11 ', 'The '),
        'changes': ['Venator opening trimmed before the Coastlake end logo (patch_249/250)'],
    },
}


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


def build(out: Path, version: str, base_feed: Path) -> None:
    if out.exists():
        raise ValueError('Choose a fresh output directory')
    spec = RELEASES[version]
    base_profiles = ROOT / spec['profiles']
    url = URL.format(version=version)
    feed = json.loads(base_feed.read_text(encoding='utf-8'))
    if feed['format'] != 3 or feed['pack']['version'] != spec['base']:
        raise ValueError(f'{version} is bound to the {spec["base"]} feed; pass --base-feed')
    base = feed['pack']
    # The port must reproduce the verified base profile folders exactly (names and sizes).
    for profile in ('full', 'lighter'):
        on_disk = {p.name: p.stat().st_size for p in (base_profiles / profile).iterdir()}
        if on_disk != {f['name']: f['size'] for f in effective(base, profile, {})}:
            raise ValueError(f'Effective-file port disagrees with the base {profile} folder')

    pack = copy.deepcopy(base)
    new_assets, seen = {}, set()
    for f in pack['files']:
        if f['name'] in spec['replace']:
            expected, source = spec['replace'][f['name']]
            if not f['sha256'].startswith(expected):
                raise ValueError(f'{f["name"]} is not the expected {spec["base"]} file')
            path = ROOT / source
            f['sha256'], f['size'] = sha(path), path.stat().st_size
            f['url'] = url + f['sha256'] if f['size'] else ''
            if f['size']:
                new_assets[f['sha256']] = path
            seen.add(f['name'])
    if seen != set(spec['replace']):
        raise ValueError(f'Missing base entries: {sorted(set(spec["replace"]) - seen)}')
    if spec['append']:
        if sha(ROOT / spec['append'][0][1]) != spec['appendSha']:
            raise ValueError('Appended patch is not the validated candidate')
        taken = {f['name'] for f in pack['files']}
        for name, source, option in spec['append']:
            if name in taken:
                raise ValueError(f'{name} is already in use')
            path = ROOT / source
            digest, size = sha(path), path.stat().st_size
            pack['files'].append({'name': name, 'url': url + digest if size else '', 'size': size, 'sha256': digest,
                                  'modes': ['clonedivers', 'commandos'], 'textureProfiles': ['full', 'lighter'],
                                  'option': option})
            if size:
                new_assets[digest] = path
    # The launcher computes sizes from the file list; the stored totalSize is informational,
    # so carry it forward by the size change rather than redefining it.
    delta = sum(f['size'] for f in pack['files']) - sum(f['size'] for f in base['files'])
    pack.update(version=version, totalSize=base['totalSize'] + delta, notes=spec['notes'],
                statusNotes=spec['statusNotes'])
    out.mkdir(parents=True)
    (out / 'manifest.json').write_text(json.dumps({'format': 3, 'pack': pack}, indent=2, ensure_ascii=False) + '\n',
                                       encoding='utf-8')
    assets = out / 'assets'
    assets.mkdir()
    for digest, path in new_assets.items():
        shutil.copyfile(path, assets / digest)
        if sha(assets / digest) != digest:
            raise ValueError('Asset copy failed')

    directories, summary = [], {}
    for profile in ('full', 'lighter'):
        old = {(f['name'], f['sha256']) for f in effective(base, profile, {})}
        folder = out / 'profiles' / profile
        folder.mkdir(parents=True)
        files = effective(pack, profile, {})
        linked = copied = 0
        for f in files:
            target = folder / f['name']
            if (f['name'], f['sha256']) in old:
                os.link(base_profiles / profile / f['name'], target)  # verified base bytes, shared read-only
                linked += 1
            elif f['size']:
                shutil.copyfile(assets / f['sha256'], target)
                copied += 1
            else:
                target.write_bytes(b'')
                copied += 1
        summary[profile] = {'files': len(files), 'linkedFromBase': linked, 'new': copied}
        directories.append({'id': profile, 'directory': str(folder.resolve())})
    (out / 'directories.json').write_text(json.dumps(directories, indent=2) + '\n', encoding='utf-8')
    report = {'version': version, 'baseline': f'{spec["base"]} feed', 'newAssets': len(new_assets),
              'newAssetBytes': sum(p.stat().st_size for p in new_assets.values()), 'profiles': summary,
              'changes': spec['changes'], 'runtimeTested': False}
    (out / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, indent=2))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--version', required=True, choices=sorted(RELEASES))
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--base-feed', type=Path, default=ROOT / 'manifest-v3.json',
                        help='feed of the base release, e.g. `git show a7dfccf:manifest-v3.json` for r11')
    args = parser.parse_args()
    try:
        build(args.out, args.version, args.base_feed)
        return 0
    except (OSError, ValueError, KeyError, StopIteration) as exc:
        print(f'Pack candidate build failed: {exc}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
