"""Regressions for the independent lossless texture streaming proof."""
import struct
import unittest
from pathlib import Path
from uuid import uuid4

import empire_port as P
from prepare_lighter import texture_streamed


class StreamingTests(unittest.TestCase):
    def fixture(self, rgba: bool = False) -> tuple[tuple[bytes, ...], tuple[bytes, ...]]:
        cpu = bytearray(340)
        cpu[192:196] = b'DDS '
        cpu[276:280] = b'DX10'
        struct.pack_into('<I', cpu, 320, 28 if rgba else 71)
        struct.pack_into('<II', cpu, 204, 1024, 1024)
        struct.pack_into('<I', cpu, 220, 2)
        struct.pack_into('<I', cpu, 8, 0xffffffff)
        sizes = [1024 * 1024 * 4, 512 * 512 * 4] if rgba else [524288, 131072]
        struct.pack_into('<I', cpu, 200, 0x8 if rgba else 0x80000)
        struct.pack_into('<I', cpu, 212, 4096 if rgba else sizes[0])
        new = bytearray(cpu)
        struct.pack_into('<III', new, 4, 1, 1, 0)
        struct.pack_into('<IHHI', new, 16, sum(sizes), 1024, 1024, sizes[0])
        struct.pack_into('<IHHI', new, 28, sizes[1], 512, 512, 0)
        if not rgba:
            struct.pack_into('<I', new, 200, 0)
            struct.pack_into('<I', new, 212, 0)
        chain = b'a' * sizes[0] + b'b' * sizes[1]
        return (bytes(cpu), b'', chain), (bytes(new), chain, chain[sizes[0]:])

    def test_bc_chain_and_dds_normalization(self) -> None:
        texture_streamed(*self.fixture())

    def test_rgba_streaming_preserves_pitch(self) -> None:
        texture_streamed(*self.fixture(rgba=True))

    def test_same_size_chain_tampering_rejected(self) -> None:
        before, after = self.fixture()
        with self.assertRaises(ValueError):
            texture_streamed(before, (after[0], b'c' + after[1][1:], after[2]))

    def test_resident_tail_tampering_rejected(self) -> None:
        before, after = self.fixture()
        with self.assertRaises(ValueError):
            texture_streamed(before, (after[0], after[1], b'c' + after[2][1:]))

    def test_streaming_table_tampering_rejected(self) -> None:
        before, after = self.fixture()
        cpu = bytearray(after[0])
        cpu[24] ^= 1
        with self.assertRaises(ValueError):
            texture_streamed(before, (bytes(cpu), *after[1:]))

    def test_format_change_rejected(self) -> None:
        before, after = self.fixture()
        cpu = bytearray(after[0])
        struct.pack_into('<I', cpu, 320, 98)
        with self.assertRaises(ValueError):
            texture_streamed(before, (bytes(cpu), *after[1:]))

    def test_repaired_archive_aligns_streams(self) -> None:
        folder = Path(__file__).parent / f'archive-fixture-{uuid4().hex}'
        folder.mkdir()
        path = folder / '9ba626afa44a3aa3.patch_1000'
        try:
            header = bytearray(72)
            struct.pack_into('<I', header, 0, 0xf0000011)
            resources = {(i, 0xcd4238c6a0c69e32): P.Generated((b'cpu', b'chain', b'tail')) for i in (1, 2)}
            P.write_archive(path, bytes(header), resources, stream_alignment=64)
            for row in P.directory(path):
                self.assertEqual(row[3] % 64, 0)
                parts = P.Resource(path, row, tuple(path.with_name(path.name + s) for s in P.SUFFIXES)).parts()
                self.assertEqual(parts, resources[row[:2]].parts())
        finally:
            for suffix in P.SUFFIXES:
                path.with_name(path.name + suffix).unlink(missing_ok=True)
            folder.rmdir()


if __name__ == '__main__':
    unittest.main()
