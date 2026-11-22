#!/usr/bin/env python3
"""Pack one Lua file into a Helldivers 2 patch file for Bingus Shared Loader.

  pack.py <addon.lua> <out_dir>

The Lua's first line must be "-- HD2-Addon: mods/<author>/<name>"; the loader
finds addons by that line and the resource is stored under that name. Writes
9ba626afa44a3aa3.patch_0 plus the two empty side files into <out_dir>.

Layout (checked by rebuilding Operation Reroller's patch byte for byte):
  header 0x48: magic f0000011, type count, file count, total size at 0x20
  type entry 0x20: 8 zero, type hash, count, align 0x10, 0x10
  file entry 0x50: name hash, type hash, data offset, 4x u64 zero,
                   data size u32, 2x u32 zero, 0x10, 0x10, index
  data (16-aligned): u32 source length, u32 2, source; padded to 16
"""
import os
import re
import struct
import sys

ARCHIVE = "9ba626afa44a3aa3"
M64 = (1 << 64) - 1


def murmur64a(data, seed=0):
    m, r = 0xC6A4A7935BD1E995, 47
    h = (seed ^ (len(data) * m)) & M64
    n = len(data) // 8
    for i in range(n):
        k = struct.unpack_from("<Q", data, i * 8)[0]
        k = (k * m) & M64
        k ^= k >> r
        k = (k * m) & M64
        h ^= k
        h = (h * m) & M64
    tail = data[n * 8:]
    if tail:
        for i in range(len(tail) - 1, -1, -1):
            h ^= tail[i] << (8 * i)
        h = (h * m) & M64
    h ^= h >> r
    h = (h * m) & M64
    h ^= h >> r
    return h


LUA_TYPE = murmur64a(b"lua")


def build(source):
    first = source.split(b"\n", 1)[0].decode("ascii", "replace").rstrip("\r")
    m = re.match(r"^-- HD2-Addon: (mods/[A-Za-z0-9_]+/[A-Za-z0-9_/]+)$", first)
    if not m:
        sys.exit("first line must be '-- HD2-Addon: mods/<author>/<name>'")
    name = m.group(1)
    payload = struct.pack("<II", len(source), 2) + source
    data_off = 0x48 + 0x20 + 0x50
    data_off += -data_off % 16
    total = data_off + len(payload)
    total += -total % 16
    out = bytearray(total)
    struct.pack_into("<III", out, 0, 0xF0000011, 1, 1)
    struct.pack_into("<Q", out, 0x20, total)
    struct.pack_into("<QQQII", out, 0x48, 0, LUA_TYPE, 1, 0x10, 0x10)
    struct.pack_into("<QQQ", out, 0x68, murmur64a(name.encode()), LUA_TYPE, data_off)
    struct.pack_into("<IIIIII", out, 0x68 + 56, len(payload), 0, 0, 0x10, 0x10, 0)
    out[data_off:data_off + len(payload)] = payload
    return name, bytes(out)


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    with open(sys.argv[1], "rb") as fh:
        source = fh.read()
    name, blob = build(source)
    os.makedirs(sys.argv[2], exist_ok=True)
    base = os.path.join(sys.argv[2], ARCHIVE + ".patch_0")
    with open(base, "wb") as fh:
        fh.write(blob)
    for ext in (".gpu_resources", ".stream"):
        open(base + ext, "wb").close()
    print("packed %s (%d bytes of Lua) -> %s" % (name, len(source), base))


if __name__ == "__main__":
    main()
