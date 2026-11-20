"""Regression checks for verify_candidate passive LODData count=0 and @48 LODData validation."""
import ast
import inspect
import json
from pathlib import Path
import struct
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

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

    def test_null_offset48_pointer_unit_fails_closed_when_external_provider_missing(self) -> None:
        gg_id = 0xC6A69260CC389BCD
        unit_id = 0x1C8764D62E12925A
        raw = bytearray(160)
        struct.pack_into('<Q', raw, 16, gg_id)
        struct.pack_into('<I', raw, 48, 0)
        index = {f'{gg_id:016x}.{GEOMETRY_GROUP:016x}': [{'archive': '0123456789abcdef'}]}
        resources = {(unit_id, P.UNIT): P.Generated((bytes(raw), b'', b''))}

        result = geometry_provider_scan(resources, index)

        self.assertFalse(result['passed'])
        self.assertEqual(len(result['findings']), 1)
        self.assertNotIn('lodCount', result['references'][0])


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

    def test_header_overlapping_step_offset_fails_closed(self) -> None:
        for bad_rel in (0, 23):
            rec = bytearray(_make_valid_lod_record(0x9FB14138, [[1, 2]]))
            # Header + 1 step_offset occupies bytes 0..24; rel < 24 overlaps header/offset table
            struct.pack_into('<I', rec, 20, bad_rel)
            raw = _make_unit_with_meshes_and_lod(5, [bytes(rec)])

            with self.assertRaisesRegex(ValueError, 'Invalid LOD entry'):
                lod_descriptors(raw)

    def test_repeated_step_offsets_exceeding_index_budget_fails_closed(self) -> None:
        levels = 25
        step_count = 5000  # 25 * 5000 = 125,000 > 100,000 budget
        step_rel = 20 + 4 * levels
        step_blob = struct.pack('<2fI', 1.0, 0.0, step_count) + struct.pack(f'<{step_count}I', *([0] * step_count))
        rec = struct.pack(f'<5I{levels}I', 1, 0x9FB14138, 0, 1, levels, *([step_rel] * levels)) + step_blob
        raw = _make_unit_with_meshes_and_lod(5, [rec])

        with self.assertRaisesRegex(ValueError, 'Invalid LOD descriptor'):
            lod_descriptors(raw)

    def test_verify_lod_descriptors_precedes_material_map_continue_and_fails_materialless_unit(self) -> None:
        # 1. AST structural check: lod_descriptors(raw) precedes @112 `if not at: continue` in P.UNIT loop
        tree = ast.parse(inspect.getsource(verify))
        unit_loop = None
        for node in ast.walk(tree):
            if isinstance(node, ast.For) and isinstance(node.iter, ast.Name) and node.iter.id == 'resources':
                if any(isinstance(s, ast.Expr) and isinstance(s.value, ast.Call)
                       and isinstance(s.value.func, ast.Name) and s.value.func.id == 'lod_descriptors'
                       for s in node.body):
                    unit_loop = node
                    break
        self.assertIsNotNone(unit_loop)
        lod_idx = next(i for i, s in enumerate(unit_loop.body)
                       if isinstance(s, ast.Expr) and isinstance(s.value, ast.Call)
                       and isinstance(s.value.func, ast.Name) and s.value.func.id == 'lod_descriptors')
        mat_continue_idx = next(i for i, s in enumerate(unit_loop.body)
                                if i > 0 and isinstance(s, ast.If) and any(isinstance(b, ast.Continue) for b in s.body))
        self.assertLess(lod_idx, mat_continue_idx)

        # 2. Behavioral end-to-end check through verify(candidate) on a materialless (@112 == 0) UNIT
        def run_verify_with_unit(unit_raw: bytes) -> int:
            with tempfile.TemporaryDirectory() as td:
                tdir = Path(td)
                root_dir = tdir / 'root'
                old_dir = tdir / 'old'
                base_dir = tdir / 'base'
                game_parent = tdir / 'forge'
                game_dir = game_parent / 'data'
                cand_dir = tdir / 'cand'
                for d in (root_dir, old_dir / 'dist/hd2-update-2026-10-29/native-index-v1',
                          base_dir, game_dir / 'game', game_parent / 'bin', cand_dir):
                    d.mkdir(parents=True, exist_ok=True)
                (root_dir / 'empire_port.py').write_bytes(b'# stub\n')
                (game_dir / 'bundles.nxa').write_bytes(b'nxa')
                (game_dir / 'game/game.dll').write_bytes(b'dll')
                (game_parent / 'bin/helldivers2.exe').write_bytes(b'exe')
                (root_dir / 'native-loose-archive-pins.json').write_text('[]\n', encoding='utf-8')
                (root_dir / 'native-index-25480438.json').write_text('{}\n', encoding='utf-8')
                (old_dir / 'dist/hd2-update-2026-10-29/native-index-v1/native-index.json').write_text('{}\n', encoding='utf-8')

                unit_id = 0xABCD1234567890EF
                label = f'{unit_id:016x}.{P.UNIT:016x}'
                header72 = struct.pack('<3I60s', 0xF0000011, 0, 0, b'\x00' * 60)
                P.write_archive(cand_dir / '9ba626afa44a3aa3.patch_0', header72,
                                {(unit_id, P.UNIT): P.Generated((unit_raw, b'', b''))})
                (root_dir / 'expected-resources.json').write_text(
                    json.dumps({label: [P.sha(unit_raw), P.sha(b''), P.sha(b'')]}) + '\n', encoding='utf-8')

                fake_preflight = types.SimpleNamespace(inspect_path=lambda p, sz: {'passed': True})
                fake_native_cls = type('NativeSlim', (), {'__init__': lambda s, g: None, 'chunk': lambda s, *a: b''})
                fake_native = types.SimpleNamespace(NativeSlim=fake_native_cls)
                fake_mod = types.SimpleNamespace()

                def fake_load(name: str, rel: str) -> object:
                    if name == 'verify_preflight':
                        return fake_preflight
                    if name == 'verify_native':
                        return fake_native
                    return fake_mod

                old_paths = (P.ROOT, P.OLD, P.BASE, P.GAME)
                try:
                    P.ROOT, P.OLD, P.BASE, P.GAME = root_dir, old_dir, base_dir, game_dir
                    with patch.object(P, 'load', side_effect=fake_load), patch.object(P, 'baseline', return_value={}):
                        return verify(cand_dir)
                finally:
                    P.ROOT, P.OLD, P.BASE, P.GAME = old_paths

        # Materialless (@112 == 0) UNIT with clobbered @48 LODData must fail closed inside verify()
        bad_rec = bytearray(_make_valid_lod_record(0xDEADBEEF, [[0]]))
        struct.pack_into('<6f', bad_rec, 0, -0.45, -0.25, 0.0, 0.45, 1.82, 0.35)
        bad_unit = _make_unit_with_meshes_and_lod(2, [bytes(bad_rec)])
        self.assertEqual(struct.unpack_from('<I', bad_unit, 112)[0], 0)
        with self.assertRaisesRegex(ValueError, 'Invalid LOD table'):
            run_verify_with_unit(bad_unit)

        # Materialless (@112 == 0) UNIT with valid @48 LODData passes verify() cleanly
        good_rec = _make_valid_lod_record(0xDEADBEEF, [[0]])
        good_unit = _make_unit_with_meshes_and_lod(2, [good_rec])
        self.assertEqual(struct.unpack_from('<I', good_unit, 112)[0], 0)
        self.assertEqual(run_verify_with_unit(good_unit), 0)


if __name__ == '__main__':
    unittest.main()
