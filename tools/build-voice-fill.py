#!/usr/bin/env python3
"""Fill voice gaps left after the RC mapping, on top of the published Delta bundle.

Each of the four voice banks has ~785 transcribed lines. After r12 each slot still has lines
that play silence (the Full Clone Voice mod mutes lines it could not match) or a Battlefront
clone actor. This adds, per character:

1. Curated Republic Commando recordings for callout categories the RC expansion skipped
   (target and walker pings, "strategy selected", pickups, finds, compliments, supplies).
   Only lines that are currently silent or clone-voiced change; RC-mapped lines and Boss's
   Temuera lines are never replaced. Recordings are chosen by exact transcript from the
   audited RC catalog and must pass its recognition gates.
2. For lines still silent: the Temuera Morrison recording that the Temuera Voice Overhaul
   assigned to the identical line text in another voice slot.

RC fills are PCM (the bank's codec field changes to PCM for exactly those Sounds); Temuera
fills are Vorbis and need no bank change. Also relabels voice 4 "Boss/Clone". Every other
byte of the published Delta bundle and label bank is preserved. Fresh output folder only;
never installs or publishes.
"""

import argparse
import copy
import hashlib
import importlib.util
import json
import re
import struct
import sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GAME = Path(r'C:\Program Files (x86)\Steam\steamapps\common\Helldivers 2\data')
ARCHIVE = '9ba626afa44a3aa3'
BANK, DEP, STREAM = 0x535A7BD3E650D799, 0xAF32095C82F2B070, 0x504B55235D21440E
BASE = ROOT / 'dist/boss-temuera-2026-09-24/v1/rc/9ba626afa44a3aa3.patch_0'   # r12 manifest patch_251
BASE_SHA = 'b9f77af19ceff3e3710cd42b93b731cfcc489804829041f4a4fd420daf4daaf9'
LABELS_SHA = 'e9905cc405f61aa4c9b6cbe919c79d26c23730f317f49754115177233f7f23c9'  # r12 manifest patch_252
SLOT4_LABELS = {3149181118: ('Boss', 'Boss/Clone'), 534138438: ('BOSS', 'BOSS/CLONE')}
MAPPING = ROOT / 'dist/rc-voice-expansion-2026-09-23/mapping.json'
CATALOG = ROOT / 'dist/rc-audio-review-2026-09-22/clips.jsonl'
BOSS_TEMUERA = ROOT / 'dist/boss-temuera-2026-09-24/v1/report.json'
TEMUERA, CLONE = f'{ARCHIVE}.patch_159', f'{ARCHIVE}.patch_160'

