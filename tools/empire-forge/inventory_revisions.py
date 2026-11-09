#!/usr/bin/env python3
"""Record the retained revision manifests and cross-build archive families."""
import csv
import json
import logging
from pathlib import Path
import re
import sys

import empire_port as P


def main() -> int:
    try:
        revisions = []
        for number in range(20, 42):
            folders = [p for p in (P.OLD / 'dist').glob(f'production-r{number}-*') if 'partial' not in p.name and 'aborted' not in p.name]
            candidates = []
            for folder in folders:
                candidates.extend(folder.glob('candidate-v*/manifest.json'))
                candidates.extend(folder.glob('release-ready-v*/manifest.json'))
            if not candidates and folders:
                candidates = list(folders[0].rglob('manifest.json'))
            valid = []
            for path in candidates:
                doc = P.read(path)
                pack = doc.get('pack', {})
                if re.search(fr'-r{number}(?:-|$)', pack.get('version', '')):
                    valid.append((path, pack))
            if not valid:
                revisions.append({'revision': number, 'manifestLocated': False})
                continue
            path, pack = sorted(valid, key=lambda pair: str(pair[0]))[-1]
            revisions.append({'revision': number, 'manifest': P.pin(path), 'version': pack['version'],
                              'declaredBuild': pack.get('gameBuild'), 'notes': pack.get('notes'),
                              'statusNotes': pack.get('statusNotes'), 'logicalFiles': len(pack.get('files', []))})
        for number, area, candidate in ((42, 'hd2-update-2026-10-21', 'candidate-v16'),
                                        (43, 'hd2-update-2026-10-25', 'candidate-v33'),
                                        (44, 'hd2-update-2026-10-25', 'candidate-v66'),
                                        (69, 'hd2-update-2026-10-25', 'candidate-v69'),
                                        (72, 'hd2-update-2026-10-25', 'candidate-v72'),
                                        (75, 'hd2-update-2026-10-25', 'candidate-v75'),
                                        (76, 'hd2-update-2026-10-25', 'candidate-v76')):
            path = P.OLD / 'dist' / area / candidate / 'manifest.json'
            pack = P.read(path)['pack']
            revisions.append({'revision': number, 'manifest': P.pin(path), 'version': pack['version'],
                              'declaredBuild': pack['gameBuild'], 'notes': pack.get('notes'),
                              'statusNotes': pack.get('statusNotes'), 'logicalFiles': len(pack['files'])})
        P.save('revision-inventory.json', revisions)
        forge = P.read(P.ROOT / 'native-index-25480438.json')
        steam = P.read(P.OLD / 'dist/hd2-update-2026-10-29/native-index-v1/native-index.json')
        inv = P.read(P.ROOT / 'input-inventory.json')
        keys = {r['key'] for r in inv['unitHeaders']}
        keys.update(r['key'] for r in inv['newerNativeKeysAbsentOnForge'])
        keys.update(r['key'] for r in P.read(P.ROOT / 'port-decisions.json')['resources'])
        keys.update(P.read(P.ROOT / 'expected-resources.json'))
        rows = []
        for key in sorted(keys):
            left = sorted({p['archive'] for p in forge.get(key, [])})
            right = sorted({p['archive'] for p in steam.get(key, [])})
            rows.append({'key': key, 'forge25480438Families': '|'.join(left), 'steam26251122Families': '|'.join(right),
                         'sameFamilySet': left == right, 'forgeNativePresent': bool(left), 'steamNativePresent': bool(right)})
        with (P.ROOT / 'cross-build-families.csv').open('w', newline='', encoding='utf-8') as handle:
            writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)
        P.save('revision-inventory-summary.json', {'manifests': len(revisions), 'missing': [r['revision'] for r in revisions if r.get('manifestLocated') is False],
                                                  'comparedTypedKeys': len(rows), 'differentFamilySets': sum(not r['sameFamilySet'] for r in rows),
                                                  'bothNativeFamiliesDiffer': sum(r['forgeNativePresent'] and r['steamNativePresent'] and not r['sameFamilySet'] for r in rows)})
        print(json.dumps(P.read(P.ROOT / 'revision-inventory-summary.json')))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception('Revision inventory failed')
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
