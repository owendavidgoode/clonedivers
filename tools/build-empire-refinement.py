#!/usr/bin/env python3
"""Build Empire-only r19 inputs: curated voices, current text, capes and ships.

Audio replacements use current native media IDs and retain native bank routing.
No game installation or publishing occurs here. Source recordings remain credited
to RiqCrow's collection; this is a curated conversion, not original acting.
"""
import copy
import csv
import importlib.util
import json
import os
from pathlib import Path
import re
import struct
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / 'dist/empire-refinement'
OUT = WORK / 'release-inputs'
STEM = '9ba626afa44a3aa3.patch_0'
VERSION = '2026.09.27-r19'


def module(name, file):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools' / file)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


RB = module('empire_audio', 'rebase-player-audio.py')
V = RB.V
G = module('empire_geometry', 'build-vehicle-followup.py')
P = module('empire_pack', 'build-pack-candidate.py')
L = module('empire_labels', 'rc-voice-labels.py')
sys.path.insert(0, str(ROOT / 'dist/rc-upgrade/hd2-audio-modder'))
from util import murmur64_hash

# Reviewed short stormtrooper recordings, grouped by HD2 gameplay meaning.
# The donor's original assignments included unrelated chatter; do not inherit them.
RULES = [
    (r'^affirmative$|^yes$', [9, 10, 11, 334]),
    (r'^(sorry|i apologize)$', [231, 232]),
    (r'thank', [321, 170]),
    (r'^nice$|good (work|job|shot)', [219, 291]),
    (r'reload|out of ammo|magazine', [165, 262, 130]),
    (r'reinforc', [161, 204, 105]),
    (r'grenade|explod|hellbomb', [175, 179, 134]),
    (r'^follow me$|^this way$', [143, 252]),
    (r'^hold position$|^hold this position$|^wait$', [190, 191]),
    (r'^go go go$|^move', [4, 252]),
    (r'^(cover me|covering fire)', [84, 262, 286]),
    (r'^negative$|^no$', [230]),
    (r'friendly fire|same team|watch your (aim|fire)', [156, 330]),
    (r'on fire|it burns|burning', [253, 264]),
    (r'broken (arm|leg)|bleed|wound|injur|losing blood', [76, 161, 208]),
    (r'cant move|cannot move|stuck', [141]),
    (r'^enemy|^enemies|^patrol|^hostile|^contact', [308, 127]),
    (r'flying|gunship|shrieker', [7, 8]),
    (r'^attack|^engage|^open fire', [61, 133, 29]),
    (r'^(strategy selected|strategies selected|stratagem selected|loadout confirmed|load out confirmed)', [59, 102]),
    (r'^(acquired|collected)|sample.*(acquired|collected)|^got (it|one)$', [186, 214]),
    (r'found something|something here|supplies$|equipment$|^sample$', [324, 125]),
    (r'low visibility|decreased visibility|cant see', [327]),
    (r'cold|freezing', [81]),
    (r'^wildlife|^fauna|^animal', [94]),
    (r'requesting air|calling.*eagle|sending.*eagle', [298]),
    (r'^first one$|^second one$|^third one$|^fourth one$|^fifth one$|^sixth one$|^that one$', [128, 20]),
]


def load(path):
    blobs = tuple(Path(str(path) + s).read_bytes() if Path(str(path) + s).exists() else b'' for s in V.SUFFIXES)
    return blobs, V.read_bundle(blobs, str(path))


def zipped(name, folder=''):
    with zipfile.ZipFile(WORK / 'source' / name) as z:
        return V.read_zip_bundle(z, folder + STEM)


def normalized(text):
    return re.sub('[^a-z0-9 ]', '', text.lower()).strip()


def key(media):
    return murmur64_hash(f'content/audio/us/{media}'.encode()), RB.STREAM


