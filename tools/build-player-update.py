#!/usr/bin/env python3
"""Assemble a local combined-roster test feed; never install or publish it."""

import argparse
import copy
import hashlib
import importlib.util
import json
import logging
import re
import shutil
import struct
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ARCHIVE = '9ba626afa44a3aa3'
LOG = logging.getLogger(__name__)


def digest(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def inventory_hash(folder: Path) -> str:
    paths = sorted((p for p in folder.iterdir() if re.fullmatch(r'.+\.patch_\d+(\.(gpu_resources|stream))?', p.name)), key=lambda p: p.name.casefold())
    text = '\n'.join(f'{p.name}|{p.stat().st_size}|{digest(p)}' for p in paths)
    return hashlib.sha256(text.encode('utf-8')).hexdigest()


def build_labels(output: Path, current_strings: Path) -> dict[str, Path]:
    spec = importlib.util.spec_from_file_location('voice_labels', ROOT / 'tools/rc-voice-labels.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    report = load(ROOT / 'dist/rc-upgrade/voice-labels/patch/build-report.json')
    with zipfile.ZipFile(ROOT / 'dist/mods/recovered - Galactic Map Overhaul (last).zip') as archive:
        sources = [archive.read(name) for name in archive.namelist() if re.search(r'\.patch_\d+$', name)]
    matches = [data for data in sources if hashlib.sha256(data).hexdigest() == report['templateSha256']]
    if len(matches) != 1:
        raise ValueError('Winning English bank template does not match provenance')
    source = matches[0]
    entry, type_row, old_mod = module.extract_resource(source)
    mod_text, _ = module.parse_strings(old_mod)
    old_base = {item['Key']: item['Value'] for item in load(ROOT / 'dist/rc-upgrade/voice-labels/strings-json/0x7c7587b563f10985.strings.json')['Items']}
    current, _ = module.parse_strings(current_strings.read_bytes())
    if not old_base.keys() <= current.keys():
        raise ValueError('Current text removes historical keys; review the merge')
    ranks = {108830821,436363760,739236005,781916859,783035461,926009925,1270272614,
             1602327515,1846720240,1917009630,2050983655,2675103067,2744478874,2864776199,
             3016462333,3361168857,3534500369,3686957532,3886371824,4161927870}
    theme = re.compile(r'clone|trooper|republic|geonosian|separatist|coruscant|venator|naboo|skako|night sisters', re.I)
    merged, decisions = current.copy(), []
    for key, text in mod_text.items():
        if text == old_base.get(key):
            continue
        if key not in current or current[key] != old_base.get(key):
            raise ValueError(f'Text three-way merge conflict: {key}')
        preserve = key in ranks or bool(theme.search(text))
        # These old entries combine themed wording with obsolete game descriptions.
        # Retain the new mechanics/anniversary description rather than stale prose.
        if key in {197005611,1896446197,1665034634}:
            preserve = False
        if preserve:
            merged[key] = text
        decisions.append({'key': key, 'decision': 'theme' if preserve else 'current-game', 'oldMod': text, 'result': merged[key]})
    # Body and helmet entries share these names. Qualify the equipment type,
    # and never claim the three B-01 variants have independently named helmets.
    equipment = [
        (2740153811, 3555499414, 'DP-11 — Commando Boss armor'),
        (2778148389, 1763602207, 'CM-10 — Commando Fixer armor'),
        (1265620134, 2501843501, 'CE-35 — Commando Scorch armor'),
        (3161943025, 3046397365, 'SC-30 — Commando Sev armor'),
        (526077306, 2343099532, 'CM-09 — 212th armor / Commando Sev helmet'),
        (2020958113, 1306153500, 'B-01 — Clone / Commando variants'),
    ]
    equipment_names = {}
    for cased, upper, label in equipment:
        for key, value in ((cased, label), (upper, label.upper())):
            if key not in current:
                raise ValueError(f'Equipment name key missing: {key}')
            equipment_names[key] = value
    merged.update(equipment_names)
    keys = sorted(merged)
    header = current_strings.read_bytes()[:8] + struct.pack('<II', len(keys), module.US)
    key_table = b''.join(struct.pack('<I', key) for key in keys)
    offsets, values = bytearray(), bytearray()
    for key in keys:
        offsets.extend(struct.pack('<I', 16+8*len(keys)+len(values)))
        values.extend(merged[key].encode('utf-8') + b'\0')
    original = header + key_table + offsets + values
    if module.parse_strings(original)[0] != merged:
        raise ValueError('Current text merge failed serialization')
    write(output / 'merge-report.json', {'currentSha256': digest(current_strings), 'currentKeys': len(current),
          'restoredKeys': len(current.keys()-mod_text.keys()), 'decisions': decisions, 'equipmentNames': equipment_names})
    before, fields = module.parse_strings(original)
    voice_keys = list(module.LABELS)[:8]
    paths = {}
    for preset in ('delta', 'clone'):
        edited = bytearray(original)
        replacements = {}
        for i, key in enumerate(voice_keys):
            text = module.LABELS[key][1] if preset == 'delta' else f'Clone Trooper {i // 2 + 1}'
            if i % 2:
                text = text.upper()
            replacements[key] = text
            struct.pack_into('<I', edited, fields[key], len(edited))
            edited.extend(text.encode('utf-8') + b'\0')
        after, _ = module.parse_strings(bytes(edited))
        if {key for key in before if before[key] != after[key]} != set(voice_keys):
            raise ValueError('Voice labels changed an unexpected set of text keys')
        restored = bytearray(edited[:len(original)])
        for key in voice_keys:
            restored[fields[key]:fields[key]+4] = original[fields[key]:fields[key]+4]
        if restored != original:
            raise ValueError('Voice labels modified unrelated source bytes')
        head, table, row = bytearray(source[:72]), bytearray(type_row), entry.copy()
        struct.pack_into('<II', head, 4, 1, 1)
        struct.pack_into('<Q', table, 16, 1)
        row[2:5], row[7:10], row[12] = [192, 0, 0], [len(edited), 0, 0], 0
        blob = bytes(head) + bytes(table) + module.ENTRY.pack(*row) + bytes(8) + edited
        blob += bytes(-len(blob) % 16)
        if module.parse_strings(module.extract_resource(blob)[2])[0] != after:
            raise ValueError('Label serialization failed')
        path = output / preset / f'{ARCHIVE}.patch_0'
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(blob)
        if after.keys() != current.keys() or any(after[key] != current[key] for key in current.keys()-mod_text.keys()):
            raise ValueError('New game text was lost or changed')
        write(path.parent / 'report.json', {'templateSha256': report['templateSha256'], 'currentSha256': digest(current_strings),
              'changes': replacements, 'keys': len(after), 'restoredKeys': len(current.keys()-mod_text.keys()),
              'armorNames': equipment_names})
        paths[preset] = path
    return paths


def build(output: Path, stage: Path, base_url: str, roster: Path | None, roster_lighter: Path | None,
          current_strings: Path, audio_rebase: Path, watcher_audio: Path) -> None:
    if bool(roster) != bool(roster_lighter):
        raise ValueError('A mixed roster requires both Full and verified Lighter variants')
    if output.exists():
        raise ValueError('Choose a fresh output directory')
    baseline_path = ROOT / 'manifest-v3.json'
    manifest = load(baseline_path)
    if manifest['format'] != 3 or manifest['pack']['version'] != '2026.09.13-r9':
        raise ValueError('This migration is bound to the published r9 feed')
    report_path = ROOT / 'build-inputs/r8/deploy-report.json'
    receipt = load(ROOT / 'build-inputs/r8/deployment-receipt.json')
    if digest(report_path) != receipt['reportSha256']:
        raise ValueError('Ownership report provenance failed')
    ownership = load(report_path)
    cis = {r['file'] for r in ownership if r['mod'] == 'Automaton to CIS Overhaul'}
    if len(cis) != 33:
        raise ValueError('Unexpected CIS bundle count')
    output.mkdir(parents=True)
    labels = build_labels(output / 'labels', current_strings)
    audio_report = load(audio_rebase / 'report.json')
    audio_names = {bundle['name'] for bundle in audio_report['bundles']}
    if len(audio_names) != 55 or sum(slot['mediaCount'] for slot in audio_report['rc']) != 1525:
        raise ValueError('Incomplete audio rebase')
    for bundle in audio_report['bundles']:
        for file in bundle['files']:
            if digest(audio_rebase / 'base' / file['name']) != file['sha256']:
                raise ValueError('Audio rebase hash mismatch')
    pack = manifest['pack']
    watcher_report = load(watcher_audio / 'report.json')
    if watcher_report['bank'] != 'd8b809d8749c06c0' or watcher_report['otherEmbeddedSamplesUnchanged'] != 196:
        raise ValueError('Unexpected Watcher audio scope')
    for file in watcher_report['files']:
        if digest(watcher_audio / file['name']) != file['sha256']:
            raise ValueError('Watcher audio receipt mismatch')
    pack.update(version='2026.09.23-player-preview2', combinedRoster=True, notes='Clones and commandos, together.')
    pack['options'] = [
        {'id': 'commandos', 'name': 'Delta Squad', 'description': 'Delta Squad is elite. Turn off for regular clone voices. This choice affects voices you hear from squadmates too.', 'default': True},
        {'id': 'droids', 'name': 'Droid skins', 'description': 'Use CIS enemies, vehicles and structures. Enemy audio stays unchanged.', 'default': True},
        {'id': 'aimpoints', 'name': 'Walker cannons', 'description': 'Show the original Factory Strider/MTT cannons. This does not move hitboxes or change War Strider weak points.', 'default': True},
    ]
    files = []
    removed = []
    for original in pack['files']:
        file = copy.deepcopy(original)
        match = re.fullmatch(ARCHIVE + r'\.patch_(\d+)(.*)', file['name'])
        index = int(match[1]) if match else -1
        if re.sub(r'(\.stream|\.gpu_resources)$', '', file['name']) in audio_names:
            removed.append(original)
            continue
        if index in {251, 252, 253, 255, 257, 259, 261} or 262 <= index <= 273:
            removed.append(original)
            continue
        if 253 <= index <= 261:
            file.pop('option', None)
            file.pop('unlessOption', None)
            file['modes'] = ['clonedivers', 'commandos']
        main = re.sub(r'(\.stream|\.gpu_resources)$', '', file['name'])
        if main in cis:
            if file.get('option') or file.get('unlessOption'):
                raise ValueError('CIS asset has an unexpected existing gate')
            file['option'] = 'droids'
            if index == 198:
                file['unlessOption'] = 'aimpoints'
        # Match the author's order: body expansion before material reset, then
        # helmets and legion textures. Preserve every profile's original bytes.
        clone_order = {0: 0, 7: 1, 1: 2, 6: 3, 2: 4, 3: 5, 4: 6, 5: 7}
        if index in clone_order:
            file['name'] = re.sub(r'\.patch_\d+', f'.patch_{clone_order[index]}', file['name'])
        files.append(file)
    assets = {}

    def add(path: Path, name: str, **conditions: object) -> None:
        sha = digest(path)
        size = path.stat().st_size
        if size and sha not in assets:
            target = output / 'assets' / sha
            target.parent.mkdir(exist_ok=True)
            shutil.copyfile(path, target)
            assets[sha] = {'source': str(path.resolve()), 'size': size}
        files.append({'name': name, 'url': base_url.rstrip('/') + '/assets/' + sha if size else '',
                      'size': size, 'sha256': sha, **conditions})

    def add_bundle(folder: Path, index: int, **conditions: object) -> None:
        paths = sorted(folder.glob('*.patch_*'))
        if not paths:
            raise ValueError(f'No bundle: {folder}')
        for path in paths:
            add(path, re.sub(r'\.patch_\d+', f'.patch_{index}', path.name), **conditions)

    # Voice and film remain a listener-side preset. Armor is always in the shared roster.
    for path in sorted((audio_rebase / 'base').glob('*.patch_*')):
        if re.sub(r'(\.stream|\.gpu_resources)$', '', path.name) == f'{ARCHIVE}.patch_177':
            continue
        add(path, path.name)
    add_bundle(watcher_audio, 314)
    add_bundle(audio_rebase / 'rc', 251, option='commandos', modes=['commandos'])
    for preset, path in labels.items():
        add(path, f'{ARCHIVE}.patch_252', **({'option': 'commandos', 'modes': ['commandos']} if preset == 'delta'
            else {'unlessOption': 'commandos', 'modes': ['clonedivers']}))
    for character, index in [('Boss', 253), ('Fixer', 255), ('Scorch', 257), ('Sev', 259)]:
        add_bundle(stage / 'visuals/commandos/bodies' / character, index)
    add_bundle(stage / 'visuals/commandos/helmets', 261)
    for path in sorted((stage / 'visuals/scopes').glob('*.patch_*')):
        identity = path.name.split('.')[0]
        # Original-archive scope resources retain their archive ownership. Global resources load last.
        add(path, re.sub(r'\.patch_\d+', '.patch_313' if identity == ARCHIVE else '.patch_0', path.name))
    if roster:
        # Variant verification is performed by optimize-verified-pack.ps1; bind
        # the assembled feed to its exact inventories instead of trusting paths.
        variant = load(roster_lighter / 'variant-receipt.json')
        if variant['baseInventorySha256'] != inventory_hash(roster) or variant['inventorySha256'] != inventory_hash(roster_lighter):
            raise ValueError('Roster variant receipt no longer matches its source/output bytes')
        if variant.get('settings', {}).get('maxSize') != 0 or variant['settings'].get('dedup'):
            raise ValueError('Lighter roster must preserve resolution and payload identities')
        add_bundle(roster, 312, textureProfiles=['full'])
        add_bundle(roster_lighter, 312, textureProfiles=['lighter'])
    pack['files'] = files
    # Local preview must not advertise the live app updater as its matching launcher.
    manifest.pop('app', None)
    write(output / 'manifest.json', manifest)
    write(output / 'candidate.json', {'baselineSha256': digest(baseline_path), 'ownershipSha256': digest(report_path),
          'newAssets': assets, 'removedEntries': removed, 'runtimeTested': False, 'published': False,
          'assetBytes': sum(item['size'] for item in assets.values()), 'rosterIncluded': roster is not None,
          'audioRebaseReport': str((audio_rebase / 'report.json').resolve()), 'audioRebaseReportSha256': digest(audio_rebase / 'report.json'),
          'audioReplacedBundles': sorted(audio_names), 'gameBuildAudited': audio_report['gameBuild'],
          'removedProbeBundle': 177, 'watcherBundle': 314,
          'watcherReport': str((watcher_audio / 'report.json').resolve()),
          'watcherReportSha256': digest(watcher_audio / 'report.json'),
          'scopeRemovalBundlesExcluded': list(range(262, 274)), 'aimPointScope': 'Factory Strider/MTT original cannon visibility only'})
    LOG.info('Prepared %s entries; %.1f MiB of new local assets. Not installed or published.', len(files), sum(a['size'] for a in assets.values())/2**20)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--stage', type=Path, default=ROOT / 'dist/player-update-2026-09-23')
    parser.add_argument('--base-url', default='http://127.0.0.1:8767')
    parser.add_argument('--roster', type=Path)
    parser.add_argument('--roster-lighter', type=Path)
    parser.add_argument('--current-strings', type=Path, required=True)
    parser.add_argument('--audio-rebase', type=Path, required=True)
    parser.add_argument('--watcher-audio', type=Path, required=True)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    try:
        build(args.out, args.stage, args.base_url, args.roster, args.roster_lighter, args.current_strings, args.audio_rebase, args.watcher_audio)
        return 0
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, struct.error):
        LOG.exception('Player candidate build failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
