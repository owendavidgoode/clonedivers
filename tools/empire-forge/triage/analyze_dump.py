#!/usr/bin/env python3
"""Inspect x64 minidump context and disassemble the matching offline game image."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys

from capstone import Cs, CS_ARCH_X86, CS_MODE_64


class Dump:
    def __init__(self, path: Path) -> None:
        self.path = path
        self.raw = path.read_bytes()
        if self.raw[:4] != b'MDMP':
            raise ValueError('Expected a minidump')
        count, directory = struct.unpack_from('<II', self.raw, 8)
        self.streams = {}
        for i in range(count):
            kind, size, at = struct.unpack_from('<III', self.raw, directory + 12 * i)
            self.streams[kind] = (at, size)
        self.memory = []
        if 5 in self.streams:
            at, _ = self.streams[5]
            count = struct.unpack_from('<I', self.raw, at)[0]
            for i in range(count):
                address, size, rva = struct.unpack_from('<QII', self.raw, at + 4 + 16 * i)
                self.memory.append((address, size, rva))
        if 9 in self.streams:
            at, _ = self.streams[9]
            count, rva = struct.unpack_from('<QQ', self.raw, at)
            for i in range(count):
                address, size = struct.unpack_from('<QQ', self.raw, at + 16 + 16 * i)
                self.memory.append((address, size, rva))
                rva += size
        self.modules = []
        at, _ = self.streams[4]
        count = struct.unpack_from('<I', self.raw, at)[0]
        for i in range(count):
            base, size, checksum, stamp, name_at = struct.unpack_from('<QIIII', self.raw, at + 4 + 108 * i)
            length = struct.unpack_from('<I', self.raw, name_at)[0]
            name = self.raw[name_at + 4:name_at + 4 + length].decode('utf-16-le')
            self.modules.append({'name': name, 'base': base, 'size': size, 'timestamp': stamp, 'checksum': checksum})
        at, _ = self.streams[6]
        self.thread = struct.unpack_from('<I', self.raw, at)[0]
        self.code, self.flags, _, self.address, count = struct.unpack_from('<IIQQI', self.raw, at + 8)
        self.parameters = struct.unpack_from(f'<{count}Q', self.raw, at + 40)
        size, context = struct.unpack_from('<II', self.raw, at + 160)
        names = ['rax', 'rcx', 'rdx', 'rbx', 'rsp', 'rbp', 'rsi', 'rdi'] + [f'r{i}' for i in range(8, 16)] + ['rip']
        self.registers = dict(zip(names, struct.unpack_from('<17Q', self.raw, context + 120), strict=True))

    def read(self, address: int, size: int) -> bytes | None:
        for base, length, rva in self.memory:
            if base <= address and address + size <= base + length:
                return self.raw[rva + address - base:rva + address - base + size]
        return None


class Image:
    def __init__(self, path: Path) -> None:
        self.raw = path.read_bytes()
        pe = struct.unpack_from('<I', self.raw, 0x3c)[0]
        count = struct.unpack_from('<H', self.raw, pe + 6)[0]
        optional = pe + 24
        self.base = struct.unpack_from('<Q', self.raw, optional + 24)[0]
        self.sections = []
        at = optional + struct.unpack_from('<H', self.raw, pe + 20)[0]
        for i in range(count):
            row = at + 40 * i
            virtual_size, rva, size, file_at = struct.unpack_from('<IIII', self.raw, row + 8)
            self.sections.append((rva, max(virtual_size, size), file_at))

    def read(self, rva: int, size: int) -> bytes:
        for base, length, file_at in self.sections:
            if base <= rva and rva + size <= base + length:
                return self.raw[file_at + rva - base:file_at + rva - base + size]
        raise ValueError(f'Unmapped image RVA {rva:x}')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('dump', type=Path)
    parser.add_argument('--image', type=Path, default=Path('D:/HellForge/game/bin/helldivers2.exe'))
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--regions', nargs='*', default=['2bd870:160', '1c8df0:140', '9d870:150', '8a1280:140', '669dc0:90', '5c2de0:140'])
    args = parser.parse_args()
    dump = Dump(args.dump)
    image = Image(args.image)
    module = next(m for m in dump.modules if m['name'].lower().endswith('helldivers2.exe'))
    base = module['base']
    report = {'dump': str(args.dump), 'dumpSha256': hashlib.sha256(dump.raw).hexdigest(),
              'image': str(args.image), 'imageSha256': hashlib.sha256(image.raw).hexdigest(),
              'streams': dump.streams, 'thread': dump.thread, 'exceptionCode': hex(dump.code),
              'exceptionAddress': hex(dump.address), 'exceptionRva': hex(dump.address - base),
              'exceptionParameters': [hex(v) for v in dump.parameters],
              'registers': {k: hex(v) for k, v in dump.registers.items()}, 'module': module,
              'memoryRanges': [{'base': hex(b), 'size': n, 'fileOffset': r} for b, n, r in dump.memory]}
    windows = {}
    for name, address in dump.registers.items():
        raw = dump.read(address, 256)
        windows[name] = {'address': hex(address), 'captured': raw is not None, 'hex': raw.hex() if raw else None}
    report['registerMemory'] = windows
    stack = dump.read(dump.registers['rsp'], 1024)
    report['stackQwords'] = []
    if stack:
        for offset in range(0, len(stack), 8):
            value = struct.unpack_from('<Q', stack, offset)[0]
            entry = {'offset': hex(offset), 'value': hex(value)}
            if base <= value < base + module['size']:
                entry['imageRva'] = hex(value - base)
            report['stackQwords'].append(entry)
    decoder = Cs(CS_ARCH_X86, CS_MODE_64)
    lines = []
    for region in args.regions:
        rva, length = (int(v, 16) for v in region.split(':'))
        lines.append(f'\n## RVA +0x{rva:x}, length 0x{length:x}')
        captured = dump.read(base + rva, length)
        code = captured
        lines.append(f'Dump code captured {captured is not None}')
        if code is None:
            lines.append('Packed disk image is unsuitable for disassembly. Region unavailable.')
            continue
        for ins in decoder.disasm(code, rva):
            mark = 'FAULT' if ins.address == dump.address - base else ''
            lines.append(f'{ins.address:08x}  {ins.bytes.hex():24s} {ins.mnemonic:8s} {ins.op_str} {mark}'.rstrip())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.with_suffix('.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    args.output.with_suffix('.asm.txt').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print(json.dumps({k: report[k] for k in ('thread', 'exceptionCode', 'exceptionRva', 'exceptionParameters', 'registers')}))
    return 0


if __name__ == '__main__':
    sys.exit(main())
