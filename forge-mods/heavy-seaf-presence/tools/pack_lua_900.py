#!/usr/bin/env python3
"""Repack heavy-seaf-presence src/menu_bridge.lua into data/9ba626afa44a3aa3.patch_900
and verify syntax + defaults under D:/HellForge/game/bin/lua51.dll."""
from __future__ import annotations

import ctypes
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
ORIG = Path('D:/ClaudeWork/workstreams-v21/code-mods/refs/heavy-seaf-1.2.0-clonedivers.4-orig/heavy/data/9ba626afa44a3aa3.patch_900')
OUT = ROOT / 'data/9ba626afa44a3aa3.patch_900'
LUA_SRC = ROOT / 'src/menu_bridge.lua'
LUA_DLL = Path('D:/HellForge/game/bin/lua51.dll')
ROW = struct.Struct('<7Q6I')
LUA_TYPE = 0xa14e8dfa2cd117e2


def resource_hash(name: str) -> int:
    data = name.encode('utf-8')
    mask, multiplier, shift = (1 << 64) - 1, 0xc6a4a7935bd1e995, 47
    value = len(data) * multiplier & mask
    end = len(data) // 8 * 8
    for offset in range(0, end, 8):
        part = int.from_bytes(data[offset:offset + 8], 'little') * multiplier & mask
        part ^= part >> shift
        part = part * multiplier & mask
        value = (value ^ part) * multiplier & mask
    if end < len(data):
        value = (value ^ int.from_bytes(data[end:], 'little')) * multiplier & mask
    value ^= value >> shift
    value = value * multiplier & mask
    return value ^ (value >> shift)


def main() -> int:
    template = ORIG.read_bytes()
    if struct.unpack_from('<3I', template) != (0xf0000011, 1, 1):
        raise ValueError('Unexpected patch_900 header')
    row = ROW.unpack_from(template, 104)
    expected_res = resource_hash('mods/kohl/heavy_seaf_presence')
    if row[0] != expected_res or row[1] != LUA_TYPE:
        raise ValueError('Unexpected patch_900 resource entry')

    source_bytes = LUA_SRC.read_bytes()
    payload = struct.pack('<2I', len(source_bytes), 2) + source_bytes
    head = bytearray(template[:192])
    values = list(row)
    values[0], values[2], values[7] = expected_res, 192, len(payload)
    ROW.pack_into(head, 104, *values)
    raw = head + payload
    raw += bytes(-len(raw) % 16)
    struct.pack_into('<Q', raw, 32, len(raw))

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_bytes(raw)
    (ROOT / 'data/9ba626afa44a3aa3.patch_900.gpu_resources').write_bytes(b'')
    (ROOT / 'data/9ba626afa44a3aa3.patch_900.stream').write_bytes(b'')

    # Verify in standalone LuaJIT DLL
    lib = ctypes.CDLL(str(LUA_DLL))
    lib.luaL_newstate.restype = ctypes.c_void_p
    lib.luaL_openlibs.argtypes = [ctypes.c_void_p]
    lib.luaL_loadstring.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
    lib.luaL_loadstring.restype = ctypes.c_int
    lib.lua_pcall.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_int]
    lib.lua_pcall.restype = ctypes.c_int
    lib.lua_tolstring.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_size_t)]
    lib.lua_tolstring.restype = ctypes.c_char_p
    lib.lua_close.argtypes = [ctypes.c_void_p]

    harness = (
        "local specs={}\n"
        "_G.ModOptionsMenu={\n"
        "  register_option=function(id,s) specs[id]=s; return true end,\n"
        "  get=function(id) return specs[id] and specs[id].default end,\n"
        "  set=function() end,\n"
        "  on_change=function() end,\n"
        "}\n"
        + source_bytes.decode('utf-8')
        + "\nupdate()\n"
        "assert(specs['heavy_seaf.constant_drops'].default == false, 'constant_drops must default to false')\n"
        "assert(specs['heavy_seaf.interval'].default == 120, 'interval must default to 120')\n"
        "assert(specs['heavy_seaf.squads'].default == 1, 'squads must default to 1')\n"
        "return 'OK'\n"
    )
    state = lib.luaL_newstate()
    try:
        lib.luaL_openlibs(state)
        rc = lib.luaL_loadstring(state, harness.encode('utf-8'))
        if rc == 0:
            rc = lib.lua_pcall(state, 0, 1, 0)
        if rc != 0:
            err = lib.lua_tolstring(state, -1, None)
            raise RuntimeError(err.decode('utf-8', errors='replace') if err else 'Lua check failed')
    finally:
        lib.lua_close(state)

    print(f'Repacked and LuaJIT-verified patch_900 ({len(raw)} bytes; constant_drops.default=false)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
