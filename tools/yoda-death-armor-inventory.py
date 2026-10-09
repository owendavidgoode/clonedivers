#!/usr/bin/env python3
"""Bind LEGO death-audio identity gates to actual r22 selected native UNITs.

Inventories equipment categories, both body types and private skeleton markers.
No equipment association, bank, manifest or installed asset is changed.
"""
from __future__ import annotations

import argparse
from collections.abc import Sequence
import hashlib
import importlib.util
import json
import logging
from pathlib import Path
import struct
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'dist/empire-yoda-death-2026-10-08/armor/inventory-v1'
R22 = ROOT/'dist/production-r22-2026-10-08'
MANIFEST = R22/'release-ready-v1/manifest.json'
SELECTOR = R22/'inputs-v1/production96.json'
KIT_PATH = ROOT/'dist/player-update-2026-09-23/current-kits/all-kits.json'
STYLES = {'storm': ('DP-40 Hero of the Federation', 'LEGO Stormtrooper'),
          'beach': ('AF-02 Haz-Master', 'LEGO Bikini Stormtrooper')}
SLOTS = {0:'helmet',1:'shared_skeleton',2:'torso',3:'hips',4:'left_leg',5:'right_leg',6:'left_arm',7:'right_arm',8:'left_shoulder',9:'right_shoulder'}
BODY = {0:'brawny',1:'lean',2:'unknown',3:'any'}
TYPE = {0:'armor',1:'undergarment',2:'accessory'}


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
    require(not OUT.exists(), 'Fresh LEGO identity evidence')
    F = module('yoda_armor_inputs', 'finish-armor-survey.py')
    C = module('yoda_armor_components', 'audit-weapon-audio.py')
    A, I = F.A, F.I
    require(pin(SELECTOR)['sha256'] == '9652cd9331deb344c043b0c401a0973c226a16a1d89899e2f3458f2f38e797bb', 'Exact reviewed r22 actual selector')
    manifest = json.loads(MANIFEST.read_text())
    selected = json.loads(SELECTOR.read_text())
    input_manifest = json.loads((R22/'inputs-v1/manifest.json').read_text())
    require(manifest['pack']['version'] == '2026.10.08-r22', 'Actual public r22')
    require(manifest['pack']['files'] == input_manifest['pack']['files'], 'Public r22 payload rows match actual selector input')
    kits = json.loads(KIT_PATH.read_text())
    source_reports = {style: json.loads((ROOT/'dist/empire-next-2026-10-05/armor/lego-next'/f'{style}-candidate-v2/report.json').read_text()) for style in STYLES}
    expected = {int(u['unit'],16): u for report in source_reports.values() for u in report['units']}
    require(len(expected) == 26, 'Exactly 26 authored visual LEGO UNITs')
    targets = [k for k in kits if k['name'] in {value[0] for value in STYLES.values()} and k['kind'] in (0,1)]
    keys = {(int(p['unit'],16), A.UNIT) for k in targets for p in k['pieces']}
    inputs = I.Inputs(MANIFEST)
    profile_results = {}
    raw_units = {}
    for profile in ('full','lighter'):
        state = next(s for s in selected['selections'] if s['mode'] == 'EmpireDivers' and s['profile'] == profile and s['effectiveOptions']['droids'] and not s['effectiveOptions']['covenant'])
        rows = selected['fileSets'][state['fileSet']]
        winners = F.scan_winners(rows, inputs, keys)
        profile_results[profile] = {'selection': state, 'winningUnitRows': {f'{key[0]:016x}': row for key,row in winners.items()}}
        for key, row in winners.items():
            payload = inputs.payload(row,key)
            if key[0] in raw_units:
                require(raw_units[key[0]] == payload, 'Full/Lighter LEGO MAIN identity exact')
            raw_units[key[0]] = payload
    require(set(expected) <= set(raw_units), 'Every visual LEGO UNIT resolves in both profiles')
    parts = []
    marker_sets = {}
    for style, (native_name, label) in STYLES.items():
        report = source_reports[style]
        marker_sets[style] = {}
        for u in report['units']:
            identity = int(u['unit'],16)
            actual = raw_units[identity]
            require(hashlib.sha256(actual).hexdigest() == u['candidatePayloadSha256s'][0], 'Actual selected LEGO geometry MAIN matches authored source')
            hashes, parents, _ = A.joint_data(actual)
            joints = []
            for j in u['rig']['sourceAppendJoints']:
                require(hashes[j['newIndex']] == j['thinHashInteger'] and parents[j['newIndex']] == j['parent'], 'Actual native skeleton marker and parent')
                full_name = f"content/codex_empire_lego_cosmetic_20261005/{style}/bone/{j['sourceName']}"
                require(A.H.murmur64(full_name)>>32 == j['thinHashInteger'], 'Native source marker hash formula')
                joints.append({'name': full_name, 'thinHash': j['thinHash'], 'sourceName': j['sourceName'], 'jointIndex': j['newIndex']})
            piece = u['equipment']
            consumers = [{'kit': k['name'], 'equipmentId': k['id'], 'kind': k['kind'], 'piece': p} for k in kits for p in k['pieces'] if p['unit'] == u['unit']]
            item = {'style': style, 'label': label, 'nativeKit': native_name, 'unit': u['unit'], 'role': u['role'], 'piece': piece,
                    'pieceType': TYPE[piece['piece_type']], 'bodyType': BODY[piece['body_type']],
                    'actualMainSha256': hashlib.sha256(actual).hexdigest(), 'actualMainBytes': len(actual),
                    'sourceCandidateMainExact': True, 'privateSkeletonMarkers': joints, 'allEquipmentConsumers': consumers}
            parts.append(item)
            if piece['slot'] == 2:
                marker_sets[style][BODY[piece['body_type']]] = joints
    shared_skeleton = raw_units.get(0x3c33cf10a26cbb3e)
    require(shared_skeleton is not None, 'Shared avatar skeleton resolved separately')
    shared_hashes = set(A.joint_data(shared_skeleton)[0])
    require(not any(int(m['thinHash'],16) in shared_hashes for style in marker_sets.values() for markers in style.values() for m in markers), 'Generic avatar shared skeleton rejects every positive LEGO marker')
    components = C.Components(ROOT/'dist/weapon-audio-2026-10-03/filediver-v0.7.53')
    unit_rows = components.rows('UnitComponent')
    aliases = {k: struct.unpack_from('<Q',v)[0] for k,v in unit_rows.items()}
    relevant = set(raw_units) | {k for k,v in aliases.items() if v in raw_units}
    categories = {}
    for category in ('HealthComponent','AvatarComponent','EquipmentComponent','AnimationComponent','VisibilityMaskComponent'):
        rows = components.rows(category)
        categories[category] = [{'entity': f'{identity:016x}', 'unit': f'{aliases[identity]:016x}' if identity in aliases else None, 'sha256': C.sha(rows[identity])} for identity in sorted(relevant & rows.keys())]
    avatar_path = 'content/fac_helldivers/cha_avatar/avatar_helldiver'
    avatar_id = A.H.murmur64(avatar_path)
    avatar_size, avatar_fields = components.type('AvatarComponent')
    vo_size, vo_fields = components.type('ExertionVoParams')
    vo_field = next(f for f in avatar_fields if f['type'] == C.dl_hash('ExertionVoParams'))
    require(vo_fields[0]['offset'] == 0 and vo_fields[0]['size'] == 4, 'Primary OnDeath event field')
    avatar_raw = components.rows('AvatarComponent')[avatar_id]
    require(len(avatar_raw) == avatar_size, 'Typed avatar bytes')
    vo_death = struct.unpack_from('<I', avatar_raw, vo_field['offset'])[0]
    health_size, health_fields = components.type('HealthComponent')
    health_raw = components.rows('HealthComponent')[avatar_id]
    require(len(health_raw) == health_size, 'Typed health bytes')
    death_sound = [f for f in health_fields if f['atom'] == 2 and f['storage'] == 6 and f['count'] == 10 and f['size'] == 40]
    require(len(death_sound) == 1, 'Unique primary ten-element real-death sound array')
    death_values = list(struct.unpack_from('<10I', health_raw, death_sound[0]['offset']))
    death_anim_size, death_anim_fields = components.type('DeathAnimationSound')
    death_anim_array = next(f for f in health_fields if f['type'] == C.dl_hash('DeathAnimationSound') and f['count'] == 8)
    death_anim = [list(struct.unpack_from('<2I',health_raw,death_anim_array['offset']+i*death_anim_size)) for i in range(8)]
    association_rows = [{'id': k['id'], 'nativeName': k['name'], 'label': STYLES['storm'][1] if k['name']==STYLES['storm'][0] else STYLES['beach'][1],
                         'kind': k['kind'], 'category': 'armor' if k['kind']==0 else 'helmet', 'archive': k['archive'],
                         'pieces': [{**p, 'slotName': SLOTS[p['slot']], 'pieceCategory': TYPE[p['piece_type']], 'bodyType': BODY[p['body_type']],
                                     'authoredLegoVisualUnit': int(p['unit'],16) in expected} for p in k['pieces']]} for k in targets]
    primary = ROOT/'dist/rc-upgrade/filediver-source'
    report = {'passed': True, 'tool': pin(Path(__file__)), 'publicManifest': pin(MANIFEST), 'actualSelector': pin(SELECTOR),
              'equipmentAssociations': pin(KIT_PATH), 'equipmentAssociationSnapshotBuild': '25327279', 'actualPayloadBuild': manifest['pack']['gameBuild'],
              'styles': association_rows, 'visualUnitCount': len(parts), 'units': parts, 'torsoPositiveMarkers': marker_sets,
              'profileWinners': profile_results, 'sourceMainInputs': list(inputs.pins.values()),
              'sharedSkeletonIsNeverPositiveLegoGate': {'unit': '3c33cf10a26cbb3e', 'mainSha256': hashlib.sha256(shared_skeleton).hexdigest(), 'privateMarkersAbsent': True,
                                                      'consumerCount': sum(1 for k in kits for p in k['pieces'] if p['unit']=='3c33cf10a26cbb3e')},
              'decodedSnapshotPerEquipmentComponentCategories': categories,
              'nativeDeathEventSource': {'owner': f'{avatar_id:016x}', 'path': avatar_path,
                                        'avatarExertionVoiceOnDeath': f'{vo_death:08x}', 'avatarExertionVoOffset': vo_field['offset'],
                                        'healthDeathSoundIds': [f'{v:08x}' for v in death_values], 'healthDeathSoundArrayOffset': death_sound[0]['offset'],
                                        'healthDeathAnimationSounds': [[f'{x:08x}' for x in row] for row in death_anim],
                                        'scope': 'Generic avatar entity, not either LEGO equipment kit.'},
              'primarySources': [pin(primary/n) for n in ('datalibrary/armor_sets.go','datalibrary/avatar_component.go','datalibrary/health_component.go','datalibrary/equipment_component.go','stingray/unit/unit.go')],
              'routingBound': {'noDecodedArmorKitDeathAudioField': True, 'noEquipmentSpecificHealthOrAvatarOwnerProven': True,
                               'geometryUnitIdentityDoesNotChangeGenericAvatarDeathOwner': True,
                               'assetOnlyPerLegoDeathSoundRouteProven': False,
                               'implementableRuntimeIdentityInput': 'Require dead avatar emitter plus positively matched equipped torso private helper or positively identified authored body UNIT; fail closed without equipment identity.',
                               'helmetAloneDoesNotProveLegoBody': True},
              'priorDamageRoute': pin(ROOT/'dist/production-r22-2026-10-08/armor/damage-route-v2/report.json'),
              'nativeControlDecryptionCurrent': False, 'manifestOrEquipmentAssociationsChanged': False,
              'gameOrSettingsChanged': False, 'runtimeAccepted': False}
    OUT.mkdir(parents=True)
    target = OUT/'report.json'
    target.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    return {'report': pin(target), 'visualUnits': len(parts), 'torsoMarkers': marker_sets, 'nativeDeath': report['nativeDeathEventSource'], 'componentCategoryCounts': {k:len(v) for k,v in categories.items()}}


def main(argv: Sequence[str] | None = None) -> int:
    argparse.ArgumentParser(description=__doc__.splitlines()[0]).parse_args(argv)
    logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s')
    try:
        print(json.dumps(run()))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception('LEGO death identity inventory refused')
        return 1


if __name__ == '__main__':
    sys.exit(main())
