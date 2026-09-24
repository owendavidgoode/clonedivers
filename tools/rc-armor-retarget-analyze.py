#!/usr/bin/env python3
"""Audit candidate cosmetic carrier resources and their other armor consumers."""
import json
import struct
import zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'dist/rc-upgrade/starter-commandos'
ENTRY=struct.Struct('<QQQQQQQIIIIII')

def main() -> int:
    kits=json.loads((OUT/'all-kits.json').read_text())
    consumers={}
    for kit in kits:
        for p in kit['pieces']:
            consumers.setdefault(p['unit'],[]).append(dict(id=kit['id'],name=kit['name'],kind=kit['kind'],body_type=p['body_type'],slot=p['slot'],piece_type=p['piece_type']))
    selected=[k for k in kits if k['archive'] in {'58e4bd4b2278d15c','562a45e9bc984eb9','6cd3d55c05d4eac1','519cc1ec2eb56e1d'}]
    candidates=[]
    for kit in selected:
        for p in kit['pieces']:
            if p['body_type'] not in [0,3]: continue
            other=[c for c in consumers[p['unit']] if c['name']!='B-01 Tactical']
            shared=[c for c in consumers[p['unit']] if c['id'] in {x['id'] for x in selected}]
            candidates.append(dict(kit=kit['id'],archive=kit['archive'],**p,other_consumers=other,starter_consumers=shared))
    (OUT/'starter-piece-consumers.json').write_text(json.dumps(candidates,indent=2))
    resources=[]
    with zipfile.ZipFile(ROOT/'dist/mods/Delta Squad AIO-552-1-1A-1777439542.zip') as z:
        for name in z.namelist():
            if '.patch_' not in name or name.endswith(('.stream','.gpu_resources')): continue
            data=z.read(name)
            _,types,count=struct.unpack_from('<III',data)
            for i in range(count):
                row=ENTRY.unpack_from(data,72+32*types+80*i)
                if row[1]!=0xe0a48d0be9a7453f: continue
                unit=f'{row[0]:016x}'
                resources.append(dict(patch=name,unit=unit,main_size=row[7],stream_size=row[8],gpu_size=row[9],consumers=consumers.get(unit,[])))
    (OUT/'delta-unit-payloads.json').write_text(json.dumps(resources,indent=2))
    print('Candidate unique starter carriers:')
    for p in candidates:
        if len(p['starter_consumers'])==1:
            print(p['kit'],p['unit'], 'slot',p['slot'],'type',p['piece_type'],'other consumers',len(p['other_consumers']))
    print('Delta unit payloads:')
    for r in resources:
        print(r['patch'],r['unit'],r['main_size'],r['gpu_size'])
    return 0

if __name__=='__main__':
    raise SystemExit(main())
