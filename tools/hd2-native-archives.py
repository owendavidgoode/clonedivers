#!/usr/bin/env python3
"""Read bounded native SLIM archive directories and resource payloads.

Implements the DSAR/DSAA structures in Filediver's primary slim_edition.go.
Only requested chunks are read; no native file or extracted cache is written.
"""

import bisect
import struct
from pathlib import Path
from typing import Any

CHUNK = struct.Struct('<QQIIBB6x')
ROW = struct.Struct('<QQQQQQQIIIIII')


def lz4_block(data: bytes, expected: int) -> bytes:
    """Decode a raw LZ4 block, checking every literal/match boundary."""
    result = bytearray()
    position = 0

    def length(initial: int) -> int:
        nonlocal position
        value = initial
        if initial == 15:
            while True:
                extra = data[position]
                position += 1
                value += extra
                if extra != 255:
                    break
        return value

    while position < len(data):
        token = data[position]
        position += 1
        literals = length(token >> 4)
        if position + literals > len(data) or len(result) + literals > expected:
            raise ValueError('LZ4 literal overflow')
        result.extend(data[position:position + literals])
        position += literals
        if position == len(data):
            break
        offset = struct.unpack_from('<H', data, position)[0]
        position += 2
        size = length(token & 15) + 4
        if not offset or offset > len(result) or len(result) + size > expected:
            raise ValueError('LZ4 match overflow')
        for _ in range(size):
            result.append(result[-offset])
    if len(result) != expected:
        raise ValueError('LZ4 output size mismatch')
    return bytes(result)


class NativeSlim:
    """Resolve logical archives through bundles.nxa using bounded reads."""

    def __init__(self, data: Path) -> None:
        self.data = data
        self.structures: dict[Path, list[tuple[int, ...]]] = {}
        path = data / 'bundles.nxa'
        decoded = b''.join(self.chunk(path, i) for i in range(len(self.structure(path))))
        magic, _, _, filenames, archives = struct.unpack_from('<4s4I', decoded)
        if magic != b'DSAA':
            raise ValueError('Invalid bundles.nxa DSAA header')

        def string(offset: int) -> str:
            return decoded[offset:decoded.index(0, offset)].decode('ascii')

        self.archives: dict[str, dict[str, Any]] = {}
        for i in range(archives):
            size, name, count, entries = struct.unpack_from('<QIIQ', decoded, 24 + 24 * i)
            filename = string(name)
            if filename in self.archives:
                raise ValueError('Duplicate logical archive')
            self.archives[filename] = {'size': size, 'entries': [struct.unpack_from('<I4xI3xB', decoded, entries + 16 * j) for j in range(count)]}
        offsets = struct.unpack_from(f'<{filenames}I', decoded, 24 + 24 * archives)
        self.filenames = [string(offset) for offset in offsets]

    def structure(self, path: Path) -> list[tuple[int, ...]]:
        if path not in self.structures:
            with path.open('rb') as handle:
                header = handle.read(32)
                magic, _, count, size, _, _ = struct.unpack('<4s4sIIQ8s', header)
                if magic != b'DSAR' or size != 32 + count * 32:
                    raise ValueError('Malformed native DSAR metadata')
                chunks = [CHUNK.unpack(handle.read(32)) for _ in range(count)]
            if chunks != sorted(chunks, key=lambda item: item[0]):
                raise ValueError('Unsorted DSAR chunks')
            self.structures[path] = chunks
        return self.structures[path]

    def chunk(self, path: Path, index: int) -> bytes:
        _, offset, size, compressed, codec, _ = self.structure(path)[index]
        with path.open('rb') as handle:
            handle.seek(offset)
            payload = handle.read(compressed)
        if len(payload) != compressed:
            raise ValueError('Truncated DSAR chunk')
        if codec == 0:
            if len(payload) != size:
                raise ValueError('Uncompressed DSAR size mismatch')
            return payload
        if codec == 3:
            return lz4_block(payload, size)
        raise ValueError(f'Unknown DSAR codec {codec}')

    def read(self, archive: str, offset: int, size: int) -> bytes:
        if not size:
            return b''
        direct = self.data / archive
        if direct.exists():
            chunks = self.structure(direct)
            index = bisect.bisect_right([chunk[0] for chunk in chunks], offset) - 1
            skip = offset - chunks[index][0]
            result = bytearray()
            while len(result) < size:
                payload = self.chunk(direct, index)
                result.extend(payload[skip:skip + size - len(result)])
                skip = 0
                index += 1
            return bytes(result)
        item = self.archives[archive]
        if offset + size > item['size']:
            raise ValueError('Logical archive read outside bounds')
        entries = item['entries']
        index = bisect.bisect_right([entry[0] for entry in entries], offset) - 1
        if index < 0:
            raise ValueError('No DSAA entry covers request')
        result = bytearray()
        while len(result) < size:
            start, uncompressed, bundle = entries[index]
            end = entries[index + 1][0] if index + 1 < len(entries) else item['size']
            path = self.data / self.filenames[bundle]
            chunks = self.structure(path)
            ci = bisect.bisect_left([chunk[0] for chunk in chunks], uncompressed)
            if chunks[ci][0] != uncompressed:
                raise ValueError('DSAA entry has no corresponding DSAR chunk')
            within = offset - start
            while within >= chunks[ci][2]:
                within -= chunks[ci][2]
                ci += 1
            remaining = min(size - len(result), end - offset)
            while remaining:
                payload = self.chunk(path, ci)
                take = min(remaining, len(payload) - within)
                if take <= 0:
                    raise ValueError('Invalid DSAA progress')
                result.extend(payload[within:within + take])
                remaining -= take
                offset += take
                within = 0
                ci += 1
            index += 1
        return bytes(result)

    def resource(self, archive: str, identity: int, type_: int) -> tuple[tuple[int, ...], tuple[bytes, bytes, bytes]]:
        header = self.read(archive, 0, 72)
        magic, types, count = struct.unpack_from('<III', header)
        if magic != 0xf0000011:
            raise ValueError('Malformed logical archive header')
        raw = self.read(archive, 72 + 32 * types, 80 * count)
        rows = [row for row in ROW.iter_unpack(raw) if row[:2] == (identity, type_)]
        if len(rows) != 1:
            raise ValueError('Missing or duplicate native resource')
        row = rows[0]
        payloads = tuple(self.read(archive + suffix, row[2 + i], row[7 + i]) for i, suffix in enumerate(('', '.stream', '.gpu_resources')))
        return row, payloads