# Curated callout categories: normalized game text -> exact RC transcripts per character.
RULES = [
    ('target_ping', r'^(that one|first one|second one|third one|fourth one|fifth one|6th one|sixth one)$',
     {'Sev': ["I'll take this one."],
      'Fixer': ['Enemy spotted.', 'Unknown target ahead.', "I'll take this one, sir."],
      'Scorch': ['Enemy spotted.', 'Got one right here.'],
      'Boss': ['Eliminate target.']}),
    ('walker_ping', r'^walker$',
     {'Sev': ['Spider Droid.', 'We got a spider droid here!'],
      'Fixer': ['Another spider droid!', 'Spider Droid, watch for missiles!'],
      'Scorch': ['Spider Droid!'],
      'Boss': ["How'd they get that spider droid in here?"]}),
    ('ready', r'^(strategy selected|strategies selected|stratagem selected|load out confirmed|loadout confirmed)$',
     {'Sev': ['Ready sir.', 'Ready for action.', 'Set for combat.'],
      'Fixer': ['Ready, sir.', 'Weapon ready.', 'Ready for battle.'],
      'Scorch': ['Ready, sir.', 'Weapon ready!', 'Ready as always.'],
      'Boss': ['Ready to engage.']}),
    ('pickup', r'(acquired|collected|e710|e7 10|uranium|legendari|saffron|biosample|biological sample|sample (will|should|container)|'
               r'sample container|bit of technology|scrap of technology|galaxys greatest energy)',
     {'Sev': ['Got it.', 'Got it, sir.'], 'Fixer': ['Got it.', 'Got it sir.'],
      'Scorch': ['Got it!', 'Got it.', 'Got it, sir.'], 'Boss': ['Got it.']}),
    ('found_something', r'^(i )?found something$|^theres something here$',
     {'Sev': ["There's something here."], 'Fixer': ["Don't move. There's something here.", 'Got a visual on... something.'],
      'Scorch': [], 'Boss': []}),
    ('nice', r'^nice$',
     {'Sev': ['Nice one.'], 'Fixer': ['Nice shot.', 'Good one, sir.'], 'Scorch': ['Nice.'], 'Boss': []}),
    ('supplies_ping', r'^supplies$',
     {'Sev': ['Ah, fresh ammo.'], 'Fixer': ["Look, there's the anti-armor supplies, sir."], 'Scorch': [],
      'Boss': ['Ammo crates, we need that ordnance.', 'More separatist supplies.']}),
    ('equipment_ping', r'^(weve got equipment|objective equipment|support weapon)$',
     {'Sev': [], 'Fixer': ['Found their weapons cache, sir.'], 'Scorch': [], 'Boss': []}),
    ('item_ping', r'^(critical item|high value item|package|sample|intel|objective equipment)$',
     {'Sev': ["There's something here."], 'Fixer': ['Got a visual on... something.'], 'Scorch': [], 'Boss': []}),
    ('wildlife', r'(wildlife|fauna|faun |animal)',
     {'Sev': ["Something's moving."], 'Fixer': [], 'Scorch': ['Something must have spooked those crits.'], 'Boss': []}),
    ('low_visibility', r'^decreased visibility',
     {'Sev': [], 'Fixer': [], 'Scorch': [], 'Boss': ["It's getting a little dark here."]}),
    ('fortifications', r'^fortifications$',
     {'Sev': [], 'Fixer': [], 'Scorch': [], 'Boss': ["They've set up a barricade on the bridge."]}),
]
MAX_SECONDS = 2.8
SILENCE_REPEATS = 100
CHARACTERS = {1: 'Sev', 2: 'Fixer', 3: 'Scorch', 4: 'Boss'}


def module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


V = module('audio_archive', ROOT / 'tools/build-player-visuals.py')
RC = module('rc_bank', ROOT / 'tools/build-rc-voices.py')
RB = module('rebase_audio', ROOT / 'tools/rebase-player-audio.py')
LBL = module('voice_labels', ROOT / 'tools/rc-voice-labels.py')
sys.path.insert(0, str(ROOT / 'dist/rc-upgrade/wwav'))
from wwav import convert  # noqa: E402  (MIT, staged; same converter as the RC build)


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


def norm(text: str) -> str:
    return re.sub(r'[^a-z0-9 ]', '', text.lower()).strip()


def load(path: Path):
    data = tuple(Path(str(path) + s).read_bytes() if Path(str(path) + s).exists() else b'' for s in V.SUFFIXES)
    return data, V.read_bundle(data, str(path))


def quiet(bundle: dict) -> set[str]:
    counts = Counter(sha(r.payloads[1]) for k, r in bundle.items() if k[1] == STREAM)
    return {d for d, n in counts.items() if n >= SILENCE_REPEATS}


