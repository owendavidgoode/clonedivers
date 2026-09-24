#!/usr/bin/env python3
"""Map Delta mesh resources to customization kits and inventory starter variants."""
import json
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'dist/rc-upgrade/starter-commandos'

def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    strings = {}
    for p in (ROOT / 'dist/rc-upgrade/voice-labels/strings-json').glob('*.json'):
        obj = json.loads(p.read_text(encoding='utf-8-sig'))
        if (obj.get('Language') or {}).get('KnownName') == 'us':
            strings.update({x['Key']: x['Value'] for x in obj['Items']})
    data = (ROOT / 'dist/rc-upgrade/routing-source/generated_customization_armor_sets.dl_bin').read_bytes()
    count, = struct.unpack_from('<I', data)
    pos = 4
    kits = []
    for _ in range(count):
        size, = struct.unpack_from('<I', data, pos + 12)
        base = pos + 24
        vals = struct.unpack_from('<8IQIIqq', data, base)
        ident, dlc, set_id, upper, cased, desc, rarity, passive, archive, kind, unknown, body_ptr, body_count = vals
        pieces = []
        for j in range(body_count):
            body, _, ptr, num = struct.unpack_from('<IIqq', data, base + body_ptr + j * 24)
            for k in range(num):
                unit, slot, piece_type, weight = struct.unpack_from('<QIII', data, base + ptr + k * 96)
                pieces.append(dict(unit=f'{unit:016x}', slot=slot, piece_type=piece_type, weight=weight, body_type=body))
        kits.append(dict(id=f'{ident:08x}', dlc_id=dlc, set_id=set_id, name=strings.get(cased, strings.get(upper, str(cased))), name_cased=cased, name_upper=upper, description=strings.get(desc, str(desc)), kind=kind, archive=f'{archive:016x}', pieces=pieces))
        pos = base + size
    assert pos == len(data), (pos, len(data))
    delta = json.loads((ROOT / 'dist/rc-upgrade/routing-delta-armor-assets.json').read_text(encoding='utf-8-sig'))
    unit_owners = {x['resource']:x['patch'].split('/')[1] for x in delta if x['type'] == 'e0a48d0be9a7453f'}
    matches=[]
    for kit in kits:
        matched=[dict(p, character=unit_owners[p['unit']]) for p in kit['pieces'] if p['unit'] in unit_owners]
        if matched:
            matches.append(dict(kit, matched=matched))
    starters=[k for k in kits if 'B-01' in k['name'] or 'Tactical' in k['name']]
    for name,obj in [('all-kits.json',kits),('delta-kit-mapping.json',matches),('starter-kits.json',starters)]:
        (OUT/name).write_text(json.dumps(obj,indent=2),encoding='utf-8')
    print(json.dumps({'kit_count':len(kits),'delta_affected_kit_count':len(matches),'starter_name_matches':len(starters),'output':str(OUT)},indent=2))
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
