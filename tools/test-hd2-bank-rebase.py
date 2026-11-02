#!/usr/bin/env python3
"""Regress missing firing events, authored samples and codec migrations."""

import importlib
import struct
import unittest

A = importlib.import_module('rebase-player-audio')
B = importlib.import_module('hd2-bank-rebase')


def wem(pcm: bool = False) -> bytes:
    fmt = struct.pack('<HHIIHH', 0xfffe if pcm else 0xffff, 1, 48000, 96000, 2, 16)
    if pcm:
        fmt += struct.pack('<H', 6)+bytes(6)
    body = b'WAVE'+struct.pack('<4sI', b'fmt ', len(fmt))+fmt+b'data'+struct.pack('<I', 4)+b'abcd'
    return b'RIFF'+struct.pack('<I', len(body))+body


def bank(sample: bytes | None, event: int, plugin: int = 0x40001, mode: int = 0) -> bytes:
    sound = struct.pack('<IBIIIB', plugin, mode, 123, 123, len(sample or b''), 0)
    nodes = [(2, 42, sound), (4, event, bytes(4))]
    hirc = struct.pack('<I', len(nodes))+b''.join(struct.pack('<BII', kind, len(raw)+4, identity)+raw for kind, identity, raw in nodes)
    chunks = {b'BKHD': struct.pack('<I', 154 ^ 0x9211bcac), b'HIRC': hirc}
    if sample:
        chunks[b'DIDX'] = struct.pack('<III', 123, 0, len(sample))
        chunks[b'DATA'] = sample
    body = b''.join(struct.pack('<4sI', tag, len(raw))+raw for tag, raw in chunks.items())
    head = bytearray(16)
    struct.pack_into('<I', head, 4, len(body))
    return bytes(head)+body


class BankRebaseTests(unittest.TestCase):
    def test_new_firing_event_and_native_routing_survive_authored_media(self) -> None:
        authored = wem()+b'author'
        old, native = bank(authored, 10), bank(wem(), 20)
        result, migrated, proof = B.rebase_bank(A, old, native)
        self.assertEqual(A.chunks(result)[b'HIRC'], A.chunks(native)[b'HIRC'])
        self.assertEqual(A.media(A.chunks(result))[123], authored)
        self.assertFalse(migrated)
        self.assertTrue(proof['currentHierarchyPreservedExceptDeclaredSourceCodec'])

    def test_pcm_sample_keeps_compatible_plugin_on_vorbis_native_bank(self) -> None:
        old, native = bank(wem(True), 10, 0x10001), bank(wem(), 20)
        result, _, proof = B.rebase_bank(A, old, native)
        source = A.sounds(A.chunks(result)[b'HIRC'])[123][1]
        self.assertEqual(struct.unpack_from('<I', source)[0], 0x10001)
        self.assertEqual(A.media(A.chunks(result))[123], wem(True))
        self.assertEqual(len(proof['sourceCodecEdits']), 1)

    def test_embedded_recording_is_migrated_instead_of_lost(self) -> None:
        old, native = bank(wem(), 10), bank(None, 20, mode=2)
        result, migrated, _ = B.rebase_bank(A, old, native)
        self.assertEqual(migrated, {123: wem()})
        self.assertEqual(A.chunks(result)[b'HIRC'], A.chunks(native)[b'HIRC'])

    def test_unreviewed_stream_to_embedded_migration_refuses_output(self) -> None:
        with self.assertRaisesRegex(ValueError, 'Unreviewed source migration'):
            B.rebase_bank(A, bank(None, 10, mode=2), bank(wem(), 20))


if __name__ == '__main__':
    unittest.main()