def build(out: Path) -> None:
    if out.exists():
        raise ValueError('Choose a fresh output directory')
    data, base = load(BASE)
    if sha(data[0]) != BASE_SHA:
        raise ValueError('Base is not the published r12 Delta bundle')
    (_, temuera), (_, clone) = load(GAME / TEMUERA), load(GAME / CLONE)
    q_t, q_c = quiet(temuera), quiet(clone)
    mapping = json.loads(MAPPING.read_text(encoding='utf-8'))
    boss_temuera = {a['media'] for a in json.loads(BOSS_TEMUERA.read_text(encoding='utf-8'))['added']}
    catalog = [json.loads(line) for line in CATALOG.read_text(encoding='utf-8').splitlines()]

    def state(character: str, media: int) -> str:
        if character == 'Boss' and media in boss_temuera:
            return 'temuera'
        c = clone.get(stream_key(media))
        return 'none' if c is None else 'silent' if sha(c.payloads[1]) in q_c else 'clone'

    # Candidate RC recordings per (rule, character), by exact transcript and recognition gates.
    pools = {}
    for rule, _, lines in RULES:
        for character, wanted in lines.items():
            clips = []
            for text in wanted:
                found = [c for c in catalog if c['character'] == character and c['transcript'].strip() == text
                         and c['duration_seconds'] <= MAX_SECONDS and (c.get('subtitle_word_similarity') or 0) >= 0.8
                         and not any('uncertain' in f or 'no_speech' in f for f in c.get('flags', []))]
                if not found:
                    raise ValueError(f'No eligible {character} recording for {text!r} ({rule})')
                clips.extend(sorted(found, key=lambda c: c['sha256']))
            pools[rule, character] = clips

    # Classify every unmapped line and assign fills.
    entries = [(u['slot'], u['character'], u['hd2_media_id'], u['hd2_text']) for u in mapping['unmapped']]
    by_text = defaultdict(list)
    for slot in mapping['slots']:
        for r in slot['rows']:
            by_text[norm(r['hd2_text'])].append((slot['character'], r['hd2_media_id']))
    for _, character, media, text in entries:
        by_text[norm(text)].append((character, media))
    fills, rows, usage = {}, [], Counter()
    for slot, character, media, text in sorted(entries):
        now = state(character, media)
        if now not in ('silent', 'clone'):
            rows.append({'character': character, 'media': media, 'text': text, 'before': now, 'after': now})
            continue
        choice = None
        for rule, pattern, _ in RULES:
            if re.search(pattern, norm(text)) and pools.get((rule, character)):
                pool = pools[rule, character]
                clip = min(pool, key=lambda c: (usage[c['sha256']], c['sha256']))  # spread variety
                usage[clip['sha256']] += 1
                choice = ('rc', rule, clip)
                break
        if choice is None and now == 'silent':
            for other, other_media in by_text[norm(text)]:
                t = temuera.get(stream_key(other_media))
                if other_media != media and t is not None and sha(t.payloads[1]) not in q_t:
                    choice = ('temuera', f'{other}:{other_media}', t)
                    break
        if choice is None:
            rows.append({'character': character, 'media': media, 'text': text, 'before': now, 'after': now})
            continue
        fills[slot, media] = choice
        rows.append({'character': character, 'media': media, 'text': text, 'before': now,
                     'after': choice[0], 'source': choice[1] if choice[0] == 'temuera' else choice[2]['transcript'],
                     'rule': choice[1] if choice[0] == 'rc' else 'temuera-cross-slot'})

    # Serialize: new stream resources plus PCM codec edits in each affected bank.
    resources = dict(base)
    pcm_template = next(r for k, r in base.items() if k[1] == STREAM and r.payloads[1][20:22] == b'\xfe\xff')
    wem_dir = out / 'wem'
    wem_dir.mkdir(parents=True)
    pcm_targets = defaultdict(set)
    for (slot, media), (kind, _, source) in fills.items():
        key = stream_key(media)
        if key in base:
            raise ValueError(f'Fill target {media} is already in the Delta bundle')
        if kind == 'temuera':
            resources[key] = copy.deepcopy(source)
            resources[key].row[:2] = list(key)
            continue
        wem = wem_dir / f'{source["sha256"]}.wem'
        if not wem.exists():
            convert(source['file'], str(wem), codec='pcm', quiet=True)
        audio = wem.read_bytes()
        if audio[:4] != b'RIFF' or struct.unpack_from('<H', audio, 20)[0] != 0xFFFE:
            raise ValueError('Invalid PCM WEM')
        resource = copy.deepcopy(pcm_template)
        resource.row[:2] = list(key)
        resource.row[7:10] = [12, len(audio), 0]
        resource.payloads = (bytes.fromhex('D82F767800000000') + struct.pack('<I', len(audio)), audio, b'')
        resources[key] = resource
        pcm_targets[slot].add(media)
    bank_reports = []
    for slot, targets in sorted(pcm_targets.items()):
        bank_key = RC.BANK_IDS[slot], BANK
        payload = base[bank_key].payloads[0]
        edited, changes = RC.patch_bank(payload[16:], targets)
        resources[bank_key] = copy.deepcopy(base[bank_key])
        resources[bank_key].payloads = (payload[:16] + edited,) + base[bank_key].payloads[1:]
        bank_reports.append({'character': CHARACTERS[slot], 'pcmCodecEdits': len(changes)})
    files = V.write_bundle(out / 'rc' / f'{ARCHIVE}.patch_0', data[0], resources)
    check = V.read_bundle(tuple((out / 'rc' / f'{ARCHIVE}.patch_0{s}').read_bytes() for s in V.SUFFIXES), 'check')
    for key, resource in base.items():
        if key[1] != BANK and check[key].payloads != resource.payloads:
            raise ValueError(f'Base resource changed: {key}')
    for slot, targets in pcm_targets.items():  # restoring the new codec fields gives the base bank back
        bank_key = RC.BANK_IDS[slot], BANK
        restored = bytearray(check[bank_key].payloads[0])
        for change in RC.patch_bank(base[bank_key].payloads[0][16:], targets)[1]:
            struct.pack_into('<I', restored, 16 + change['codec_offset'], 0x40001)
        if bytes(restored) != base[bank_key].payloads[0]:
            raise ValueError('Bank bytes beyond codec fields changed')

    # Voice 4 label: "Boss/Clone".
    label_path = next(p for p in GAME.glob(f'{ARCHIVE}.patch_*')
                      if p.suffix not in ('.stream', '.gpu_resources') and p.stat().st_size == 172832 and sha(p.read_bytes()) == LABELS_SHA)
    blob = label_path.read_bytes()
    entry, type_row, text = LBL.extract_resource(blob)
    before, fields = LBL.parse_strings(text)
    edited = bytearray(text)
    for key, (old, new) in SLOT4_LABELS.items():
        if before[key] != old:
            raise ValueError(f'Label {key} is {before[key]!r}, expected {old!r}')
        struct.pack_into('<I', edited, fields[key], len(edited))
        edited.extend(new.encode('utf-8') + b'\0')
    after, _ = LBL.parse_strings(bytes(edited))
    if {k for k in before if before[k] != after[k]} != set(SLOT4_LABELS) or after.keys() != before.keys():
        raise ValueError('Label edit touched other keys')
    head, table, row = bytearray(blob[:72]), bytearray(type_row), entry.copy()
    struct.pack_into('<II', head, 4, 1, 1)
    struct.pack_into('<Q', table, 16, 1)
    row[2:5], row[7:10], row[12] = [192, 0, 0], [len(edited), 0, 0], 0
    label_blob = bytes(head) + bytes(table) + LBL.ENTRY.pack(*row) + bytes(8) + bytes(edited)
    label_blob += bytes(-len(label_blob) % 16)
    if LBL.parse_strings(LBL.extract_resource(label_blob)[2])[0] != after:
        raise ValueError('Label serialization failed')
    (out / 'labels').mkdir()
    (out / 'labels' / f'{ARCHIVE}.patch_0').write_bytes(label_blob)

    summary = {}
    for character in CHARACTERS.values():
        mine = [r for r in rows if r['character'] == character]
        summary[character] = {'before': dict(Counter(r['before'] for r in mine)), 'after': dict(Counter(r['after'] for r in mine)),
                              'byRule': dict(Counter(r.get('rule') for r in mine if r.get('rule'))),
                              'stillSilent': sorted({norm(r['text']) for r in mine if r['after'] == 'silent'})}
    report = {'base': BASE_SHA, 'filled': len(fills), 'distinctRcRecordings': len(usage), 'banks': bank_reports,
              'summary': summary, 'labels': {str(k): v[1] for k, v in SLOT4_LABELS.items()},
              'files': files + [{'name': 'labels/' + f'{ARCHIVE}.patch_0', 'size': len(label_blob), 'sha256': sha(label_blob)}],
              'rows': rows, 'installed': False, 'published': False, 'gameplayVerified': False}
    (out / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    for character, s in summary.items():
        print(f"{character}: before {s['before']} -> after {s['after']}")
        print(f"   rules {s['byRule']}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    try:
        build(args.out)
        return 0
    except (OSError, ValueError, KeyError, StopIteration, struct.error) as exc:
        print(f'Voice fill failed: {exc}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
