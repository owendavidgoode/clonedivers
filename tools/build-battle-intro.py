#!/usr/bin/env python3
"""Build the r17 battle intro and short, silent startup logo replacements offline."""

import argparse
import copy
import importlib.util
import json
import shutil
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


R = module('battle_audio', ROOT / 'tools/rebase-player-audio.py')
V = R.V
INTRO = module('battle_video', ROOT / 'tools/build-rc-intro.py')
AUDIO = module('battle_stream', ROOT / 'tools/build-rc-intro-audio.py')
BINK = INTRO.BINK
LOGOS = (0x5263C1FE0615C6AD, 0xC08AFC84BE2BC3FA)
LOGO_BANK = 0xB7CED9DAFD29E46A
SFX_BANK = 0x55C14C4B194BCE0F
VERSION = '2026.09.27-r17'


def replace(resource, main: bytes, stream: bytes = b''):
    result = copy.deepcopy(resource)
    result.payloads = (main, stream, b'')
    result.row[7:10] = list(map(len, result.payloads))
    return result


def silence_logos(bank: bytes, wem: bytes) -> tuple[bytes, list[int]]:
    chunks = R.chunks(bank)
    media = R.media(chunks)
    sounds = R.sounds(chunks[b'HIRC'])
    if len(media) != 9 or set(media) != set(sounds):
        raise ValueError('Re-audit native logo bank media')
    original = chunks[b'HIRC']
    hirc = bytearray(original)
    restored = bytearray(original)
    position = 4
    changes = []
    for _ in range(struct.unpack_from('<I', hirc)[0]):
        kind, size = struct.unpack_from('<BI', hirc, position)
        if kind == 2:
            source = position + 9
            codec, stream, identity = struct.unpack_from('<IBI', hirc, source)
            old_size = struct.unpack_from('<I', hirc, source + 13)[0]
            if (codec, stream) != (0x40001, 0) or old_size != len(media[identity]):
                raise ValueError('Unexpected logo sound source layout')
            struct.pack_into('<I', hirc, source, 0x10001)
            struct.pack_into('<I', hirc, source + 13, len(wem))
            changes.append((source, old_size))
        position += 5 + size
    if position != len(hirc) or len(changes) < 9:
        raise ValueError('Logo sound count mismatch')
    restored = bytearray(hirc)
    for source, size in changes:
        struct.pack_into('<I', restored, source, 0x40001)
        struct.pack_into('<I', restored, source + 13, size)
    if restored != original:
        raise ValueError('Logo routing changed')
    table, data = bytearray(), bytearray()
    for identity in media:
        data.extend(bytes(-len(data) % 16))
        table.extend(struct.pack('<III', identity, len(data), len(wem)))
        data.extend(wem)
    updated = {**chunks, b'HIRC': bytes(hirc), b'DIDX': bytes(table), b'DATA': bytes(data)}
    body = b''.join(struct.pack('<4sI', key, len(value)) + value for key, value in updated.items())
    wrapper = bytearray(bank[:16]); struct.pack_into('<I', wrapper, 4, len(body))
    result = bytes(wrapper) + body
    if any(value != wem for value in R.media(R.chunks(result)).values()):
        raise ValueError('Logo media did not round-trip')
    return result, sorted(media)


