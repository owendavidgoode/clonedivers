#!/usr/bin/env python3
"""Regressions for external geometry closure, including actual v3/v4 payloads."""
from __future__ import annotations

from pathlib import Path
import struct
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import empire_port as P
import verify_candidate as V
from build_v4 import resources_at


class GeometryProviderTests(unittest.TestCase):
    def setUp(self) -> None:
        raw = bytearray(116)
        struct.pack_into('<Q', raw, 16, 2)
        self.unit = (1, P.UNIT)
        self.provider = (2, V.GEOMETRY_GROUP)
        self.resources = {self.unit: P.Generated((bytes(raw), b'', b''))}
        self.index = {f'{2:016x}.{V.GEOMETRY_GROUP:016x}': [{'archive': 'native-family'}]}
        gg = bytearray(64)
        struct.pack_into('<II', gg, 8, 1, 44)
        struct.pack_into('<Q', gg, 24, 1)
        struct.pack_into('<I', gg, 32, 40)
        self.gg = bytes(gg)

    def test_native_index_does_not_prove_residency(self) -> None:
        result = V.geometry_provider_scan(self.resources, self.index)
        self.assertFalse(result['passed'])
        self.assertEqual(result['findings'][0]['nativeFamilies'], ['native-family'])

    def test_unknown_geometry_id_is_rejected(self) -> None:
        self.assertFalse(V.geometry_provider_scan(self.resources, {})['passed'])

    def test_included_provider_passes(self) -> None:
        self.resources[self.provider] = P.Generated((self.gg, b'', b''))
        self.assertTrue(V.geometry_provider_scan(self.resources, self.index)['passed'])

    def test_wrong_owner_is_rejected(self) -> None:
        gg = bytearray(self.gg)
        struct.pack_into('<Q', gg, 24, 99)
        self.resources[self.provider] = P.Generated((bytes(gg), b'', b''))
        result = V.geometry_provider_scan(self.resources, self.index)
        self.assertFalse(result['passed'])
        self.assertIn('owner UNIT', result['findings'][0]['reason'])

    def test_nonunit_consumer_cannot_bypass_closure(self) -> None:
        resources = {(3, 4): P.Generated((b'prefix' + struct.pack('<Q', 2), b'', b''))}
        self.assertFalse(V.geometry_provider_scan(resources, self.index)['passed'])

    def test_missing_mesh_name_is_rejected(self) -> None:
        raw = bytearray(self.resources[self.unit].parts()[0])
        raw.extend(bytes(28))
        struct.pack_into('<I', raw, 100, 116)
        struct.pack_into('<III', raw, 116, 1, 16, 0x1234)
        self.resources[self.unit] = P.Generated((bytes(raw), b'', b''))
        self.resources[self.provider] = P.Generated((self.gg, b'', b''))
        result = V.geometry_provider_scan(self.resources, self.index)
        self.assertFalse(result['passed'])
        self.assertIn('mesh names', result['findings'][0]['reason'])

    def test_missing_provider_gpu_bytes_are_rejected(self) -> None:
        gg = bytearray(self.gg)
        gg.extend(bytes(436))
        struct.pack_into('<II', gg, 44, 1, 8)
        struct.pack_into('<4I', gg, 52 + 416, 0, 1, 0, 0)
        self.resources[self.provider] = P.Generated((bytes(gg), b'', b''))
        result = V.geometry_provider_scan(self.resources, self.index)
        self.assertFalse(result['passed'])
        self.assertIn('GPU extent', result['findings'][0]['reason'])

    def test_actual_v3_fails_with_both_capes(self) -> None:
        result = V.geometry_provider_scan(resources_at(ROOT / 'empire-forge-r45-v3'),
                                          P.read(ROOT / 'native-index-25480438.json'))
        self.assertFalse(result['passed'])
        self.assertEqual(result['resourcesScanned'], 6780)
        self.assertEqual(result['unitsScanned'], 905)
        self.assertEqual({r['owner'] for r in result['findings']},
                         {f'{name:016x}.{P.UNIT:016x}' for name in (0x3c33cf10a26cbb3e, 0xea846ef460cc5ba0)})
        self.assertTrue(any('ed9eeb61' in r['meshNames'] for r in result['findings']))

    def test_actual_v4_passes_and_exactly_restores_two_units(self) -> None:
        resources = resources_at(ROOT / 'empire-forge-r45-v4')
        result = V.geometry_provider_scan(resources, P.read(ROOT / 'native-index-25480438.json'))
        self.assertTrue(result['passed'], result['findings'])
        self.assertEqual(len(result['references']), 1)
        before = P.read(ROOT / 'expected-resources.json')
        after = P.read(ROOT / 'triage-r45/v4/expected-resources.json')
        changed = {key for key in before.keys() | after.keys() if before.get(key) != after.get(key)}
        self.assertEqual(changed, {f'{name:016x}.{P.UNIT:016x}' for name in (0x3c33cf10a26cbb3e, 0xea846ef460cc5ba0)})
        baseline = P.baseline()
        for name in (0x3c33cf10a26cbb3e, 0xea846ef460cc5ba0):
            parts = resources[name, P.UNIT].parts()
            self.assertEqual(parts, baseline[name, P.UNIT].parts())
            self.assertEqual(struct.unpack_from('<Q', parts[0], 16)[0], 0)
            self.assertTrue(parts[2])


if __name__ == '__main__':
    unittest.main()
