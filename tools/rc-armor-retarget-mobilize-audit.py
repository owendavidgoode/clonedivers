#!/usr/bin/env python3
"""Audit concrete early-war-bond cosmetic retargets against existing Delta parts."""
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT/'dist/rc-upgrade/starter-commandos'

def main() -> int:
    kits=json.loads((BASE/'all-kits.json').read_text())
    donor=json.loads((BASE/'delta-unit-payloads.json').read_text())
    by_id={k['id']:k for k in kits}
    source_ids={'Fixer':'7a07031f','Sev':next(k['id'] for k in kits if k['name']=='SC-30 Trailblazer Scout' and k['kind']==0)}
    target_ids=['076d9aad','58dbdacd','ecabadbf','26d572a4','5bb4bbb0']
    rows=[]
    for character,source_id in source_ids.items():
        source=by_id[source_id]
        eligible=[p for p in source['pieces'] if p['body_type'] in [0,3] and p['slot']!=1]
        for target_id in target_ids:
            target=by_id[target_id]
            targets=[p for p in target['pieces'] if p['body_type'] in [0,3] and p['slot']!=1]
            mapping=[]
            for p in targets:
                sig=(p['slot'],p['piece_type'])
                possible=[s for s in eligible if (s['slot'],s['piece_type'])==sig]
                if len(possible)>1: raise ValueError('Ambiguous semantic piece')
                s=possible[0] if possible else None
                overlap=[d['patch'].split('/')[1] for d in donor if d['unit']==p['unit']]
                others=[dict(name=k['name'],id=k['id']) for k in kits if k['id']!=target_id and k['kind']==0 and p['unit'] in [q['unit'] for q in k['pieces']]]
                mapping.append(dict(target_unit=p['unit'],slot=p['slot'],piece_type=p['piece_type'],body_type=p['body_type'],source_unit=s['unit'] if s else None,source_in_delta=any(d['unit']==s['unit'] and f'/{character}/' in d['patch'] for d in donor) if s else False,existing_delta_characters=overlap,other_kit_consumers=others))
            unused=[p for p in eligible if p['unit'] not in [m['source_unit'] for m in mapping]]
            row=dict(character=character,source_kit=source_id,target_kit=target_id,target_name=target['name'],target_archive=target['archive'],mapping=mapping,unused_source_pieces=unused)
            rows.append(row)
    (BASE/'mobilize-target-audit.json').write_text(json.dumps(rows,indent=2))
    for row in rows:
        print(row['character'],row['target_name'],row['target_archive'],'targets',len(row['mapping']),'unmapped',sum(not m['source_unit'] or not m['source_in_delta'] for m in row['mapping']),'unused donors',len(row['unused_source_pieces']),'Delta conflicts',[(m['target_unit'],m['existing_delta_characters']) for m in row['mapping'] if m['existing_delta_characters']])
    return 0

if __name__=='__main__':
    raise SystemExit(main())
