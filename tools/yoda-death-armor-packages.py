#!/usr/bin/env python3
"""Read the actual native LEGO kit PACKAGE dependency resources without editing."""
from __future__ import annotations

import argparse
from collections import Counter
from collections.abc import Sequence
import functools
import hashlib
import importlib.util
import json
import logging
from pathlib import Path
import struct
import sys
from typing import Any

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'dist/empire-yoda-death-2026-10-08/armor/packages-v1'
PACKAGE=0xad9c6d9ed1e5e77a


def require(value: bool,message: str) -> None:
    if not value:
        raise ValueError(message)


def pin(path: Path) -> dict[str,Any]:
    with path.open('rb') as handle:
        checksum=hashlib.file_digest(handle,'sha256').hexdigest()
    return {'path':str(path.resolve()),'bytes':path.stat().st_size,'sha256':checksum}


def module(name: str,file: str) -> Any:
    spec=importlib.util.spec_from_file_location(name,ROOT/'tools'/file)
    require(spec is not None and spec.loader is not None,'Import '+file)
    result=importlib.util.module_from_spec(spec)
    sys.modules[name]=result
    spec.loader.exec_module(result)
    return result


def run() -> dict[str,Any]:
    require(not OUT.exists(),'Fresh package source output')
    F=module('yoda_package_native','finish-armor-survey.py')
    source=ROOT/'dist/empire-yoda-death-2026-10-08/armor/native-kits-v1/report.json'
    require(pin(source)['sha256']=='59abe1cf3b093dc5f3c4a8ad4cb93ebb67965be2d9db9fe416a473701b7e3c74','Exact native category records')
    kits=json.loads(source.read_text())['targetKitRecords']
    reader=F.S.N.NativeSlim(F.S.GAME)
    reader.chunk=functools.lru_cache(maxsize=4)(reader.chunk)
    catalog_path=ROOT/'dist/intro-1.7.1/all-native.txt'
    catalog=F.S.C.load_catalog(catalog_path)
    catalog={key:{**row,'archives':[archive for archive in row['archives'] if archive in reader.archives or (F.S.GAME/archive).exists()]} for key,row in catalog.items()}
    hashes_path=ROOT/'dist/rc-upgrade/filediver-source/hashes/hashes.txt'
    names={F.A.H.murmur64(n):n for n in hashes_path.read_text().splitlines() if n and not n.startswith('//')}
    inputs=F.I.Inputs(ROOT/'dist/production-r22-2026-10-08/release-ready-v1/manifest.json')
    selector_path=ROOT/'dist/production-r22-2026-10-08/inputs-v1/production96.json'
    selector=json.loads(selector_path.read_text())
    state=next(s for s in selector['selections'] if s['mode']=='EmpireDivers' and s['profile']=='full' and s['effectiveOptions']['droids'] and not s['effectiveOptions']['covenant'])
    rows=selector['fileSets'][state['fileSet']]
    keys={(int(k['archive'],16),PACKAGE) for k in kits}
    winners=F.scan_winners(rows,inputs,keys)
    results=[]
    for kit in kits:
        key=(int(kit['archive'],16),PACKAGE)
        native=F.J.H.native_resource(reader,catalog,key)
        resource=inputs.payload(winners[key],key) if key in winners else native.payloads[0]
        require(not native.payloads[1] and not native.payloads[2],'Native PACKAGE is MAIN-only')
        magic,unknown0,count,unknown1=struct.unpack_from('<4s3I',resource)
        require(count<100000 and len(resource)==16+count*16,'Primary PACKAGE header and all dependency entries exact')
        entries=[{'type':f'{typ:016x}','typeName':names.get(typ),'name':f'{identity:016x}','resourceName':names.get(identity)} for typ,identity in struct.iter_unpack('<QQ',resource[16:])]
        require(len({(e['type'],e['name']) for e in entries})==len(entries),'Unique native package resources')
        package_units={e['name'] for e in entries if int(e['type'],16)==F.A.UNIT}
        bound_units={p['unit'] for p in kit['pieces']}
        missing=sorted(bound_units-package_units)
        results.append({'equipmentId':kit['equipmentId'],'category':kit['category'],'label':kit['label'],
                        'packageResource':f'{key[0]:016x}.{key[1]:016x}','nativeProviders':catalog[key]['archives'],
                        'selectedReplacement':winners.get(key),'selectedPayloadSha256':hashlib.sha256(resource).hexdigest(),
                        'selectedMainBytes':len(resource),'exactNativePayload':resource==native.payloads[0],
                        'header':{'magicHex':magic.hex(),'unknown0':unknown0,'fileCount':count,'unknown1':unknown1},
                        'typeCounts':dict(Counter(e['typeName'] or e['type'] for e in entries)),
                        'equipmentUnitReferences':{'nativeAssociationUnits':len(bound_units),'presentInPackage':len(bound_units&package_units),'notDirectlyListed':missing},
                        'dependencies':entries})
    primary=ROOT/'dist/rc-upgrade/filediver-source/stingray/package/package.go'
    output={'passed':True,'tool':pin(Path(__file__)),'nativeKitProof':pin(source),'nativeCatalog':pin(catalog_path),'actualSelector':pin(selector_path),
            'primaryPackageSchema':pin(primary),'packages':results,
            'scope':'Current native kit PACKAGE resources decoded read-only. PACKAGE residency differs from archive resource presence; no package has been edited.',
            'bankRoutePreference':'A unique additive Event/Action/Sound in an already effective resident player BANK can avoid custom package dependencies, subject to independent bank preservation QA.',
            'packageChanged':False,'bankChanged':False,'gameOrSettingsChanged':False,'runtimeAccepted':False}
    OUT.mkdir(parents=True)
    target=OUT/'report.json'
    target.write_text(json.dumps(output,indent=2)+'\n',encoding='utf-8')
    return {'report':pin(target),'packages':[{k:r[k] for k in ('equipmentId','packageResource','exactNativePayload','header','typeCounts','equipmentUnitReferences')} for r in results]}


def main(argv: Sequence[str] | None=None) -> int:
    argparse.ArgumentParser(description=__doc__.splitlines()[0]).parse_args(argv)
    logging.basicConfig(level=logging.INFO,format='%(levelname)s: %(message)s')
    try:
        print(json.dumps(run()))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception('Native armor packages refused')
        return 1


if __name__=='__main__':
    sys.exit(main())