def build(work: Path, baseline: Path, out: Path) -> None:
    if out.exists():
        raise ValueError('Use a fresh output directory')
    feed = json.loads(baseline.read_text(encoding='utf-8'))
    if feed['pack']['version'] != '2026.09.26-r16':
        raise ValueError('Re-audit baseline')
    originals = {}
    for number in (6, 249, 250):
        name = f'9ba626afa44a3aa3.patch_{number}'
        blobs = []
        for suffix in V.SUFFIXES:
            item = next((f for f in feed['pack']['files'] if f['name'] == name + suffix), None)
            blob = (work / 'templates' / (name + suffix)).read_bytes()
            if item and (V.sha(blob) != item['sha256'] or len(blob) != item['size']):
                raise ValueError('Template differs from shipped r16')
            if not item and blob:
                raise ValueError('Unexpected template sidecar')
            blobs.append(blob)
        originals[number] = (blobs[0], V.read_bundle(tuple(blobs), name))
    movie, blank, wem, silence = [(work / name).read_bytes() for name in ('intro.bk2', 'blank.bk2', 'intro.wem', 'silence.wem')]
    movie_info, blank_info = INTRO.bink_info(movie), INTRO.bink_info(blank)
    if movie_info['seconds'] != 38 or blank_info['frames'] != 2:
        raise ValueError('Unexpected video duration')
    for audio in (wem, silence):
        if audio[:4] != b'RIFF' or struct.unpack_from('<H', audio, 20)[0] != 0xFFFE:
            raise ValueError('Expected Wwise PCM audio')
    native = work / 'native-export'
    resources = originals[6][1]
    # Keep the existing cutscene SFX suppression, remove the obsolete intro/audio,
    # and replace both startup logos with two black frames.
    logos = {key: value for key, value in resources.items() if key[0] == SFX_BANK}
    template = resources[LOGOS[0], BINK]
    logos[LOGOS[0], BINK] = replace(template, template.payloads[0], blank)
    logos[LOGOS[1], BINK] = replace(template, (native / '0xc08afc84be2bc3fa.bk2.main').read_bytes(), blank)
    logos[LOGOS[1], BINK].row[0] = LOGOS[1]
    logo_source = (native / 'content/audio/intro_logos.wwise_bank.main').read_bytes()
    logo_bank, muted = silence_logos(logo_source, silence)
    for typ, payload in ((R.BANK, logo_bank), (R.DEP, (native / 'content/audio/intro_logos.wwise_dep.main').read_bytes())):
        resource = replace(resources[SFX_BANK, typ], payload)
        resource.row[0] = LOGO_BANK
        logos[LOGO_BANK, typ] = resource
    video = originals[249][1]
    key = INTRO.INTRO, BINK
    video[key] = replace(video[key], video[key].payloads[0], movie)
    audio = originals[250][1]
    key = AUDIO.STREAM_ID, R.STREAM
    audio[key] = replace(audio[key], audio[key].payloads[0][:8] + struct.pack('<I', len(wem)), wem)
    # Use the installed game's current English cutscene bank, with only this
    # source's codec changed. Other cutscene events and dialogue remain native.
    current_bank = (native / 'content/audio/us/cutscenes.wwise_bank.main').read_bytes()
    bank_body, bank_changes = R.RC.patch_bank(current_bank[16:], {AUDIO.SOURCE_ID})
    patched_bank = current_bank[:16] + bank_body
    key = AUDIO.BANK_ID, R.BANK
    audio[key] = replace(audio[key], patched_bank)
    key = AUDIO.BANK_ID, R.DEP
    audio[key] = replace(audio[key], (native / 'content/audio/us/cutscenes.wwise_dep.main').read_bytes())
    files = []
    for number, original, bundle in ((322, 6, logos), (323, 249, video), (324, 250, audio)):
        files.extend(V.write_bundle(out / 'bundles' / f'9ba626afa44a3aa3.patch_{number}', originals[original][0], bundle))
    assets = out / 'assets'; assets.mkdir()
    for file in files:
        if file['size']:
            shutil.copyfile(out / 'bundles' / file['name'], assets / file['sha256'])
        feed['pack']['files'].append({**file, 'modes': ['empire'],
            'textureProfiles': ['full', 'lighter'],
            'url': f'https://github.com/owendavidgoode/clonedivers/releases/download/pack-{VERSION}-files/{file["sha256"]}' if file['size'] else ''})
    feed['pack']['version'] = VERSION
    feed['pack']['notes'] = 'EmpireDivers: Helldivers vs Star Wars battle intro with matching audio and startup logo cards removed. Clonedivers keeps the Venator intro. Covenant and SAI E-11 sound/blue bolts retained.'
    feed['pack']['statusNotes'] = 'Intro and mode combinations checked offline; gameplay unverified. EmpireDivers and Covenant remain experimental.'
    feed['pack']['totalSize'] = sum(f['size'] for f in feed['pack']['files'])
    (out / 'manifest.json').write_text(json.dumps(feed, indent=2) + '\n')
    report = {'source': 'https://www.youtube.com/watch?v=c8bCzPS9DMI',
        'title': 'Helldivers vs Star Wars', 'author': 'ChrisMastree Productions', 'musicCredit': 'DaricShireMusic (source description)',
        'trim': '0–38s; picture fade 36.9–37.4s, audio fade 36.9–38s; trailing black removed',
        'video': movie_info, 'blankLogos': blank_info, 'videoSha256': V.sha(movie), 'wemSha256': V.sha(wem),
        'logoResources': [f'{identity:016x}' for identity in LOGOS], 'mutedLogoMedia': muted,
        'nativeLogoBankSha256': V.sha(logo_source), 'nativeCutsceneBankSha256': V.sha(current_bank),
        'cutsceneBankChange': 'Only English intro source codec: Vorbis to PCM',
        'cutsceneSources': bank_changes,
        'mode': 'empire', 'clonediversVenatorUnchanged': True,
        'existingCutsceneSfxSuppressionPreserved': True, 'files': files, 'gameplayTested': False}
    (out / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work', required=True, type=Path)
    parser.add_argument('--baseline', required=True, type=Path)
    parser.add_argument('--out', required=True, type=Path)
    args = parser.parse_args()
    build(args.work, args.baseline, args.out)
