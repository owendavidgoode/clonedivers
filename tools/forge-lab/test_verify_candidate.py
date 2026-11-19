"""Regression checks for verify_candidate.geometry_provider_scan passive LODData count=0 UNITs."""
from pathlib import Path
import struct
import sys
import unittest

EMPIRE_FORGE = Path(__file__).resolve().parents[1] / 'empire-forge'
if str(EMPIRE_FORGE) not in sys.path:
    sys.path.insert(0, str(EMPIRE_FORGE))

import empire_port as P  # noqa: E402
from verify_candidate import GEOMETRY_GROUP, geometry_provider_scan  # noqa: E402


def _make_unit_with_external_gg(gg_id: int, lod_count: int, extra_needle_offset: int | None = None) -> P.Generated:
    raw = bytearray(160)
    struct.pack_into('<Q', raw, 16, gg_id)
    lod_table_off = len(raw)
    struct.pack_into('<I', raw, 48, lod_table_off)
    if lod_count == 0:
        raw.extend(struct.pack('<4I', 0, 0, 0, 0))
    else:
        entry_rel = 4 + 4 * lod_count
        raw.extend(struct.pack('<I', lod_count))
        for i in range(lod_count):
            raw.extend(struct.pack('<I', entry_rel + i * 24))
        for _ in range(lod_count):
            raw.extend(bytes(24))
    if extra_needle_offset is not None:
        if extra_needle_offset + 8 > len(raw):
            raw.extend(bytes(extra_needle_offset + 8 - len(raw)))
        struct.pack_into('<Q', raw, extra_needle_offset, gg_id)
    return P.Generated((bytes(raw), b'', b''))


class GeometryProviderScanTests(unittest.TestCase):
    def test_lod_count_zero_unit_allows_missing_external_provider(self) -> None:
        gg_id = 0xC6A69260CC389BCD
        unit_id = 0x1C8764D62E12925A
        index = {f'{gg_id:016x}.{GEOMETRY_GROUP:016x}': [{'archive': '0123456789abcdef'}]}
        resources = {(unit_id, P.UNIT): _make_unit_with_external_gg(gg_id, lod_count=0)}

        result = geometry_provider_scan(resources, index)

        self.assertTrue(result['passed'])
        self.assertEqual(result['findings'], [])
        self.assertEqual(len(result['references']), 1)
        self.assertEqual(result['references'][0]['lodCount'], 0)
        self.assertFalse(result['references'][0]['providerInPack'])

    def test_active_lod_count_one_unit_fails_closed_when_external_provider_missing(self) -> None:
        gg_id = 0xC6A69260CC389BCD
        unit_id = 0x3C33CF10A26CBB3E
        index = {f'{gg_id:016x}.{GEOMETRY_GROUP:016x}': [{'archive': '0123456789abcdef'}]}
        resources = {(unit_id, P.UNIT): _make_unit_with_external_gg(gg_id, lod_count=1)}

        result = geometry_provider_scan(resources, index)

        self.assertFalse(result['passed'])
        self.assertEqual(len(result['findings']), 1)
        self.assertNotIn('lodCount', result['references'][0])

    def test_lod_count_zero_unit_with_non_offset16_reference_still_fails_closed(self) -> None:
        gg_id = 0xC6A69260CC389BCD
        unit_id = 0x1C8764D62E12925A
        index = {f'{gg_id:016x}.{GEOMETRY_GROUP:016x}': [{'archive': '0123456789abcdef'}]}
        resources = {(unit_id, P.UNIT): _make_unit_with_external_gg(gg_id, lod_count=0, extra_needle_offset=64)}

        result = geometry_provider_scan(resources, index)

        self.assertFalse(result['passed'])
        self.assertEqual(len(result['findings']), 1)

    def test_non_unit_reference_at_offset16_still_fails_closed(self) -> None:
        gg_id = 0xC6A69260CC389BCD
        other_type = 0xEAC0B497876ADEDF
        raw = bytearray(128)
        struct.pack_into('<Q', raw, 16, gg_id)
        index = {f'{gg_id:016x}.{GEOMETRY_GROUP:016x}': [{'archive': '0123456789abcdef'}]}
        resources = {(0x1111222233334444, other_type): P.Generated((bytes(raw), b'', b''))}

        result = geometry_provider_scan(resources, index)

        self.assertFalse(result['passed'])
        self.assertEqual(len(result['findings']), 1)


if __name__ == '__main__':
    unittest.main()