def build():
    if OUT.exists():
        raise ValueError('Use a fresh release input directory')
    feed = json.loads((ROOT / 'manifest-v3-current.json').read_text())
    if feed['pack']['version'] != '2026.09.27-r18':
        raise ValueError('Expected r18 baseline')
    OUT.mkdir(); (OUT / 'assets').mkdir()
    (OUT / 'baseline.json').write_text(json.dumps(feed, indent=2)+'\n')
    report = {'sources': {}, 'bundles': [], 'gameplayTested': False}
    for name, expected in {
        'empire-voices-47129.zip':'ab18dfbfc35057eaaa097c5bd2bb6e3083bad23b7ad21a158b9c5b1a907df0ce',
        'empire-command-61601.zip':'5e6007e120648bfdb9ab11248dd2c5f1fd482fbc9eb01f37ec2dd97cb5782586',
        'galactic-map-55782.zip':'337cdcc27be52b5cf7d04875e882aee63b3ccbd57b7af0abec09e94f5d745020',
    }.items():
        actual = V.sha((WORK / 'source' / name).read_bytes())
        if actual != expected: raise ValueError('Source mismatch: '+name)
        report['sources'][name] = actual
    report['sources']['galactic-map-55782.zip'] = V.sha((WORK / 'source/galactic-map-55782.zip').read_bytes())
    pack = feed['pack']
    srcfiles = P.effective(pack, 'full', {o['id']:o.get('default',False) for o in pack['options']})
    sources = {f['sha256']:ROOT/'dist/sai/profiles-verified/full'/f['name'] for f in srcfiles if f['size']}
    for folder in ['dist/empire-covenant/release-inputs/assets', 'dist/intro-1.7.1/release-inputs/assets']:
        for path in (ROOT/folder).iterdir(): sources.setdefault(path.name,path)

    def old_bundle(number):
        stem = STEM.replace('.patch_0', f'.patch_{number}')
        names = {f['name']:f for f in pack['files'] if not f['size'] or f['sha256'] in sources}
        blobs = tuple(sources[names[stem+s]['sha256']].read_bytes() if names.get(stem+s,{}).get('size') else b'' for s in V.SUFFIXES)
        return blobs,V.read_bundle(blobs,stem)

    def append(number, label, blobs, resources, replace=False):
        path = OUT / label / STEM
        files = V.write_bundle(path, blobs[0], resources)
        prefix = STEM.replace('.patch_0',f'.patch_{number}')
        if replace:
            pack['files'] = [f for f in pack['files'] if re.sub(r'\.(stream|gpu_resources)$','',f['name']) != prefix]
        for file in files:
            if file['size'] and not (OUT/'assets'/file['sha256']).exists():
                os.link(path.parent/file['name'],OUT/'assets'/file['sha256'])
            pack['files'].append({**file,'name':file['name'].replace('.patch_0',f'.patch_{number}'),
                                  'modes':['empire'],'url':P.URL.format(version=VERSION)+file['sha256'] if file['size'] else ''})
        report['bundles'].append({'number':number,'label':label,'resources':len(resources),'files':files})

    # Replace the old low-detail ships inside the Imperial armor/LAAT bundle.
    blobs, resources = old_bundle(320)
    removed = json.loads((WORK/'ship-resources.json').read_text())
    for r in removed: resources.pop((int(r['id'],16),int(r['type'],16)))
    _, ships = load(WORK/'ships-v2'/STEM)
    for k,r in ships.items():
        if k in resources and resources[k].payloads != r.payloads: raise ValueError('Ship/armor collision')
        resources[k] = r
    append(320,'ships-and-armor',blobs,resources,True)

    # Eliminate all draw groups, including shadows, in the shipped cape donor.
    blobs, donor = old_bundle(245); capes = {}
    for k,r in donor.items():
        if k[1] != G.UNIT: continue
        data = bytearray(r.payloads[0]); gpu = r.payloads[2]
        if G.u32(data,44) != 10800438: raise ValueError('Cape uses legacy unit format')
        for at in G.pointers(data,92):
            struct.pack_into('<I',data,at+392,0); struct.pack_into('<I',data,at+428,0)
        for at in G.pointers(data,100):
            for i in range(G.u32(data,at+120)):
                struct.pack_into('<I',data,at+G.u32(data,at+124)+24*i+16,0)
        capes[k] = G.with_data(r,bytes(data),gpu)
    append(325,'no-capes',blobs,capes)

    # SEAF weapon/foley effects are mixed with Clone dialogue in the old bundle.
    blobs, mixed = old_bundle(163); sfx = {}; allowed = set()
    inv = json.loads((WORK/'inventory.json').read_text())['163']
    for row in inv:
        if row['type'] != f'{RB.BANK:016x}' or not re.search(r'/(seaf_weapons_|foley_seafs)',row.get('name',''),re.I): continue
        identity = int(row['id'],16)
        sfx[identity,RB.BANK] = mixed[identity,RB.BANK]
        if (identity,RB.DEP) in mixed: sfx[identity,RB.DEP] = mixed[identity,RB.DEP]
        for media in RB.sounds(RB.chunks(mixed[identity,RB.BANK].payloads[0])[b'HIRC']):
            allowed.add(key(media))
            allowed.add((murmur64_hash(f'content/audio/{media}'.encode()),RB.STREAM))
    sfx.update({k:r for k,r in mixed.items() if k in allowed})
    if not sfx: raise ValueError('No shared SEAF weapon effects')
    append(326,'seaf-weapon-sounds',blobs,sfx)

    # Native-routing-safe streams only: donor banks and old event tables never ship.
    names = {}
    for line in (ROOT/'dist/intro-1.7.1/all-native.txt').read_text().splitlines():
        m = re.match(r'(.+)\.wwise_bank, 0x([0-9a-f]+)\.',line)
        if m: names[int(m[2],16)] = m[1]
    def native_bank(identity):
        relative = names[identity]+'.wwise_bank.main'
        for folder in [WORK/'native',ROOT/'dist/compat-7.1.1/current']:
            if (folder/relative).exists(): return (folder/relative).read_bytes()
        raise ValueError('Missing current bank: '+relative)
    def stream_only(blobs, donor, selected=None):
        result = {}; banks = []
        for (identity,kind),r in donor.items():
            if kind != RB.BANK or (selected is not None and identity not in selected): continue
            old = RB.sounds(RB.chunks(r.payloads[0])[b'HIRC'])
            current = RB.sounds(RB.chunks(native_bank(identity))[b'HIRC'])
            count = 0
            for media in old.keys() & current.keys():
                if key(media) not in donor: continue
                if struct.unpack_from('<IB',old[media][1]) != struct.unpack_from('<IB',current[media][1]):
                    raise ValueError('Voice codec/stream mode changed')
                if current[media][1][4] != 2: continue
                result[key(media)] = donor[key(media)]; count += 1
            banks.append({'name':names[identity],'streams':count})
        return result,banks
    blobs, donor = zipped('empire-command-61601.zip')
    officers, banks = stream_only(blobs,donor)
    report['imperialCommand'] = banks
    append(327,'imperial-command',blobs,officers)

    blobs, donor = zipped('empire-voices-47129.zip')
    catalog_path = ROOT/'build-inputs/r19/storm-recordings.json'
    catalog = json.loads(catalog_path.read_text(encoding='utf-8-sig'))
    if V.sha(json.dumps(catalog,sort_keys=True,separators=(',',':')).encode()) != 'bfec68fa05ff99f023ff2d1a63ed2b4d66c60d15751922375f79ad5242495b3d':
        raise ValueError('Audited voice catalog changed')
    voices = {}; mapped = []; coverage = {}
    bysha = {V.sha(r.payloads[1]):r for k,r in donor.items() if k[1]==RB.STREAM}
    for slot,csvpath in enumerate(sorted((ROOT/'dist/rc-upgrade/hd2-voice-transcripts').glob('voice*.csv')),1):
        identity = [0x0a39096a51ae9e86,0x017f8b366af141f8,0x498646c6a1ffba95,0x6c91a47b88d26638][slot-1]
        sounds = RB.sounds(RB.chunks(native_bank(identity))[b'HIRC'])
        count = 0
        for row in csv.DictReader(csvpath.open(encoding='utf-8-sig')):
            media = int(row['wem_short_id']); text = normalized(row['text'])
            choices = next((clips for pattern,clips in RULES if re.search(pattern,text)),None)
            if choices is None or media not in sounds: continue
            if struct.unpack_from('<IB',sounds[media][1]) != (0x40001,2): raise ValueError('Player source is not streamed Vorbis')
            chosen = catalog[str(choices[media % len(choices)])]
            if not chosen['transcript'] or chosen['duration']>4: raise ValueError('Unreviewed/long callout')
            r = copy.deepcopy(bysha[chosen['sha']]); r.row[:2] = key(media)
            voices[key(media)] = r; count += 1
            mapped.append({'slot':slot,'media':media,'gameText':row['text'],'recording':chosen['sha'],'transcript':chosen['transcript']})
        coverage[slot] = {'mapped':count,'nativeSounds':len(sounds),'unmapped':'native game voice; never Clone or Commando audio'}
    report['stormtrooperCoverage'] = coverage
    (OUT/'voice-mapping.json').write_text(json.dumps(mapped,indent=2)+'\n')
    append(328,'stormtrooper-callouts',blobs,voices)

    blobs, icons = zipped('galactic-map-55782.zip','icon-origimp/')
    append(329,'imperial-map-icons',blobs,icons)
    blobs, texts = zipped('galactic-map-55782.zip','text-imp/')
    for k,r in texts.items():
        native = (WORK/'native'/f'0x{k[0]:016x}.strings.main').read_bytes()
        entries, offsets = L.parse_strings(native); replacements,_ = L.parse_strings(r.payloads[0])
        data = bytearray(native)
        if k[0] == L.RESOURCE:
            for label in list(L.LABELS)[:8]: replacements[label] = 'Stormtrooper' if entries[label][0].isupper() and not entries[label].isupper() else 'STORMTROOPER'
        for identity,text in replacements.items():
            if identity not in entries: continue
            if entries[identity] == text: continue
            struct.pack_into('<I',data,offsets[identity],len(data));data.extend(text.encode()+b'\0')
        texts[k] = RB.payload(r,bytes(data))
        actual,_ = L.parse_strings(data)
        if set(actual)!=set(entries): raise ValueError('Lost current text keys')
    append(330,'imperial-map-text',blobs,texts)
    blobs, donor = zipped('galactic-map-55782.zip','voice-default/')
    streams,banks = stream_only(blobs,donor)
    report['mapPA'] = banks
    append(331,'map-pa',blobs,streams)
    pack['version'] = VERSION
    pack['notes'] = 'EmpireDivers: detailed Star Destroyers, no capes, curated stormtrooper callouts and Imperial command voices, Imperial map UI and shared SEAF weapon sounds.'
    pack['statusNotes'] = 'Validated offline; gameplay remains unverified. Unmapped stormtrooper callouts retain native HD2 voices. Clone voices and Venator remain exclusive to Clonedivers.'
    pack['totalSize'] = sum(f['size'] for f in pack['files'])
    (OUT/'manifest.json').write_text(json.dumps(feed,indent=2)+'\n')
    (OUT/'report.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({'bundles':len(report['bundles']),'coverage':coverage,'command':report['imperialCommand']}))


if __name__ == '__main__':
    build()
