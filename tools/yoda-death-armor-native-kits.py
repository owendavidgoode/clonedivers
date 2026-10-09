#!/usr/bin/env python3
"""Independently decode LEGO kit categories and bindings from native DL records."""
from __future__ import annotations

import argparse
from collections.abc import Sequence
import gzip
import hashlib
import importlib.util
import json
import logging
from pathlib import Path
import struct
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'dist/empire-yoda-death-2026-10-08/armor/native-kits-v1'
INVENTORY = ROOT/'dist/empire-yoda-death-2026-10-08/armor/inventory-v1/report.json'
SOURCE = ROOT/'dist/rc-upgrade/filediver-source/datalibrary'


def require(value: bool, message: str) -> None:
    if not value:
        raise ValueError(message)


def pin(path: Path) -> dict[str, Any]:
    with path.open('rb') as handle:
        value = hashlib.file_digest(handle, 'sha256').hexdigest()
    return {'path': str(path.resolve()), 'bytes': path.stat().st_size, 'sha256': value}


def module(name: str, file: str) -> Any:
    spec = importlib.util.spec_from_file_location(name, ROOT/'tools'/file)
    require(spec is not None and spec.loader is not None, 'Import '+file)
    value = importlib.util.module_from_spec(spec)
    sys.modules[name] = value
    spec.loader.exec_module(value)
    return value


