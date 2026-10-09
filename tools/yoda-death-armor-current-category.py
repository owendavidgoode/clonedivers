#!/usr/bin/env python3
"""Cross-check LEGO applied-armor IDs against current source-derived kit metadata."""
from __future__ import annotations

import argparse
from collections.abc import Sequence
import hashlib
import importlib.util
import json
import logging
from pathlib import Path
import sys
from typing import Any
import zipfile

ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT/'dist/empire-yoda-death-2026-10-08'
OUT=BASE/'armor/current-category-v1'


def require(value: bool,message: str) -> None:
    if not value:
        raise ValueError(message)


def pin(path: Path) -> dict[str,Any]:
    with path.open('rb') as handle:
        digest=hashlib.file_digest(handle,'sha256').hexdigest()
    return {'path':str(path.resolve()),'bytes':path.stat().st_size,'sha256':digest}


def load(name: str,path: Path) -> Any:
    spec=importlib.util.spec_from_file_location(name,path)
    require(spec is not None and spec.loader is not None,'Offline Lua runner')
    value=importlib.util.module_from_spec(spec)
    sys.modules[name]=value
    spec.loader.exec_module(value)
    return value


def run() -> dict[str,Any]:
    require(not OUT.exists(),'Fresh current-source category proof')
    primary_path=BASE/'runtime/primary-v1/report.json'
    require(pin(primary_path)['sha256']=='68af5c3067afd3c9c99939fd89068f0082403152080b12fdf93a59fe0823759d','Sealed current primary source')
    primary=json.loads(primary_path.read_text())
    archive=Path(primary['archive']['path'])
    require(pin(archive)==primary['archive'],'Exact primary source ZIP')
    source=BASE/'runtime/primary-v1/source/domains/player_passives.lua'
    with zipfile.ZipFile(archive) as bundle:
        files=[n for n in bundle.namelist() if n.endswith('/domains/player_passives.lua')]
        require(len(files)==1 and bundle.read(files[0])==source.read_bytes(),'Source-derived data module is exact official archived blob')
    runner_path=ROOT/'dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d/sdk/tools/lua_runner.py'
    vm=load('yoda_current_category_lua',runner_path)
    # This executes the data-only generated domain, not the SDK native APIs.
    code='local d=(function()\n'+source.read_text()+'\nend)()\n'+'''
local wanted={['0xB513FD54']=true,['0xE9ADD047']=true,['0x5C3087D2']=true,['0x2F748B84']=true}
local rows={}
for _,k in ipairs(d.kits)do
 if wanted[k.id]then rows[#rows+1]=string.format('{"id":%q,"slot":%q,"name":%q,"index":%d}',k.id,k.slot,k.name,k.index)end
end
return '{"kitCount":'..#d.kits..',"gameDllSha256":'..string.format('%q',d.source.gameDllSha256)..',"recordArmorOffset":'..d.record.armorKit..',"recordHelmetOffset":'..d.record.helmetKit..',"kitTypeOffset":'..d.kit.type..',"appliedStride":'..d.manager.appliedStride..',"kits":['..table.concat(rows,',')..']}'
'''
    current=json.loads(vm.execute(code.encode()))
    require(current['recordArmorOffset']==12 and current['recordHelmetOffset']==4 and current['kitTypeOffset']==40 and current['appliedStride']==68,'Current source exact applied-body fields')
    expected={'0xB513FD54':'armor','0xE9ADD047':'armor','0x5C3087D2':'helmet','0x2F748B84':'helmet'}
    require({k['id']:k['slot'] for k in current['kits']}==expected,'Current source confirms body and helmet categories separately')
    native_path=BASE/'armor/native-kits-v1/report.json'
    require(pin(native_path)['sha256']=='59abe1cf3b093dc5f3c4a8ad4cb93ebb67965be2d9db9fe416a473701b7e3c74','Native record category proof')
    native=json.loads(native_path.read_text())
    require({('0x'+k['equipmentId'].upper()):k['category'] for k in native['targetKitRecords']}==expected,'Independent native DL and current primary categories agree')
    game_path=Path('C:/Program Files (x86)/Steam/steamapps/common/Helldivers 2/data/game/game.dll')
    game_pin=pin(game_path)
    require(game_pin['sha256'].upper()==current['gameDllSha256'],'Installed disk build matches current primary native metadata fingerprint')
    report={'passed':True,'tool':pin(Path(__file__)),'primarySource':pin(primary_path),'officialArchivedModule':pin(source),'offlineDataRunner':pin(runner_path),
            'dataOnlyDomainExecuted':True,'nativeGameApiExecuted':False,'currentSourceMetadata':current,'nativeDLCategoryProof':pin(native_path),
            'installedGameDll':game_pin,'currentDiskFingerprintMatchesPrimary':True,
            'policy':'Read +0x0C of the proven applied customization record as body armor; +0x04 is helmet and cannot qualify. Require kit type +0x28 == Armor(0).',
            'loadedInstructionPinsStillRequired':True,'historicalSnapshotCannotSubstituteForLoadedRuntimeValidation':True,
            'gameOrSettingsChanged':False,'manifestChanged':False,'runtimeAccepted':False}
    OUT.mkdir(parents=True)
    target=OUT/'report.json'
    target.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    return {'report':pin(target),'current':current}


def main(argv: Sequence[str] | None=None) -> int:
    argparse.ArgumentParser(description=__doc__.splitlines()[0]).parse_args(argv)
    logging.basicConfig(level=logging.INFO,format='%(levelname)s: %(message)s')
    try:
        print(json.dumps(run()))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception('Current armor category source refused')
        return 1


if __name__=='__main__':
    sys.exit(main())
