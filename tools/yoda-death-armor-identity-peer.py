#!/usr/bin/env python3
"""Independently qualify the LEGO applied-body ID gate against all native kits."""
from __future__ import annotations

import argparse
from collections import Counter
from collections.abc import Sequence
import gzip
import hashlib
import json
import logging
from pathlib import Path
import struct
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT/'dist/empire-yoda-death-2026-10-08/armor'
OUT = BASE/'identity-peer-v1'
BODY_IDS = {0xb513fd54: 'storm', 0xe9add047: 'beach'}
HELMET_IDS = {0x5c3087d2, 0x2f748b84}


def require(value: bool, message: str) -> None:
    if not value:
        raise ValueError(message)


def pin(path: Path) -> dict[str, Any]:
    with path.open('rb') as handle:
        digest = hashlib.file_digest(handle, 'sha256').hexdigest()
    return {'path': str(path.resolve()), 'bytes': path.stat().st_size, 'sha256': digest}


def run() -> dict[str, Any]:
    require(not OUT.exists(), 'Fresh identity peer output')
    inventory_path = BASE/'inventory-v1/report.json'
    categories_path = BASE/'native-kits-v1/report.json'
    require(pin(inventory_path)['sha256']=='22967e062cd86601ef9ad54afa9534518699b4bac89140ddfe2498231369e7b5', 'Frozen current geometry inventory')
    require(pin(categories_path)['sha256']=='59abe1cf3b093dc5f3c4a8ad4cb93ebb67965be2d9db9fe416a473701b7e3c74', 'Frozen native category source')
    inventory = json.loads(inventory_path.read_text())
    category_proof = json.loads(categories_path.read_text())
    raw_path = ROOT/'dist/rc-upgrade/filediver-source/datalibrary/generated_customization_armor_sets.dl_bin.gz'
    raw = gzip.decompress(raw_path.read_bytes())
    source_pin = next(p for p in category_proof['nativeArmorDefinitions'] if p['path']==str(raw_path.resolve()))
    require(pin(raw_path)==source_pin, 'Native kit source hash checked independently')
    count = struct.unpack_from('<I',raw)[0]
    pointer = 4
    records = []
    for ordinal in range(count):
        require(pointer+24<=len(raw), 'Whole independent native record header')
        magic,version,type_hash,size,width = struct.unpack_from('<4sIIIB',raw,pointer)
        begin=pointer+24
        require(magic==b'LDLD' and width==1 and size>=64 and begin+size<=len(raw), 'Independent native record bounds')
        identity=struct.unpack_from('<I',raw,begin)[0]
        category=struct.unpack_from('<I',raw,begin+40)[0]
        body_offset,body_count=struct.unpack_from('<qQ',raw,begin+48)
        require(body_count<100 and body_offset>=0 and body_offset+body_count*24<=size, 'Independent native body array')
        body_types=[struct.unpack_from('<I',raw,begin+body_offset+24*i)[0] for i in range(body_count)]
        records.append({'ordinal':ordinal,'equipmentId':identity,'category':category,'bodyTypes':body_types})
        pointer=begin+size
    require(pointer==len(raw) and count==402 and len({r['equipmentId'] for r in records})==402, 'Exactly 402 unique native categories')
    positives=[r for r in records if r['equipmentId'] in BODY_IDS]
    require(len(positives)==2 and all(r['category']==0 and {0,1,3}<=set(r['bodyTypes']) for r in positives), 'Only actual body armor categories with both body types')
    require(all(r['category']==1 for r in records if r['equipmentId'] in HELMET_IDS), 'Helmet IDs independently distinguish category')
    def qualified_body(armor_id: Any, known_category: Any) -> bool:
        # Independent policy oracle; actual runtime peer must prove that its
        # native field is applied armor, then use the same two exact body IDs.
        return type(armor_id) is int and known_category==0 and armor_id in BODY_IDS
    all_native = [{'id':f"{r['equipmentId']:08x}",'category':r['category'],'accepted':qualified_body(r['equipmentId'],r['category'])} for r in records]
    require(sum(r['accepted'] for r in all_native)==2, 'Every other native equipment ID rejected')
    ordinary = next(r['equipmentId'] for r in records if r['category']==0 and r['equipmentId'] not in BODY_IDS)
    ordinary_helmet = next(r['equipmentId'] for r in records if r['category']==1 and r['equipmentId'] not in HELMET_IDS)
    fixture_rows=[]
    for armor in (*BODY_IDS, ordinary):
        for body_type in (0,1):
            for helmet in (*HELMET_IDS,ordinary_helmet,None):
                accepted=qualified_body(armor,0)
                expected=armor in BODY_IDS
                require(accepted==expected,'Body controls sound regardless helmet or body type')
                fixture_rows.append({'armor':f'{armor:08x}','bodyType':body_type,'helmet':f'{helmet:08x}' if helmet is not None else None,'accepted':accepted})
    negatives=[]
    for value,kind,reason in [(id,1,'Helmet category cannot qualify') for id in HELMET_IDS]+[(id,1,'Right body ID in wrong category cannot qualify') for id in BODY_IDS]+[(None,0,'Missing identity'),(0,0,'Zero identity'),('b513fd54',0,'Unparsed string identity'),(True,0,'Boolean is not armor ID'),(0xb513fd55,0,'Near-match ID'),(0xe9add046,0,'Near-match ID')]:
        require(not qualified_body(value,kind),reason)
        negatives.append({'input':value,'category':kind,'reason':reason,'rejected':True})
    require(len(inventory['units'])==26 and all(u['sourceCandidateMainExact'] for u in inventory['units']), 'Final actual r22 geometry coverage')
    expected_torso={(style,body) for style in ('storm','beach') for body in ('brawny','lean')}
    actual_torso={(u['style'],u['bodyType']) for u in inventory['units'] if u['role']=='torso'}
    require(actual_torso==expected_torso,'Four exact torso source routes')
    report={'passed':True,'tool':pin(Path(__file__)),'geometryInventory':pin(inventory_path),'nativeCategoryProof':pin(categories_path),'nativeDefinitions':pin(raw_path),
            'allNativeCategoryCounts':{str(k):v for k,v in sorted(Counter(r['category'] for r in records).items())},
            'all402NativeIdentityResults':all_native,'positiveBodyIds':[f'{v:08x}' for v in BODY_IDS],'helmetIdsExcluded':[f'{v:08x}' for v in HELMET_IDS],
            'bodyTypeAndHelmetFixtures':fixture_rows,'badIdentityAndCategoryNegatives':negatives,
            'policy':'Applied LEGO body armor controls this feature. Both body types qualify. Helmet selection neither grants nor removes the LEGO death cue.',
            'gateStillRequiredBeforePlayback':['Current applied-armor native field ownership and revalidation','Actual dead avatar state','Current per-emitter player/peer agreement','One-shot life/death deduplication','Available reviewed audio source'],
            'genericAvatarAssetReplacementAccepted':False,'assetOnlyDeathAudioRouteProven':False,'gameOrSettingsChanged':False,'runtimeAccepted':False}
    OUT.mkdir(parents=True)
    target=OUT/'report.json'
    target.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    return {'report':pin(target),'allNativeIdentities':len(all_native),'bodyHelmetFixtures':len(fixture_rows),'negativeIdentityFixtures':len(negatives)}


def main(argv: Sequence[str] | None = None) -> int:
    argparse.ArgumentParser(description=__doc__.splitlines()[0]).parse_args(argv)
    logging.basicConfig(level=logging.INFO,format='%(levelname)s: %(message)s')
    try:
        print(json.dumps(run()))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception('LEGO armor identity peer refused')
        return 1


if __name__=='__main__':
    sys.exit(main())
