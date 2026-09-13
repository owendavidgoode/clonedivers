#!/usr/bin/env python3
"""Stage four Delta helmets on the four B-01 variants with original dependencies."""
import hashlib
import json
import struct
import zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'dist/rc-upgrade/starter-commandos/helmet-patch'
ENTRY=struct.Struct('<QQQQQQQIIIIII')
UNIT=0xe0a48d0be9a7453f
BONES=0x18dead01056b72e9
MATERIAL=0xeac0b497876adedf
TEXTURE=0xcd4238c6a0c69e32
SLOTS=[('Sev','651ccf16ab84901a','bc20d0b4efff128c'),('Fixer','6c5bacf02c5aa0f0','7b23e3c0ab4cf618'),('Scorch','697d6a57971da101','c96cb2e72d7a0525'),('Boss','48e63f84996be394','781134771dd69fbe')]

def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()

def entries(data: bytes) -> list[list[int]]:
    magic,types,count=struct.unpack_from('<III',data)
    assert magic==0xf0000011
    return [list(ENTRY.unpack_from(data,72+32*types+80*i)) for i in range(count)]

def main() -> int:
    OUT.mkdir(parents=True,exist_ok=True)
    source=ROOT/'dist/mods/Delta Squad AIO-552-1-1A-1777439542.zip'
    resources={}
    header=None
    with zipfile.ZipFile(source) as z:
        for name in z.namelist():
            if '.patch_' not in name or name.endswith(('.stream','.gpu_resources')): continue
            main_data=z.read(name)
            header=main_data[:72]
            gpu=z.read(name+'.gpu_resources') if name+'.gpu_resources' in z.namelist() else b''
            stream=z.read(name+'.stream') if name+'.stream' in z.namelist() else b''
            for row in entries(main_data):
                key=tuple(row[:2])
                payloads=(main_data[row[2]:row[2]+row[7]],stream[row[3]:row[3]+row[8]],gpu[row[4]:row[4]+row[9]])
                assert tuple(map(len,payloads))==tuple(row[7:10])
                value=dict(row=row,payloads=payloads,source=name)
                if key in resources and resources[key]['payloads']!=payloads:
                    raise ValueError(f'Differing duplicate dependency {key}')
                resources[key]=value
    selected={}
    external=[]
    report=[]
    def include(key: tuple[int,int], reason: str) -> None:
        if key in selected: return
        if key not in resources:
            external.append(dict(resource=f'{key[0]:016x}',type=f'{key[1]:016x}',reason=reason))
            return
        selected[key]=resources[key]
        payload=resources[key]['payloads'][0]
        if key[1]==MATERIAL:
            count=struct.unpack_from('<I',payload,64)[0]
            assert 136+count*12<=len(payload)
            for tex in struct.unpack_from('<'+'Q'*count,payload,136+count*4):
                include((tex,TEXTURE),'material texture')
            base=struct.unpack_from('<Q',payload,24)[0]
            if base: include((base,MATERIAL),'base material')
    for slot,(character,old,new) in enumerate(SLOTS,1):
        old_key=(int(old,16),UNIT)
        new_key=(int(new,16),UNIT)
        value=resources[old_key]
        selected[new_key]=value
        payload=value['payloads'][0]
        bones=struct.unpack_from('<Q',payload,8)[0]
        if bones: include((bones,BONES),'helmet skeleton')
        material_offset=struct.unpack_from('<I',payload,112)[0]
        count=struct.unpack_from('<I',payload,material_offset)[0]
        assert count<100 and material_offset+4+12*count<=len(payload)
        materials=struct.unpack_from('<'+'Q'*count,payload,material_offset+4+4*count)
        for mat in materials: include((mat,MATERIAL),'helmet material')
        report.append(dict(variant=slot,character=character,source_unit=old,target_unit=new,bones=f'{bones:016x}',materials=[f'{m:016x}' for m in materials],unit_sha256=sha(payload)))
    inventory=(ROOT/'dist/rc-upgrade/all-assets-routing.txt').read_text(encoding='utf-8-sig')
    for dep in external:
        assert f"0x{dep['resource']}.0x{dep['type']}" in inventory,dep
    types=sorted({k[1] for k in selected})
    ordered=sorted(selected,key=lambda k:(types.index(k[1]),k[0]))
    head=bytearray(header)
    struct.pack_into('<II',head,4,len(types),len(ordered))
    type_rows=b''.join(struct.pack('<QQQII',0,t,sum(k[1]==t for k in ordered),16,64) for t in types)
    main_offset=72+len(type_rows)+80*len(ordered)+8
    directory=bytearray()
    outputs=[bytearray(),bytearray(),bytearray()]
    for index,key in enumerate(ordered):
        value=selected[key]
        row=value['row'].copy()
        row[:2]=key
        row[2:7]=[main_offset+len(outputs[0]),len(outputs[1]),len(outputs[2]),0,0]
        row[12]=index
        directory.extend(ENTRY.pack(*row))
        for target,payload in zip(outputs,value['payloads']):
            target.extend(payload+bytes((-len(payload))%16))
    patch=bytes(head)+type_rows+directory+bytes(8)+outputs[0]
    patch+=bytes(max(0,256*len(ordered)-len(patch)))
    files=[]
    for suffix,data in [('',patch),('.stream',outputs[1]),('.gpu_resources',outputs[2])]:
        path=OUT/('9ba626afa44a3aa3.patch_0'+suffix)
        path.write_bytes(data)
        files.append(dict(name=path.name,size=len(data),sha256=sha(data)))
    for row in entries(patch):
        key=tuple(row[:2])
        found=(patch[row[2]:row[2]+row[7]],outputs[1][row[3]:row[3]+row[8]],outputs[2][row[4]:row[4]+row[9]])
        assert found==selected[key]['payloads']
    kits=json.loads((OUT.parent/'all-kits.json').read_text())
    consumers=[dict(id=k['id'],name=k['name'],archive=k['archive']) for k in kits if 'bc20d0b4efff128c' in [p['unit'] for p in k['pieces']]]
    summary=dict(mapping=report,shared_variant1_helmet_consumers=consumers,resource_count=len(selected),types={f'{t:016x}':sum(k[1]==t for k in selected) for t in types},external_base_game_dependencies=external,files=files,runtime_tested=False,source_zip_sha256=sha(source.read_bytes()),validation='Every main/GPU/stream payload byte-identical to Delta donor; only four unit TOC identities remapped; all external dependency IDs found in current game inventory.')
    (OUT/'build-report.json').write_text(json.dumps(summary,indent=2))
    with zipfile.ZipFile(OUT/'Republic Commando Starter Helmets.zip','w',compression=zipfile.ZIP_DEFLATED) as z:
        for item in files: z.write(OUT/item['name'],'RC Starter Helmets/'+item['name'])
        z.writestr('README.txt','B-01 variants1/2/3/4: Sev/Fixer/Scorch/Boss. Cosmetic helmet replacement only. Runtime validation pending.\nBase helmet unit is shared: B01 recolors, SA-25 Steel Trooper, B-22 Model Citizen, B-16 Battle-Scarred Veteran, TR-7 Ambassador of the Brand and TR-40 Gold Eagle also display Sev.\n')
    print(json.dumps({k:summary[k] for k in ['resource_count','types','files','runtime_tested']},indent=2))
    return 0

if __name__=='__main__':
    raise SystemExit(main())
