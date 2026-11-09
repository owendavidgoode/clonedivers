#!/usr/bin/env python3
"""Rehash the delivered packs and retained evidence without modifying game files."""
from __future__ import annotations

import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import empire_port as P


def main() -> int:
    out = ROOT / 'triage-r45'
    variants = []
    source = P.read(ROOT / 'candidate-files.json')
    if [P.pin(Path(p['path'])) for p in source] != source:
        raise ValueError('Source r45 pack changed')
    for name in ('a-medium-r19', 'b-native-capes', 'c-cape-geometry', 'd-native-text'):
        evidence = out / name
        files = P.read(evidence / 'candidate-files.json')
        if [P.pin(Path(p['path'])) for p in files] != files:
            raise ValueError(f'Delivered pack changed after verification {name}')
        variants.append({'name': name, 'build': P.pin(evidence / 'build.json'),
                         'verification': P.pin(evidence / 'verification-summary.json'),
                         'files': len(files), 'bytes': sum(p['size'] for p in files), 'allFilePinsMatch': True})
    inputs = [ROOT / name for name in ('r19-resource-diff.json', 'port-decisions.json', 'native-index-25480438.json',
                                      'verify_candidate.py', 'empire_port.py', 'ship-helper-evidence.json')]
    inputs += [Path(P.read(out / f'{name}.json')['dump']) for name in ('hellforge-dump', 'game-dump', 'second-game-dump', 'third-game-dump')]
    inputs += [P.OLD / 'dist/rc-upgrade/filediver-source' / name for name in ('hashes/hashes.txt', 'hashes/thinhashes.txt', 'stingray/hash.go', 'stingray/unit/unit.go')]
    report = {'passed': True, 'sourcePackUnchanged': True, 'variants': variants, 'inputs': [P.pin(p) for p in inputs],
              'runtimeTested': False, 'gitRepository': False}
    (out / 'delivery-audit.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'passed': True, 'sourcePackUnchanged': True, 'variantCount': len(variants),
                      'totalVariantBytes': sum(v['bytes'] for v in variants)}))
    return 0


if __name__ == '__main__':
    sys.exit(main())
