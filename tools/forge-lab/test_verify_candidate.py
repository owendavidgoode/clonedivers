"""Regression checks for verify_candidate passive LODData count=0 and @48 LODData validation."""
import inspect
from pathlib import Path
import struct
import sys
import unittest

EMPIRE_FORGE = Path(__file__).resolve().parents[1] / 'empire-forge'
if str(EMPIRE_FORGE) not in sys.path:
    sys.path.insert(0, str(EMPIRE_FORGE))

import empire_port as P  # noqa: E402
from verify_candidate import GEOMETRY_GROUP, geometry_provider_scan, lod_descriptors, verify  # noqa: E402


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

    def test_lod_count_zero_unit_with_overlapping_offset20_needle_fails_closed(self) -> None:
        gg_id = 0x0101010101010101
        unit_id = 0x1C8764D62E12925A
        index = {f'{gg_id:016x}.{GEOMETRY_GROUP:016x}': [{'archive': '0123456789abcdef'}]}
        resources = {(unit_id, P.UNIT): _make_unit_with_external_gg(gg_id, lod_count=0, extra_needle_offset=20)}

        result = geometry_provider_scan(resources, index)

        self.assertFalse(result['passed'])
        self.assertEqual(len(result['findings']), 1)

    def test_lod_count_zero_unit_still_validates_in_pack_geometry_group_owner(self) -> None:
        gg_id = 0xC6A69260CC389BCD
        unit_id = 0x1C8764D62E12925A
        index = {f'{gg_id:016x}.{GEOMETRY_GROUP:016x}': [{'archive': '0123456789abcdef'}]}
        # GeometryGroup present in pack with count=1 but wrong owner (0xDEAD != unit_id)
        gg_raw = bytearray(64)
        struct.pack_into('<I', gg_raw, 8, 1)
        struct.pack_into('<Q', gg_raw, 24, 0xDEADBEEFCAFEBABE)
        resources = {
            (unit_id, P.UNIT): _make_unit_with_external_gg(gg_id, lod_count=0),
            (gg_id, GEOMETRY_GROUP): P.Generated((bytes(gg_raw), b'', b'')),
        }

        result = geometry_provider_scan(resources, index)

        self.assertFalse(result['passed'])
        self.assertEqual(len(result['findings']), 1)
        self.assertIn('GeometryGroup does not uniquely provide owner UNIT', result['findings'][0]['reason'])

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


def _make_unit_with_meshes_and_lod(mesh_count: int, lod_records: list[bytes] | None) -> bytes:
    raw = bytearray(160)
    if mesh_count > 0:
        mesh_table_at = len(raw)
        struct.pack_into('<I', raw, 100, mesh_table_at)
        header_size = 4 + 8 * mesh_count
        offsets = [header_size + 128 * i for i in range(mesh_count)]
        names = [0x1000 + i for i in range(mesh_count)]
        raw.extend(struct.pack(f'<I{mesh_count}I{mesh_count}I', mesh_count, *offsets, *names))
        raw.extend(bytes(128 * mesh_count))
    if lod_records is not None:
        lod_table_at = len(raw)
        struct.pack_into('<I', raw, 48, lod_table_at)
        if not lod_records:
            raw.extend(struct.pack('<4I', 0, 0, 0, 0))
        else:
            count = len(lod_records)
            cur = 4 + 4 * count
            offsets = []
            body = bytearray()
            for rec in lod_records:
                offsets.append(cur)
                body.extend(rec)
                cur += len(rec)
            raw.extend(struct.pack(f'<I{count}I', count, *offsets))
            raw.extend(body)
    return bytes(raw)


def _make_valid_lod_record(bone_hash: int, steps: list[list[int]]) -> bytes:
    levels = len(steps)
    cur = 20 + 4 * levels
    step_offsets = []
    body = bytearray()
    for indices in steps:
        step_offsets.append(cur)
        # Step header: 2 float thresholds (8 bytes) + count (u32) + mesh indices
        body.extend(struct.pack('<2fI', 1.0, 0.0, len(indices)))
        if indices:
            body.extend(struct.pack(f'<{len(indices)}I', *indices))
        cur = 20 + 4 * levels + len(body)
    return struct.pack(f'<5I{levels}I', 1, bone_hash, 0, 1, levels, *step_offsets) + bytes(body)


class LodDescriptorsValidationTests(unittest.TestCase):
    def test_clobbered_6f_lod_record_fails_closed(self) -> None:
        rec = bytearray(_make_valid_lod_record(0xDEADBEEF, [[1, 2, 3, 4]]))
        # Simulate `<6f` bounding-box overwrite into bytes 0..24 of LODData record
        struct.pack_into('<6f', rec, 0, -0.45, -0.25, 0.0, 0.45, 1.82, 0.35)
        raw = _make_unit_with_meshes_and_lod(5, [bytes(rec)])

        with self.assertRaisesRegex(ValueError, 'Invalid LOD table'):
            lod_descriptors(raw)

    def test_valid_non_empty_lod_data_passes_cleanly(self) -> None:
        rec = _make_valid_lod_record(0x9FB14138, [[1, 2, 3, 4], [1, 2]])
        raw = _make_unit_with_meshes_and_lod(5, [rec])

        self.assertEqual(lod_descriptors(raw), [[1, 2, 3, 4], [1, 2]])

    def test_lod_count_zero_and_null_pointer_pass_cleanly(self) -> None:
        raw_zero = _make_unit_with_meshes_and_lod(5, [])
        raw_null = _make_unit_with_meshes_and_lod(5, None)

        self.assertEqual(lod_descriptors(raw_zero), [])
        self.assertEqual(lod_descriptors(raw_null), [])

    def test_out_of_range_mesh_index_in_lod_descriptor_fails_closed(self) -> None:
        rec = _make_valid_lod_record(0x9FB14138, [[1, 5]])
        raw = _make_unit_with_meshes_and_lod(5, [rec])

        with self.assertRaisesRegex(ValueError, 'Invalid LOD descriptor'):
            lod_descriptors(raw)

    def test_verify_calls_lod_descriptors_unconditionally_in_unit_loop(self) -> None:
        src = inspect.getsource(verify)
        self.assertIn('lod_descriptors(raw)', src)


if __name__ == '__main__':
    unittest.main()
