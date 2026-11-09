"""Focused regressions for the Forge archive writer and UNIT control guard."""
import struct
import unittest
from pathlib import Path

import empire_port as P
from inspect_inputs import native_families
from verify_candidate import pointers, ship_helper_evidence


class ArchiveTests(unittest.TestCase):
    def test_native_index_includes_loose_families(self) -> None:
        folder = P.ROOT / 'archive-fixture' / 'native-index'
        folder.mkdir(parents=True, exist_ok=True)
        (folder / '0000000000000002').write_bytes(b'')
        (folder / '0000000000000003.patch_0').write_bytes(b'')
        (folder / '0000000000000004.stream').write_bytes(b'')
        self.assertEqual(native_families({'0000000000000001': {}, 'bundles.nxa': {}}, folder),
                         ['0000000000000001', '0000000000000002'])

    def test_writer_aligns_head_and_wwise_slots(self) -> None:
        preflight = P.load('test_preflight', 'slim-archive-preflight.py')
        header = bytearray(72)
        struct.pack_into('<I', header, 0, 0xf0000011)
        entries = {(12, P.STREAM): P.Generated((bytes(12), b'RIFFtest', b'')),
                   (8, P.UNIT): P.Generated((bytes(128), b'', b'gpu')),
                   (1, P.STREAM): P.Generated((bytes(12), b'RIFFnext', b''))}
        folder = P.ROOT / 'archive-fixture'
        folder.mkdir(exist_ok=True)
        path = folder / '9ba626afa44a3aa3.patch_0'
        P.write_archive(path, bytes(header), entries)
        sizes = tuple(path.with_name(path.name + s).stat().st_size for s in P.SUFFIXES[1:])
        self.assertTrue(preflight.inspect_path(path, sizes)['passed'])
        rows = P.directory(path)
        self.assertEqual([r[1] for r in rows], sorted(k[1] for k in entries))
        for row in rows:
            paths = tuple(path.with_name(path.name + s) for s in P.SUFFIXES)
            self.assertEqual(P.Resource(path, row, paths).parts(), entries[row[:2]].parts())

    def test_control_guard_allows_visual_pointer_changes(self) -> None:
        native = bytes(128)
        source = bytearray(native)
        struct.pack_into('<I', source, 48, 120)
        self.assertTrue(P.unit_safe((bytes(source), b'', b''), native))

    def test_control_guard_rejects_external_control_changes(self) -> None:
        native = bytes(128)
        source = bytearray(native)
        struct.pack_into('<I', source, 32, 120)
        self.assertFalse(P.unit_safe((bytes(source), b'', b''), native))

    def test_control_guard_rejects_truncation(self) -> None:
        self.assertFalse(P.unit_safe((bytes(127), b'', b''), bytes(128)))

    def test_wem_format_parser(self) -> None:
        data = b'RIFF' + bytes(4) + b'WAVEfmt ' + struct.pack('<IH', 2, 0xffff)
        self.assertEqual(P.fmt(data), 0xffff)
        self.assertIsNone(P.fmt(b'bad'))


class ShipHelperTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        folder = P.ROOT / 'archive-fixture' / 'ship-helper'
        cls.candidate = ((folder / 'candidate.main').read_bytes(), b'', (folder / 'candidate.gpu').read_bytes())
        cls.native = ((folder / 'native.main').read_bytes(), b'', (folder / 'native.gpu').read_bytes())

    def test_native_equivalent_renamed_slot_passes(self) -> None:
        proof = ship_helper_evidence(self.candidate, self.native, 0x093fc934)
        self.assertIsNotNone(proof)
        self.assertEqual(proof['candidateSection']['geometrySha256'], proof['nativeSection']['geometrySha256'])
        self.assertNotEqual(self.candidate[2], self.native[2])  # Different native/export buffer order and formats.

    def test_lod_activation_stays_a_finding(self) -> None:
        for which in ('candidate', 'native'):
            parts = getattr(self, which)
            raw = bytearray(parts[0])
            group = pointers(raw, 48)[0]
            entry = group + struct.unpack_from('<I', raw, group + 20)[0]
            struct.pack_into('<I', raw, entry + 12, 0)
            changed = bytes(raw), parts[1], parts[2]
            a, b = (changed, self.native) if which == 'candidate' else (self.candidate, changed)
            self.assertIsNone(ship_helper_evidence(a, b, 0x093fc934))

    def test_changed_geometry_stays_a_finding(self) -> None:
        gpu = bytearray(self.candidate[2])
        struct.pack_into('<f', gpu, 4, 7.0)
        self.assertIsNone(ship_helper_evidence((self.candidate[0], b'', bytes(gpu)), self.native, 0x093fc934))

    def test_changed_section_role_stays_a_finding(self) -> None:
        raw = bytearray(self.candidate[0])
        struct.pack_into('<I', raw, pointers(raw, 100)[0] + 36, 9)
        self.assertIsNone(ship_helper_evidence((bytes(raw), b'', self.candidate[2]), self.native, 0x093fc934))

    def test_rendered_section_using_same_slot_stays_a_finding(self) -> None:
        raw = bytearray(self.candidate[0])
        mesh = pointers(raw, 100)[1]  # A second type-5 section; type alone must not grant an exemption.
        slot = mesh + struct.unpack_from('<I', raw, mesh + 108)[0]
        struct.pack_into('<I', raw, slot, 0x093fc934)
        self.assertIsNone(ship_helper_evidence((bytes(raw), b'', self.candidate[2]), self.native, 0x093fc934))

    def test_native_material_mapping_stays_a_finding(self) -> None:
        raw = bytearray(self.native[0])
        at = struct.unpack_from('<I', raw, 112)[0]
        struct.pack_into('<I', raw, at + 4, 0xcd121f4b)
        self.assertIsNone(ship_helper_evidence(self.candidate, (bytes(raw), b'', self.native[2]), 0x093fc934))

    def test_other_unresolved_slot_stays_a_finding(self) -> None:
        self.assertIsNone(ship_helper_evidence(self.candidate, self.native, 0x12345678))


if __name__ == '__main__':
    unittest.main()
