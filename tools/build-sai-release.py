#!/usr/bin/env python3
"""Add LAS-12 SAI E-11 audio and the pack's blue blaster bolt to r15.

Offline only. Reuses the current-format, already shipped bolt payload under the
SAI's distinct thruster identity; never patches weapon mechanics or game files.
"""

import argparse
import copy
import gzip
import importlib.util
import json
import logging
import os
import struct
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('sai_audio', ROOT / 'tools/rebase-player-audio.py')
R = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(R)
V = R.V
BANK_ID = 0x160d95832337d914
PARTICLES = 0xa8193123526fad64
SAI_BOLT = 0x9916eded8d58907d
DONOR_BOLT = 0xaa3e9927a36a96fb


def pinned(path: Path, digest: str) -> bytes:
    data = path.read_bytes()
    if V.sha(data) != digest:
        raise ValueError(f'Input changed; re-audit {path}')
    return data


def build(args: argparse.Namespace) -> None:
    if args.out.exists():
        raise ValueError('Use a fresh output directory')
    archive = pinned(args.archive, '61c166a2480381000ff7ae7835982d18953280022b5e12f29fa60a4de12e1800')
    source = pinned(args.source, '43f32bdbd6e84794e84c05fdbc188e936733d7260a9f25e14a140a00325e9ea4')
    resources = V.read_bundle((source, b'', b''), str(args.source))
    if set(resources) != {(BANK_ID, R.BANK), (BANK_ID, R.DEP)}:
        raise ValueError('Unexpected sound mod resources')
    native_bank = pinned(args.native / 'content/audio/wep_smg_laser_blaster.wwise_bank.main',
                         '38ac7654b961abc3c959f95d7411130b21312a763c7b89b286cd613edf891a4b')
    native_bolt = pinned(args.native / '0x9916eded8d58907d.particles.main',
                         '0600fe9592815979382275525a069ea728844d87bbefe037efa4e7da8a666983')
    mod_bank = resources[BANK_ID, R.BANK].payloads[0]
    old_chunks, new_chunks = R.chunks(native_bank), R.chunks(mod_bank)
    if old_chunks.keys() != new_chunks.keys() or any(old_chunks[k] != new_chunks[k] for k in old_chunks if k not in (b'DIDX', b'DATA')):
        raise ValueError('Sound mod changes native audio routing or bank metadata')
    old_media, new_media = R.media(old_chunks), R.media(new_chunks)
    if old_media.keys() != new_media.keys():
        raise ValueError('Sound media membership changed')
    changed = [key for key in old_media if old_media[key] != new_media[key]]
    if len(changed) != 42:
        raise ValueError('Unexpected E-11 sample changes')
    native_dep = (args.native / 'content/audio/wep_smg_laser_blaster.wwise_dep.main').read_bytes()
    if resources[BANK_ID, R.DEP].payloads[0] != native_dep:
        raise ValueError('Sound dependency differs from installed game')

    # Filediver v0.7.53 metadata: 350 x 272-byte ProjectileInfo records.
    # Type 236 is the unique thruster effect found in the SAI archive 28569b6c8c641298.
    metadata = pinned(args.projectiles, 'aec1858137136f567c664541827a22aec109b9fbef844b09c7db85fb6867be89')
    data = gzip.decompress(metadata)
    if struct.unpack_from('<qQ', data, 28) != (16, 350) or len(data) != 44 + 350 * 272:
        raise ValueError('Projectile metadata layout changed')
    references = [(struct.unpack_from('<I', data, 44 + i * 272)[0], i)
                  for i in range(350) if struct.unpack_from('<Q', data, 44 + i * 272 + 72)[0] == SAI_BOLT]
    if references != [(236, 327)]:
        raise ValueError('SAI projectile mapping changed')
    with zipfile.ZipFile(args.bolts) as archive_bolts:
        _, donor = V.read_zip_bundle(archive_bolts, 'Custom Projectile Colors/Blue/9ba626afa44a3aa3.patch_105')
    bolt = copy.deepcopy(donor[DONOR_BOLT, PARTICLES])
    if V.sha(bolt.payloads[0]) != 'd4004a31cb208aef507d3858104f9a1717957538c2013c97261332af16138870' or any(bolt.payloads[1:]):
        raise ValueError('Published blue-bolt donor changed')
    if struct.unpack_from('<I', bolt.payloads[0])[0] != struct.unpack_from('<I', native_bolt)[0]:
        raise ValueError('Particle format mismatch')
    if struct.unpack_from('<I', bolt.payloads[0])[0] != 115:
        raise ValueError('Expected current particle version 115')
    bolt.row[0] = SAI_BOLT
    resources[SAI_BOLT, PARTICLES] = bolt
    files = V.write_bundle(args.out / 'bundle/9ba626afa44a3aa3.patch_321', source, resources)
    feed = json.loads(args.baseline.read_text(encoding='utf-8'))
    pack = feed['pack']
    if pack['version'] != '2026.09.26-r15':
        raise ValueError('Re-audit the pack baseline')
    assets = args.out / 'assets'
    assets.mkdir()
    for item in files:
        if item['size']:
            os.link(args.out / 'bundle' / item['name'], assets / item['sha256'])
        pack['files'].append({**item, 'modes': ['clonedivers', 'commandos', 'empire'],
                              'url': f'https://github.com/owendavidgoode/clonedivers/releases/download/pack-{args.version}-files/{item["sha256"]}' if item['size'] else ''})
    pack['version'] = args.version
    pack['notes'] = 'LAS-12 SAI now includes E-11 firing sounds and blue Battlefront-style blaster bolts in Clonedivers and EmpireDivers.'
    pack['statusNotes'] = 'SAI sound and projectile payloads checked offline; gameplay unverified. EmpireDivers and Covenant remain experimental.'
    pack['totalSize'] = sum(f['size'] for f in pack['files'])
    (args.out / 'manifest.json').write_text(json.dumps(feed, indent=2) + '\n')
    report = {'source': 'https://www.nexusmods.com/helldivers2/mods/16485?tab=files&file_id=66060',
              'author': 'Bullet177013', 'sourceArchiveSha256': V.sha(archive),
              'nativeBankSha256': V.sha(native_bank), 'nativeHierarchyPreserved': True,
              'soundSamples': len(new_media), 'changedSamples': changed,
              'nativeDependencyPreserved': True, 'nativeBoltSha256': V.sha(native_bolt),
              'boltSource': 'thebf333 Custom Projectiles, shipped r15 patch_105 blue Sickle bolt',
              'boltPayloadSha256': V.sha(bolt.payloads[0]), 'boltPayloadUnchanged': True,
              'boltResource': f'{SAI_BOLT:016x}', 'projectileType': 236,
              'metadataSource': 'https://github.com/xypwn/filediver/tree/v0.7.53/datalibrary',
              'metadataSha256': V.sha(metadata), 'files': files,
              'weaponModelChanged': False, 'gameplayTested': False}
    (args.out / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    logging.info('Built SAI audio and laser bolt: 3 resources, 42 changed samples')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('archive', 'source', 'native', 'projectiles', 'bolts', 'baseline', 'out'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--version', default='2026.09.26-r16')
    logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s')
    try:
        build(parser.parse_args())
        return 0
    except (OSError, ValueError, KeyError, zipfile.BadZipFile):
        logging.exception('SAI build failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