def run() -> dict[str, Any]:
    require(not OUT.exists(), 'Fresh native armor definitions')
    inventory_pin = pin(INVENTORY)
    require(inventory_pin['sha256'] == '22967e062cd86601ef9ad54afa9534518699b4bac89140ddfe2498231369e7b5', 'Final actual-r22 geometry inventory')
    inventory = json.loads(INVENTORY.read_text())
    expected = {int(r['id'],16): r for r in inventory['styles']}
    C = module('yoda_kit_library', 'audit-weapon-audio.py')
    components = C.Components(ROOT/'dist/weapon-audio-2026-10-03/filediver-v0.7.53')
    kit_size, kit_fields = components.type('HelldiverCustomizationKit')
    def typed(identity: int) -> tuple[int,list[dict[str,int]]]:
        data = components.library
        u32 = lambda at: struct.unpack_from('<I',data,at)[0]
        matches = [i for i in range(components.type_count) if u32(36+4*i)==identity]
        require(len(matches)==1, 'Unique typed child struct')
        at = components.type_start+36*matches[0]
        fields = []
        for i in range(u32(at+24)):
            field = components.member_start+72*(u32(at+28)+i)
            fields.append({'offset':u32(field+40),'size':u32(field+24),'count':struct.unpack_from('<H',data,field+14)[0],
                           'atom':data[field+12],'storage':data[field+13],'type':u32(field+16)})
        return u32(at+12), fields
    require(kit_size==64 and len(kit_fields)==11 and kit_fields[10]['offset']==48 and kit_fields[10]['size']==16, 'Primary kit struct and bodies DL array')
    body_type_hash = kit_fields[10]['type']
    body_size, body_fields = typed(body_type_hash)
    require(body_size==24 and len(body_fields)==2 and [(f['offset'],f['size']) for f in body_fields]==[(0,4),(8,16)], 'Primary body type and pieces array')
    piece_type_hash = body_fields[1]['type']
    piece_size, piece_fields = typed(piece_type_hash)
    require(piece_size==96 and [(f['offset'],f['size']) for f in piece_fields[:4]]==[(0,8),(8,4),(12,4),(16,4)], 'Primary native piece identity/category/weight')
    require(len(piece_fields)==13 and [(f['offset'],f['size']) for f in piece_fields[4:12]]==[(at,8) for at in range(24,88,8)] and piece_fields[12]['offset']==88 and piece_fields[12]['size']==1, 'Only decoded material/customization refs and tone byte after native piece identity')
    compressed = SOURCE/'generated_customization_armor_sets.dl_bin.gz'
    raw_path = SOURCE/'generated_customization_armor_sets.dl_bin'
    raw = gzip.decompress(compressed.read_bytes())
    require(raw == raw_path.read_bytes(), 'Retained decoded armor source exact')
    count = struct.unpack_from('<I',raw)[0]
    require(0<count<10000, 'Bounded kit count')
    at = 4
    found = []
    for ordinal in range(count):
        require(at+24<=len(raw), 'Whole native kit header')
        magic, version, type_hash, size, width = struct.unpack_from('<4sIIIB',raw,at)
        base = at+24
        require(magic==b'LDLD' and type_hash==C.dl_hash('HelldiverCustomizationKit') and width==1 and base+size<=len(raw) and size>=kit_size, 'Typed native kit block')
        identity, dlc, set_id, name_upper, name_cased, description, rarity, passive = struct.unpack_from('<8I',raw,base)
        archive, category = struct.unpack_from('<QI',raw,base+32)
        def array(pointer: int, stride: int) -> tuple[int,int]:
            offset,length=struct.unpack_from('<qQ',raw,pointer)
            require(0<=length<10000 and (length==0 or offset>=0 and offset+length*stride<=size), 'Bounded source-relative native array')
            return base+offset,length
        body_pointer, body_count = array(base+48,body_size)
        pieces = []
        for b in range(body_count):
            bp = body_pointer+b*body_size
            body = struct.unpack_from('<I',raw,bp)[0]
            pp,pn = array(bp+8,piece_size)
            for p in range(pn):
                unit,slot,kind,weight = struct.unpack_from('<Q3I',raw,pp+p*piece_size)
                pieces.append({'unit':f'{unit:016x}','slot':slot,'piece_type':kind,'weight':weight,'body_type':body})
        if identity in expected:
            exp = expected[identity]
            require(category==exp['kind'] and archive==int(exp['archive'],16), 'Native armor/helmet category and archive match cached association')
            canonical = lambda x: (x['unit'],x['slot'],x['piece_type'],x['weight'],x['body_type'])
            require(sorted(map(canonical,pieces))==sorted(map(canonical,exp['pieces'])), 'Every body-type native UNIT binding exact')
            found.append({'ordinal':ordinal,'equipmentId':f'{identity:08x}','categoryValue':category,'category':exp['category'],'label':exp['label'],
                          'archive':f'{archive:016x}','recordOffset':base,'recordBytes':size,'recordSha256':hashlib.sha256(raw[base:base+size]).hexdigest(),
                          'nameUpper':name_upper,'nameCased':name_cased,'passiveId':passive,'bodyCount':body_count,'pieces':pieces,
                          'cachedAssociationsMatchNativeRecordExactly':True})
        at=base+size
    require(at==len(raw) and len(found)==4, 'Every native block consumed and four target categories resolved')
    output = {'passed':True,'tool':pin(Path(__file__)),'actualR22Inventory':inventory_pin,'nativeArmorDefinitions':[pin(compressed),pin(raw_path)],
              'primarySchema':pin(SOURCE/'armor_sets.go'),'typeLibrary':pin(ROOT/'dist/weapon-audio-2026-10-03/filediver-v0.7.53/dl_library.dl_typelib.gz'),
              'typedKitSchema':{'bytes':kit_size,'fields':kit_fields},'typedBodySchema':{'typeHash':f'{body_type_hash:08x}','bytes':body_size,'fields':body_fields},
              'typedPieceSchema':{'typeHash':f'{piece_type_hash:08x}','bytes':piece_size,'fields':piece_fields},
              'nativeKitRecordsConsumed':count,'targetKitRecords':found,
              'decodedKitSchemaIncludesDeathAudioField':False,'decodedPieceSchemaIncludesDeathAudioField':False,
              'nativeControlSnapshotCurrentRuntimeDecryption':False,
              'scope':'The retained native armor definitions independently confirm four equipment IDs, categories and all body-type UNIT bindings; they do not create a per-model death sound.',
              'noGlobalVoiceReplacementRequiredByIdentityGate':True,
              'runtimeHandoff':{'positiveAppliedArmorIds':['b513fd54','e9add047'],'helmetOnlyIdsExcluded':['5c3087d2','2f748b84'],
                                'defaultPolicy':'A positively identified applied LEGO armor body qualifies; helmet alone does not. Both body types are included.'},
              'gameOrSettingsChanged':False,'manifestOrEquipmentAssociationsChanged':False,'runtimeAccepted':False}
    OUT.mkdir(parents=True)
    target=OUT/'report.json'
    target.write_text(json.dumps(output,indent=2)+'\n',encoding='utf-8')
    return {'report':pin(target),'records':count,'categories':[{k:r[k] for k in ('equipmentId','category','label','bodyCount')} for r in found]}


def main(argv: Sequence[str] | None = None) -> int:
    argparse.ArgumentParser(description=__doc__.splitlines()[0]).parse_args(argv)
    logging.basicConfig(level=logging.INFO,format='%(levelname)s: %(message)s')
    try:
        print(json.dumps(run()))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception('Native LEGO kit categories refused')
        return 1


if __name__=='__main__':
    sys.exit(main())
