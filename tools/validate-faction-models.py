#!/usr/bin/env python3
"""Decode faction models with Filediver in a separate stock-asset view.

Reuses the existing marker validator's DSAR and glTF readers. Zero-triangle
weapon hiders are checked structurally and excluded from Filediver's renderer.
Dependencies: numpy, Pillow. A game view contains read-only-use stock hard links.
"""

import argparse
import importlib.util
import json
import logging
import struct
import subprocess
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]


def module(name: str, filename: str):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools' / filename)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


M = module('faction_validation', 'validate-aim-markers.py')
V = module('faction_vehicle', 'build-vehicle-followup.py')


def validate(patch: Path, game_view: Path, output: Path) -> None:
    if output.exists():
        raise ValueError('Choose a fresh validation output directory')
    output.mkdir(parents=True)
    data = game_view / 'data'
    M.game_view(data)
    entries, visible, hidden, omitted_meshes = [], {}, [], []
    for index, (identity, kind, main, gpu) in enumerate(M.archive_rows(patch)):
        if kind not in (M.UNIT_TYPE, 0xcd4238c6a0c69e32, 0xeac0b497876adedf, 0x18dead01056b72e9):
            continue
        if kind == M.UNIT_TYPE:
            layouts = V.pointers(main, 92)
            for at in layouts:
                for offset, size in ((416, 420), (424, 428)):
                    if V.u32(main, at + offset) + V.u32(main, at + size) > len(gpu):
                        raise ValueError('Geometry buffer exceeds GPU payload')
            groups = []
            export_meshes = []
            for at in V.pointers(main, 100):
                mesh_groups = [struct.unpack_from('<6I', main, at + V.u32(main, at + 124) + i * 24)
                               for i in range(V.u32(main, at + 120))]
                groups.extend(mesh_groups)
                layout_index = struct.unpack_from('<i', main, at + 60)[0]
                if not 0 <= layout_index < len(layouts):
                    raise ValueError('Invalid mesh layout reference')
                layout = layouts[layout_index]
                if not V.u32(main, layout + 392):
                    if V.u32(main, layout + 428) or any(group[4] for group in mesh_groups):
                        raise ValueError('Empty index buffer has nonempty draw groups')
                else:
                    export_meshes.append(at)
            if not any(group[4] for group in groups):
                for at in V.pointers(main, 92):
                    if V.u32(main, at + 392) or V.u32(main, at + 428):
                        raise ValueError('Hidden mesh still declares indices')
                    if V.u32(main, at + 416) + V.u32(main, at + 420) > len(gpu):
                        raise ValueError('Hidden mesh has an invalid vertex range')
                hidden.append({'id': f'{identity:016x}', 'triangles': 0,
                               'check': 'Zero draw groups and index buffers; not a renderable mesh.'})
                continue
            # Filediver divides by zero for mixed visible/zero-index layouts.
            # Omit only those empty meshes from its temporary inspection copy;
            # the candidate patch is never changed by this export workaround.
            removed = len(V.pointers(main, 100)) - len(export_meshes)
            if removed:
                edited = bytearray(main)
                table = V.u32(main, 100)
                struct.pack_into('<I', edited, table, len(export_meshes))
                # Filediver only reads this companion table's mesh count.
                mesh_data = V.u32(main, 96)
                if mesh_data:
                    struct.pack_into('<I', edited, mesh_data, len(export_meshes))
                for mesh_index, at in enumerate(export_meshes):
                    struct.pack_into('<I', edited, table + 4 + mesh_index * 4, at - table)
                main = bytes(edited)
                omitted_meshes.append({'id': f'{identity:016x}', 'emptyMeshes': removed,
                                       'reason': 'Filediver zero-index division workaround, export copy only'})
            fake = 0x5eed00000000c000 + index
            visible[fake] = identity
            identity = fake
        entries.append((identity, kind, main, gpu))
    M.write_bundle(data / '5eed000000000001', entries)
    with (output / 'filediver.log').open('w', encoding='utf-8') as log:
        run = subprocess.run([str(M.FILEDIVER), '-g', str(game_view), '-i', '0x5eed00000000c*',
                              '--model-format', 'glb', '--model-include-lods', '--model-include-gibs',
                              '-o', str(output / 'export')], stdout=log, stderr=subprocess.STDOUT, check=False)
    reports = []
    for fake, identity in visible.items():
        path = output / 'export' / f'0x{fake:016x}.unit.glb'
        if not path.exists():
            raise ValueError(f'Filediver could not decode {identity:016x}; see log')
        doc, binary = M.load_glb(path)
        # glTF permits primitives with the default material (no material key).
        primitives = []
        for mesh in doc['meshes']:
            for primitive in mesh['primitives']:
                values = {key: M.accessor(doc, binary, accessor)
                          for key, accessor in primitive['attributes'].items()}
                values['indices'] = M.accessor(doc, binary, primitive['indices']).ravel()
                primitives.append(values)
        triangles = 0
        for primitive in primitives:
            positions, indices = primitive['POSITION'], primitive['indices']
            if not np.isfinite(positions).all() or (indices.size and indices.max() >= len(positions)):
                raise ValueError('Nonfinite positions or invalid indices')
            for key, value in primitive.items():
                if key.startswith(('TEXCOORD', 'WEIGHTS')) and not np.isfinite(value).all():
                    raise ValueError('Nonfinite UVs or skin weights')
            triangles += len(indices) // 3
        if not triangles:
            raise ValueError('Expected visible geometry')
        reports.append({'id': f'{identity:016x}', 'meshes': len(doc['meshes']),
                        'primitives': len(primitives), 'trianglesAcrossLods': triangles,
                        'glb': str(path), 'sha256': V.V.sha(path.read_bytes())})
    if run.returncode:
        raise ValueError('Filediver returned a failure status')
    report = {'patchSha256': V.V.sha(patch.read_bytes()), 'decodedModels': reports,
              'hiddenModels': hidden, 'exportOnlyOmissions': omitted_meshes,
              'runtimeTested': False, 'published': False}
    (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    logging.info('Decoded %d models; checked %d intentional zero-triangle hiders', len(reports), len(hidden))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('patch', type=Path)
    parser.add_argument('--game-view', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s')
    try:
        validate(args.patch, args.game_view, args.out)
        return 0
    except (OSError, ValueError, KeyError, struct.error):
        logging.exception('Model validation failed')
        return 1
    except KeyboardInterrupt:
        return 130


if __name__ == '__main__':
    sys.exit(main())
